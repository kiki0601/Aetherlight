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

        if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".cr3", StringComparison.OrdinalIgnoreCase))
        {
            if (TryReadCanonCr3WhiteBalance(path, out int cr3Temp, out int cr3Tint))
            {
                _asShotTemperature = RoundTemperatureForDisplay(cr3Temp);
                _asShotTint = cr3Tint;
                _whiteBalanceMetadataFound = true;
            }
            else
            {
                TryReadGenericMetadata(path);
            }
        }
        else if (!string.IsNullOrWhiteSpace(path))
        {
            TryReadGenericMetadata(path);
        }

        _loading = true;
        TemperatureSlider.Value = 0;
        TintSlider.Value = 0;
        _loading = false;
        UpdateWhiteBalanceLabels();
    }

    private void TryReadGenericMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            double? temperature = null, tint = null;
            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string normalizedName = name.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

                if (temperature == null &&
                    (normalizedName.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) ||
                     normalizedName.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ExtractFirstNumber(description);
                    if (IsKelvin(value)) temperature = value;
                }

                if (tint == null &&
                    (normalizedName.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase) ||
                     normalizedName.Contains("WBShiftGreenMagenta", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ExtractSignedNumber(description);
                    if (IsTint(value)) tint = value;
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
        }
        catch
        {
            // Metadata is optional. RAW development must continue if it cannot be read.
        }
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

            // First try MetadataExtractor's Canon maker-note representation. On current
            // Canon CR3 files this is the cleanest route when the nested TIFF directory
            // has been exposed by the library.
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(path);
                double? metadataTemp = null;
                double? metadataTint = null;
                foreach (var directory in directories)
                foreach (var tag in directory.Tags)
                {
                    string name = tag.Name.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
                    string description = tag.Description ?? string.Empty;
                    if (metadataTemp == null && name.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ExtractFirstNumber(description);
                        if (IsKelvin(value)) metadataTemp = value;
                    }
                    if (metadataTint == null && name.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ExtractSignedNumber(description);
                        if (IsTint(value)) metadataTint = value;
                    }
                }
                if (metadataTemp.HasValue)
                {
                    temperature = (int)Math.Round(metadataTemp.Value);
                    tint = metadataTint.HasValue ? (int)Math.Round(metadataTint.Value) : 0;
                    return true;
                }
            }
            catch
            {
                // Fall through to the binary ColorData reader.
            }

            // Canon stores ColorData11 in MakerNotes tag 0x4001. ExifTool documents
            // R6 Mark II/R7/R50/R3 ColorData11 index 0 as the version and index 109
            // as ColorTempAsShot. The R6 Mark II uses version 48. The tag is a binary
            // int16 array inside the CR3 TIFF/MakerNote structure, so do not assume that
            // the TIFF entry itself uses SHORT type. Some CR3 writers expose it as UNDEFINED.
            if (TryReadCanonColorDataArray(bytes, 48, 109, out int temp48))
            {
                temperature = temp48;
                // The Canon ProcessingInfo green/magenta shift is optional. Keep zero
                // here when the file does not expose a reliable camera shift value.
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }

            // EOS R3 uses the same ColorData11 layout with version 34.
            if (TryReadCanonColorDataArray(bytes, 34, 109, out int temp34))
            {
                temperature = temp34;
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }

            // Last-resort scan for the ColorData11 payload itself. This deliberately
            // validates the version, the four as-shot WB coefficients at index 105,
            // and the Kelvin value at index 109 before accepting a match. This avoids
            // depending on a particular CR3 TIFF nesting/offset layout.
            if (TryScanCanonColorData(bytes, out int scannedTemp))
            {
                temperature = scannedTemp;
                TryReadCanonProcessingTint(bytes, out tint);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadCanonColorDataArray(byte[] bytes, short expectedVersion, int tempIndex, out int temperature)
    {
        temperature = 0;
        try
        {
            // MakerNotes ColorData is an int16 array. Accept either TIFF SHORT (3)
            // or UNDEFINED (7), because CR3 containers can expose the same payload
            // through different TIFF representations.
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
                    if (byteCount <= 4)
                    {
                        valueOffset = entry + 8;
                    }
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
            // Continue to the raw payload scan.
        }
        return false;
    }

    private static bool TryScanCanonColorData(byte[] bytes, out int temperature)
    {
        temperature = 0;
        const int versionOffset = 0;
        const int wbOffset = 105 * 2;
        const int tempOffset = 109 * 2;

        for (int i = 0; i + tempOffset + 2 <= bytes.Length; i += 2)
        {
            short version = ReadI16(bytes, i + versionOffset);
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
        // ProcessingInfo is a small int16 record. Search for a plausible Kelvin value
        // followed by a plausible green/magenta shift at Canon's known indexes.
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
        // Guarantee that the metadata reader is invoked when the current photo changes.
        // The previous implementation could leave the UI at its 6500/0 defaults because
        // the import path changed without calling LoadAsShotWhiteBalance.
        if (!_loading && !string.IsNullOrWhiteSpace(_currentPhotoPath) &&
            !string.Equals(_whiteBalanceLoadedPath, _currentPhotoPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadAsShotWhiteBalance(_currentPhotoPath);
            return;
        }

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
