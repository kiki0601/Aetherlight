using System.Globalization;
using System.Buffers.Binary;
using System.IO;
using MetadataExtractor;

namespace Aetherlight;

public partial class MainWindow
{
    private int _asShotTemperature = 6500;
    private int _asShotTint = 0;
    private bool _whiteBalanceMetadataFound;
    private string _whiteBalanceLoadedPath = string.Empty;

    private void LoadAsShotWhiteBalance(string path)
    {
        _asShotTemperature = 6500;
        _asShotTint = 0;
        _whiteBalanceMetadataFound = false;
        _whiteBalanceLoadedPath = path ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(path))
        {
            // IMPORTANT: MetadataExtractor is the primary source. The previous
            // implementation tried the hand-written CR3 scanner first, and that
            // scanner could find an unrelated valid 6500K value elsewhere in the
            // CR3 and stop before the real ColorTempAsShot tag was examined.
            bool metadataRead = TryReadGenericMetadata(path);

            // Only use the binary Canon reader when the metadata library did not
            // give us a reliable as-shot temperature.
            if (!metadataRead && path.EndsWith(".cr3", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadCanonCr3WhiteBalance(path, out int cr3Temp, out int cr3Tint))
                {
                    _asShotTemperature = RoundTemperatureForDisplay(cr3Temp);
                    _asShotTint = cr3Tint;
                    _whiteBalanceMetadataFound = true;
                }
            }
        }

