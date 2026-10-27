using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.QrCodes.Reader.Registrars;
using Soenneker.QrCodes.Reader.Abstract;
using ZXing;
using ZXing.QrCode.Internal;

namespace Soenneker.QrCodes.Reader.Tests;

public sealed class QrCodesReaderTests
{
    static QrCodesReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private readonly QrCodesReader _reader = new();

    internal static bool[,] Encode(string text, int version = 0, ErrorCorrectionLevel? level = null, string charset = "UTF-8", int mask = -1)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = charset };
        if (version != 0) hints[EncodeHintType.QR_VERSION] = version;
        if (mask >= 0) hints[EncodeHintType.QR_MASK_PATTERN] = mask;
        QRCode code = ZXing.QrCode.Internal.Encoder.encode(text, level ?? ErrorCorrectionLevel.Q, hints);
        var result = new bool[code.Matrix.Height, code.Matrix.Width];
        for (int y = 0; y < result.GetLength(0); y++)
            for (int x = 0; x < result.GetLength(1); x++) result[y, x] = code.Matrix[x, y] == 1;
        return result;
    }

    internal static byte[] Image(bool[,] modules, int scale = 4, int border = 4)
    {
        int size = modules.GetLength(0), width = (size + border * 2) * scale;
        var pixels = Enumerable.Repeat((byte)255, width * width).ToArray();
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                int mx = x / scale - border, my = y / scale - border;
                if (mx >= 0 && my >= 0 && mx < size && my < size && modules[my, mx]) pixels[y * width + x] = 0;
            }
        return pixels;
    }

    internal static void Equal(string expected, QrCodeReadResult? actual)
    {
        if (actual?.Text != expected) throw new Exception($"Expected '{expected[..Math.Min(60, expected.Length)]}', got '{actual?.Text}'.");
    }

    [Test]
    public void All_versions_correction_levels_and_masks_decode()
    {
        ErrorCorrectionLevel[] levels = [ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H];
        for (int version = 1; version <= 40; version++)
            for (int level = 0; level < 4; level++)
            {
                var specification = ZXing.QrCode.Internal.Version.getVersionForNumber(version);
                int capacity = specification.TotalCodewords - specification.getECBlocksForLevel(levels[level]).TotalECCodewords - (version < 10 ? 3 : 4);
                string text = new string('x', capacity);
                bool[,] modules = Encode(text, version, levels[level], mask: (version + level) % 8);
                QrCodeReadResult? result = _reader.ReadModules(modules);
                Equal(text, result);
                if (result!.Version != version || (int)result.ErrorCorrection != level || result.CorrectedCodewords != 0)
                    throw new Exception($"Incorrect metadata for {version}/{level}.");
                if (level == 2)
                {
                    byte[] pixels = Image(modules, 2);
                    int width = (modules.GetLength(0) + 8) * 2;
                    if (_reader.ReadGrayscale(pixels, width, width)?.Text != text) throw new Exception($"Image failed for version {version}.");
                }
            }
    }

    [Test]
    [Arguments("012345678901234567890123456789")]
    [Arguments("HELLO WORLD 123 $%*+-./:")]
    [Arguments("Hello 世界 🌍 café")]
    [Arguments("https://soenneker.com/?a=1&b=2")]
    public void Reads_modes_and_images(string text)
    {
        bool[,] modules = Encode(text);
        Equal(text, _reader.ReadModules(modules));
        byte[] pixels = Image(modules);
        int width = (int)Math.Sqrt(pixels.Length);
        Equal(text, _reader.ReadGrayscale(pixels, width, width));
        byte[] rgba = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = pixels[i];
            rgba[i * 4 + 3] = pixels[i] == 0 ? (byte)255 : (byte)0;
        }
        Equal(text, _reader.ReadRgba(rgba, width, width));
    }

    [Test]
    public void Rotation_mirroring_and_inversion()
    {
        const string text = "Rotate and mirror 世界";
        bool[,] original = Encode(text);
        int size = original.GetLength(0);
        for (int rotation = 0; rotation < 4; rotation++)
            for (int mirror = 0; mirror < 2; mirror++)
                for (int invert = 0; invert < 2; invert++)
                {
                    var transformed = new bool[size, size];
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            int sx = mirror == 0 ? x : size - x - 1, sy = y;
                            for (int r = 0; r < rotation; r++) (sx, sy) = (size - sy - 1, sx);
                            transformed[y, x] = original[sy, sx] != (invert != 0);
                        }
                    Equal(text, _reader.ReadModules(transformed));
                    byte[] image = Image(transformed);
                    if (invert != 0)
                    {
                        int width = (size + 8) * 4;
                        for (int y = 0; y < width; y++)
                            for (int x = 0; x < width; x++)
                                if (x < 16 || y < 16 || x >= width - 16 || y >= width - 16) image[y * width + x] = 0;
                    }
                    var decoded = _reader.ReadGrayscale(image, (size + 8) * 4, (size + 8) * 4);
                    if (decoded?.Text != text) throw new Exception($"Image failed: rotation {rotation}, mirror {mirror}, inverted {invert}");
                }
    }

    [Test]
    public void Repairs_damaged_data_and_format_information()
    {
        const string text = "Repair";
        bool[,] modules = Encode(text, 1, ErrorCorrectionLevel.H);
        int size = modules.GetLength(0);
        // Distinct data codewords in the first upward two-column stripe.
        for (int i = 0; i < 6; i++) modules[size - 1 - i * 2, size - 1] = !modules[size - 1 - i * 2, size - 1];
        modules[0, 8] = !modules[0, 8];
        modules[1, 8] = !modules[1, 8];
        QrCodeReadResult? result = _reader.ReadModules(modules);
        Equal(text, result);
        if (result!.CorrectedCodewords == 0) throw new Exception("No corrections reported.");
    }

    [Test]
    [Arguments("漢字日本語", "Shift_JIS")]
    [Arguments("café bientôt", "ISO-8859-1")]
    [Arguments("Привет мир", "windows-1251")]
    [Arguments("你好世界", "GB18030")]
    public void Reads_legacy_encodings_and_kanji(string text, string charset) => Equal(text, _reader.ReadModules(Encode(text, charset: charset)));

    [Test]
    public void Reads_gs1_and_group_separators()
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.GS1_FORMAT] = true };
        QRCode encoded = ZXing.QrCode.Internal.Encoder.encode("ABC%DEF%%G", ErrorCorrectionLevel.M, hints);
        var modules = new bool[encoded.Matrix.Height, encoded.Matrix.Width];
        for (int y = 0; y < encoded.Matrix.Height; y++)
            for (int x = 0; x < encoded.Matrix.Width; x++) modules[y, x] = encoded.Matrix[x, y] == 1;
        QrCodeReadResult? result = _reader.ReadModules(modules);
        Equal("ABC\u001DDEF%G", result);
        if (!result!.IsGs1) throw new Exception("Missing GS1 metadata.");
    }

    [Test]
    [Arguments(0.0, 0.002)]
    [Arguments(0.3, 0.0)]
    [Arguments(0.75, 0.0)]
    [Arguments(0.2, 0.001)]
    public void Detects_rotated_and_perspective_images(double angle, double perspective)
    {
        const string text = "Camera perspective 世界";
        bool[,] modules = Encode(text, 5);
        int size = modules.GetLength(0);
        const int width = 500;
        var pixels = Enumerable.Repeat((byte)255, width * width).ToArray();
        double a = 6 * Math.Cos(angle), b = -6 * Math.Sin(angle), c = 180;
        double d = 6 * Math.Sin(angle), e = 6 * Math.Cos(angle), f = 40;
        double g = perspective, h = perspective / 2;
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                double aa = a - (x + 0.5) * g, bb = b - (x + 0.5) * h, cc = x + 0.5 - c;
                double dd = d - (y + 0.5) * g, ee = e - (y + 0.5) * h, ff = y + 0.5 - f;
                double determinant = aa * ee - bb * dd;
                int mx = (int)Math.Floor((cc * ee - bb * ff) / determinant) - 4;
                int my = (int)Math.Floor((aa * ff - cc * dd) / determinant) - 4;
                if (mx >= 0 && my >= 0 && mx < size && my < size && modules[my, mx]) pixels[y * width + x] = 0;
            }
        Equal(text, _reader.ReadGrayscale(pixels, width, width));
    }

    [Test]
    public void Invalid_inputs_and_blank_images()
    {
        Expect<ArgumentNullException>(() => _reader.ReadModules(null!));
        Expect<ArgumentException>(() => _reader.ReadModules(new bool[22, 22]));
        Expect<ArgumentException>(() => _reader.ReadGrayscale(new byte[10], 100, 100));
        Expect<ArgumentException>(() => _reader.ReadRgba(new byte[16], int.MaxValue, int.MaxValue));
        Expect<ArgumentOutOfRangeException>(() => _reader.ReadGrayscale([], 1, 1, new QrCodeReaderOptions { MaxPixels = 0 }));
        if (_reader.ReadGrayscale(new byte[10000], 100, 100) != null || _reader.ReadModules(new bool[21, 21]) != null)
            throw new Exception("Blank image decoded.");
    }

    [Test]
    public void Reads_uneven_lighting_and_low_resolution()
    {
        const string text = "Lighting";
        bool[,] modules = Encode(text);
        byte[] small = Image(modules, 1);
        int smallWidth = modules.GetLength(0) + 8;
        Equal(text, _reader.ReadGrayscale(small, smallWidth, smallWidth));
        byte[] pixels = Image(modules, 5);
        int width = smallWidth * 5;
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = (byte)(pixels[y * width + x] == 0 ? 20 + 180 * x / width : 90 + 160 * x / width);
        Equal(text, _reader.ReadGrayscale(pixels, width, width));
    }

    [Test]
    public void Singleton_is_safe_for_concurrent_calls()
    {
        var services = new ServiceCollection();
        services.AddQrCodesReaderAsSingleton();
        using ServiceProvider provider = services.BuildServiceProvider();
        IQrCodesReader reader = provider.GetRequiredService<IQrCodesReader>();
        bool[,] modules = Encode("Concurrent");
        Parallel.For(0, 16, _ => Equal("Concurrent", reader.ReadModules(modules)));
    }

    internal static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
