using ComputeSharp;
using System.Threading;

namespace Aetherlight;

internal static class GpuPreviewRenderer
{
    private static GraphicsDevice? _device;
    private static readonly object Gate = new();
    private static bool _disabled;

    public static bool TryRender(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        double[] values,
        double baseTemperature,
        double baseTint,
        int maxWidth,
        CancellationToken token,
        out byte[] pixels,
        out int width,
        out int height)
    {
        pixels = Array.Empty<byte>();
        width = height = 0;
        if (_disabled || source.Length == 0 || sourceWidth <= 0 || sourceHeight <= 0) return false;

        try
        {
            token.ThrowIfCancellationRequested();
            width = Math.Min(maxWidth, sourceWidth);
            height = Math.Max(1, (int)Math.Round(sourceHeight * (width / (double)sourceWidth)));

            uint[] packed = DownsampleAndPack(source, sourceWidth, sourceHeight, width, height, token);
            uint[] result = new uint[packed.Length];

            GraphicsDevice device;
            lock (Gate)
            {
                _device ??= GraphicsDevice.GetDefault();
                device = _device;
            }

            token.ThrowIfCancellationRequested();
            using ReadOnlyBuffer<uint> input = device.AllocateReadOnlyBuffer(packed);
            using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(packed.Length);

            device.For(packed.Length, new DevelopPreviewShader(
                input,
                output,
                width,
                height,
                (float)Math.Pow(2, values[0]),
                (float)((259.0 * (values[1] + 255.0)) / (255.0 * (259.0 - values[1]))),
                (float)((values[6] - baseTemperature) / 100.0),
                (float)(values[7] / 100.0),
                (float)(1 + values[9] / 100.0),
                (float)(values[8] / 100.0),
                (float)(values[2] / 100.0),
                (float)(values[3] / 100.0),
                (float)(values[4] / 100.0),
                (float)(values[5] / 100.0)));

            token.ThrowIfCancellationRequested();
            output.CopyTo(result);
            token.ThrowIfCancellationRequested();

            pixels = new byte[result.Length * 4];
            for (int i = 0, p = 0; i < result.Length; i++, p += 4)
            {
                uint value = result[i];
                pixels[p] = (byte)(value & 255u);
                pixels[p + 1] = (byte)((value >> 8) & 255u);
                pixels[p + 2] = (byte)((value >> 16) & 255u);
                pixels[p + 3] = 255;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _disabled = true;
            pixels = Array.Empty<byte>();
            return false;
        }
    }

    private static uint[] DownsampleAndPack(byte[] source, int sourceWidth, int sourceHeight, int width, int height, CancellationToken token)
    {
        uint[] packed = new uint[checked(width * height)];
        double scaleX = sourceWidth / (double)width;
        double scaleY = sourceHeight / (double)height;

        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            int sy = Math.Min(sourceHeight - 1, (int)(y * scaleY));
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(sourceWidth - 1, (int)(x * scaleX));
                int sourceIndex = (sy * sourceWidth + sx) * 4;
                uint b = source[sourceIndex];
                uint g = source[sourceIndex + 1];
                uint r = source[sourceIndex + 2];
                packed[y * width + x] = b | (g << 8) | (r << 16) | 0xFF000000u;
            }
        });
        return packed;
    }

    // Public accessibility is required because ComputeSharp generates a public
    // shader descriptor for this type.
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct DevelopPreviewShader(
        ReadOnlyBuffer<uint> source,
        ReadWriteBuffer<uint> output,
        int width,
        int height,
        float exposure,
        float contrast,
        float temperature,
        float tint,
        float saturation,
        float vibrance,
        float highlights,
        float shadows,
        float whites,
        float blacks) : IComputeShader
    {
        public void Execute()
        {
            int index = (int)ThreadIds.X;
            int pixelCount = width * height;
            if (index >= pixelCount) return;

            uint packed = source[index];
            float b = (packed & 255u) / 255.0f;
            float g = ((packed >> 8) & 255u) / 255.0f;
            float r = ((packed >> 16) & 255u) / 255.0f;

            r *= exposure; g *= exposure; b *= exposure;
            r = (r - .5f) * contrast + .5f;
            g = (g - .5f) * contrast + .5f;
            b = (b - .5f) * contrast + .5f;

            float luma = .2126f * r + .7152f * g + .0722f * b;
            float shadowMask = Clamp(1 - luma * 2, 0, 1);
            float highlightMask = Clamp((luma - .5f) * 2, 0, 1);
            float whiteMask = Clamp((luma - .7f) / .3f, 0, 1);
            float blackMask = Clamp((.3f - luma) / .3f, 0, 1);
            float tonal = shadows * shadowMask * .35f + highlights * highlightMask * -.25f + whites * whiteMask * .25f + blacks * blackMask * -.25f;
            r += tonal; g += tonal; b += tonal;

            r += temperature * .10f;
            b -= temperature * .10f;
            r += tint * .03f;
            g -= tint * .03f;

            float gray = (r + g + b) / 3;
            float vibranceFactor = 1 + vibrance * (1 - Abs(gray - .5f) * 2);
            r = gray + (r - gray) * saturation * vibranceFactor;
            g = gray + (g - gray) * saturation * vibranceFactor;
            b = gray + (b - gray) * saturation * vibranceFactor;

            uint bb = (uint)(Clamp(b, 0, 1) * 255 + .5f);
            uint gg = (uint)(Clamp(g, 0, 1) * 255 + .5f);
            uint rr = (uint)(Clamp(r, 0, 1) * 255 + .5f);
            output[index] = bb | (gg << 8) | (rr << 16) | 0xFF000000u;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        private static float Abs(float value) => value < 0 ? -value : value;
    }
}