        _loading = true;
        TemperatureSlider.Value = 0;
        TintSlider.Value = 0;
        _loading = false;
        UpdateWhiteBalanceLabels();
    }

    private bool TryReadGenericMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);

            double? temperature = null;
            double? tint = null;
            int temperatureScore = -1;
            int tintScore = -1;

            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string normalized = NormalizeTagName(name);

                // Canon CR3 can expose several temperature-related fields. We must
                // rank them instead of taking the first one encountered.
                int tempScore = GetTemperatureTagScore(normalized, name);
                if (tempScore >= 0)
                {
                    var value = ExtractFirstNumber(description);
                    if (IsKelvin(value) && tempScore > temperatureScore)
                    {
                        temperature = value;
                        temperatureScore = tempScore;
                    }
                }

                int tintScoreCandidate = GetTintTagScore(normalized, name);
                if (tintScoreCandidate >= 0)
                {
                    var value = ExtractSignedNumber(description);
                    if (IsTint(value) && tintScoreCandidate > tintScore)
                    {
                        tint = value;
                        tintScore = tintScoreCandidate;
                    }
                }
            }

            if (temperature.HasValue)
            {
                _asShotTemperature = RoundTemperatureForDisplay((int)Math.Round(temperature.Value));
                _whiteBalanceMetadataFound = true;
            }

            if (tint.HasValue)
            {
                _asShotTint = (int)Math.Round(tint.Value);
                _whiteBalanceMetadataFound = true;
            }

            return temperature.HasValue;
        }
        catch
        {
            // Metadata is optional. RAW development must continue if it cannot be read.
            return false;
        }
    }

    private static string NormalizeTagName(string name) =>
        name.Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("/", string.Empty);

    private static int GetTemperatureTagScore(string normalized, string originalName)
    {
        // Highest priority: the actual as-shot Canon field.
        if (normalized.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase)) return 100;

        // Other explicit Kelvin/as-shot fields are useful fallbacks.
        if (normalized.Contains("ColorTempKelvin", StringComparison.OrdinalIgnoreCase)) return 90;
        if (normalized.Contains("ColorTemperatureKelvin", StringComparison.OrdinalIgnoreCase)) return 90;
        if (normalized.Contains("ColorTempMeasured", StringComparison.OrdinalIgnoreCase)) return 70;
        if (normalized.Equals("ColorTemperature", StringComparison.OrdinalIgnoreCase)) return 60;
        if (originalName.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase)) return 50;
        return -1;
    }

    private static int GetTintTagScore(string normalized, string originalName)
    {
        if (normalized.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("WBShiftGreenMagenta", StringComparison.OrdinalIgnoreCase)) return 100;
        if (normalized.Contains("GreenMagenta", StringComparison.OrdinalIgnoreCase)) return 90;
        if (originalName.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase)) return 80;
        return -1;
    }

    private static int RoundTemperatureForDisplay(int kelvin) =>
        (int)(Math.Round(kelvin / 100.0, MidpointRounding.AwayFromZero) * 100);

    private static bool TryReadCanonCr3WhiteBalance(string path, out int temperature, out int tint)
    {
        temperature = 0;
        tint = 0;

        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            // Canon ColorData11. Accept both SHORT and UNDEFINED representations.
            if (TryReadCanonColorDataArray(bytes, 48, 109, out int temp48))
            {
                temperature = temp48;
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }

            if (TryReadCanonColorDataArray(bytes, 34, 109, out int temp34))
            {
                temperature = temp34;
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }

            // Last-resort payload scan, with strict WB coefficient validation.
            if (TryScanCanonColorData(bytes, out int scannedTemp))
            {
                temperature = scannedTemp;
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }
        }
        catch
        {
            // Ignore optional RAW metadata errors.
        }

        return false;
    }

    private static bool TryReadCanonColorDataArray(byte[] bytes, short expectedVersion, int tempIndex, out int temperature)
    {
        temperature = 0;
        try
        {
            for (int tiffStart = 0; tiffStart <= bytes.Length - 8; tiffStart++)
            {
                if (bytes[tiffStart] != (byte)'I' || bytes[tiffStart + 1] != (byte)'I' ||
                    bytes[tiffStart + 2] != 42 || bytes[tiffStart + 3] != 0)
                    continue;

                uint ifdOffset = ReadU32(bytes, tiffStart + 4);
                if (ifdOffset > bytes.Length - tiffStart - 2) continue;
                int ifd = checked(tiffStart + (int)ifdOffset);
                if (ifd < 0 || ifd + 2 > bytes.Length) continue;
                ushort entryCount = ReadU16(bytes, ifd);
                if (entryCount == 0 || entryCount > 10000 || ifd + 2 + entryCount * 12 > bytes.Length) continue;

                for (int n = 0; n < entryCount; n++)
                {
                    int entry = ifd + 2 + n * 12;
                    ushort tag = ReadU16(bytes, entry);
                    ushort type = ReadU16(bytes, entry + 2);
                    uint count = ReadU32(bytes, entry + 4);
                    if (tag != 0x4001 || (type != 3 && type != 7) || count < 1000) continue;

                    int byteCount = type == 3 ? checked((int)count * 2) : checked((int)count);
                    int valueOffset;
                    if (byteCount <= 4) valueOffset = entry + 8;
                    else
                    {
                        uint relative = ReadU32(bytes, entry + 8);
                        if (relative > bytes.Length - tiffStart) continue;
                        valueOffset = checked(tiffStart + (int)relative);
                    }

                    if (valueOffset < 0 || valueOffset + byteCount > bytes.Length) continue;
                    int version = ReadI16(bytes, valueOffset);
                    if (version != expectedVersion) continue;

                    int tempOffset = valueOffset + tempIndex * 2;
                    if (tempOffset + 2 > bytes.Length) continue;
                    int value = ReadI16(bytes, tempOffset);
                    if (!IsKelvin(value)) continue;

                    temperature = value;
                    return true;
                }
            }
        }
        catch
        {
            // Fall through to the raw payload scan.
        }
        return false;
    }

    private static bool TryScanCanonColorData(byte[] bytes, out int temperature)
    {
        temperature = 0;
        const int wbOffset = 105 * 2;
        const int tempOffset = 109 * 2;

        for (int i = 0; i + tempOffset + 2 <= bytes.Length; i += 2)
        {
            short version = ReadI16(bytes, i);
            if (version != 48 && version != 34) continue;

            int r = ReadI16(bytes, i + wbOffset);
            int g1 = ReadI16(bytes, i + wbOffset + 2);
            int g2 = ReadI16(bytes, i + wbOffset + 4);
            int b = ReadI16(bytes, i + wbOffset + 6);
            int temp = ReadI16(bytes, i + tempOffset);

            if (!IsPlausibleWbCoefficient(r) || !IsPlausibleWbCoefficient(g1) ||
                !IsPlausibleWbCoefficient(g2) || !IsPlausibleWbCoefficient(b)) continue;
            if (Math.Abs(g1 - g2) > 200) continue;
            if (!IsKelvin(temp)) continue;

            temperature = temp;
            return true;
        }
        return false;
    }

    private static bool IsPlausibleWbCoefficient(int value) => value >= 200 && value <= 20000;

    private static bool TryReadCanonProcessingTint(byte[] bytes, out int tint)
    {
        tint = 0;
        for (int i = 0; i + 38 <= bytes.Length; i += 2)
        {
            int processingTemp = ReadI16(bytes, i + 18);
            int processingTint = ReadI16(bytes, i + 26);
            if (IsKelvin(processingTemp) && IsTint(processingTint))
            {
                tint = processingTint;
                return true;
            }
        }
        return false;
    }

    private static ushort ReadU16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static short ReadI16(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint ReadU32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static bool IsKelvin(double? value) => value.HasValue && value.Value >= 1500 && value.Value <= 20000;
    private static bool IsKelvin(int value) => value >= 1500 && value <= 20000;
    private static bool IsTint(double? value) => value.HasValue && value.Value >= -100 && value.Value <= 100;
    private static bool IsTint(int value) => value >= -100 && value <= 100;

    private void UpdateWhiteBalanceLabels()
    {
        int currentTemperature = _asShotTemperature + (int)Math.Round(TemperatureSlider.Value);
        int currentTint = _asShotTint + (int)Math.Round(TintSlider.Value);
        TemperatureValue.Text = $"{currentTemperature} K";
        TintValue.Text = currentTint.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    }

    private static double? ExtractFirstNumber(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[-+]?\d+(?:[\.,]\d+)?");
        if (!match.Success) return null;
        return double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static double? ExtractSignedNumber(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"[-+]?\d+(?:[\.,]\d+)?");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return null;
    }
}
