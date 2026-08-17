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

    private void LoadAsShotWhiteBalance(string path)
    {
        _asShotTemperature = 6500;
        _asShotTint = 0;
        _whiteBalanceMetadataFound = false;
        if (path.EndsWith(".cr3", StringComparison.OrdinalIgnoreCase) && TryReadCanonCr3WhiteBalance(path, out int cr3Temp, out int cr3Tint))
        {
            _asShotTemperature = RoundTemperatureForDisplay(cr3Temp);
            _asShotTint = cr3Tint;
            _whiteBalanceMetadataFound = true;
        }
        else TryReadGenericMetadata(path);
        _loading = true; TemperatureSlider.Value = 0; TintSlider.Value = 0; _loading = false; UpdateWhiteBalanceLabels();
    }

    private void TryReadGenericMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path); double? temperature = null, tint = null;
            foreach (var directory in directories) foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim(), description = (tag.Description ?? string.Empty).Trim();
                string normalizedName = name.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
                if (temperature == null && (normalizedName.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) || normalizedName.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase) || name.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase))) { var value = ExtractFirstNumber(description); if (IsKelvin(value)) temperature = value; }
                if (tint == null && (normalizedName.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase) || normalizedName.Contains("WBShiftGreenMagenta", StringComparison.OrdinalIgnoreCase) || name.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase))) { var value = ExtractSignedNumber(description); if (IsTint(value)) tint = value; }
            }
            if (temperature.HasValue) { _asShotTemperature = RoundTemperatureForDisplay((int)Math.Round(temperature.Value)); _whiteBalanceMetadataFound = true; }
            if (tint.HasValue) { _asShotTint = (int)Math.Round(tint.Value); _whiteBalanceMetadataFound = true; }
        }
        catch { }
    }

    private static int RoundTemperatureForDisplay(int kelvin) => (int)(Math.Round(kelvin / 100.0, MidpointRounding.AwayFromZero) * 100);

    private static bool TryReadCanonCr3WhiteBalance(string path, out int temperature, out int tint)
    {
        temperature = 0; tint = 0;
        try
        {
            byte[] bytes = File.ReadAllBytes(path); int foundTemp = 0, foundTint = 0; bool tempFound = false, tintFound = false;
            for (int tiffStart = 0; tiffStart <= bytes.Length - 8; tiffStart++)
            {
                if (bytes[tiffStart] != (byte)'I' || bytes[tiffStart + 1] != (byte)'I' || bytes[tiffStart + 2] != 42 || bytes[tiffStart + 3] != 0) continue;
                uint ifdOffset = ReadU32(bytes, tiffStart + 4); if (ifdOffset > bytes.Length - tiffStart - 2) continue;
                int ifd = checked(tiffStart + (int)ifdOffset); ushort entryCount = ReadU16(bytes, ifd); if (entryCount == 0 || entryCount > 10000 || ifd + 2 + entryCount * 12 > bytes.Length) continue;
                for (int n = 0; n < entryCount; n++)
                {
                    int entry = ifd + 2 + n * 12; ushort tag = ReadU16(bytes, entry), type = ReadU16(bytes, entry + 2); uint count = ReadU32(bytes, entry + 4);
                    if (type != 3 || count == 0 || count > 10000) continue; int byteCount = checked((int)count * 2), valueOffset;
                    if (byteCount <= 4) valueOffset = entry + 8; else { uint relative = ReadU32(bytes, entry + 8); if (relative > bytes.Length - tiffStart) continue; valueOffset = checked(tiffStart + (int)relative); }
                    if (valueOffset < 0 || valueOffset + byteCount > bytes.Length) continue;
                    if (tag == 0x4001 && count >= 3000)
                    {
                        int version = ReadI16(bytes, valueOffset); int tempIndex = version switch { 48 or 34 => 109, 33 or 32 => 89, 19 or 18 or 17 or 16 => 75, _ => -1 };
                        if (tempIndex >= 0 && tempIndex * 2 + 2 <= byteCount) { int value = ReadI16(bytes, valueOffset + tempIndex * 2); if (IsKelvin(value)) { foundTemp = value; tempFound = true; } }
                    }
                    if (tag == 0x00A0 && count == 19 && byteCount == 38)
                    {
                        int processingTemp = ReadI16(bytes, valueOffset + 9 * 2), processingTint = ReadI16(bytes, valueOffset + 13 * 2);
                        if (IsKelvin(processingTemp) && IsTint(processingTint)) { foundTint = processingTint; tintFound = true; }
                    }
                }
                if (tempFound && tintFound) break;
            }
            if (!tempFound) return false; temperature = foundTemp; tint = tintFound ? foundTint : 0; return true;
        }
        catch { return false; }
    }

    private static ushort ReadU16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static short ReadI16(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint ReadU32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static bool IsKelvin(double? value) => value.HasValue && value.Value >= 1500 && value.Value <= 20000;
    private static bool IsKelvin(int value) => value >= 1500 && value <= 20000;
    private static bool IsTint(double? value) => value.HasValue && value.Value >= -100 && value.Value <= 100;
    private static bool IsTint(int value) => value >= -100 && value <= 100;
    private void UpdateWhiteBalanceLabels() { int currentTemperature = _asShotTemperature + (int)Math.Round(TemperatureSlider.Value); int currentTint = _asShotTint + (int)Math.Round(TintSlider.Value); TemperatureValue.Text = $"{currentTemperature} K"; TintValue.Text = currentTint.ToString("+0;-0;0", CultureInfo.InvariantCulture); }
    private static double? ExtractFirstNumber(string text) { var match = System.Text.RegularExpressions.Regex.Match(text, @"[-+]?\d+(?:[\.,]\d+)?"); if (!match.Success) return null; return double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null; }
    private static double? ExtractSignedNumber(string text) { var matches = System.Text.RegularExpressions.Regex.Matches(text, @"[-+]?\d+(?:[\.,]\d+)?"); foreach (System.Text.RegularExpressions.Match match in matches) if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value; return null; }
}
