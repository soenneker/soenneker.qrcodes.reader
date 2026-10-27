using System;
using Soenneker.QrCodes.Reader.Abstract;
using Soenneker.QrCodes.Reader.Internal;

namespace Soenneker.QrCodes.Reader;

public sealed class QrCodesReader : IQrCodesReader
{
    public QrCodeReadResult? ReadModules(bool[,] modules, QrCodeReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        options = Validate(options);
        int size = modules.GetLength(0);
        if (size != modules.GetLength(1) || size is < 21 or > 177 || (size - 21) % 4 != 0)
            throw new ArgumentException("Expected a square Model 2 grid, without its quiet zone.", nameof(modules));
        QrCodeReadResult? result = QrMatrixDecoder.Decode(modules, options.TryMirrored);
        if (result != null || !options.TryInverted) return result;
        var inverted = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) inverted[y, x] = !modules[y, x];
        return QrMatrixDecoder.Decode(inverted, options.TryMirrored);
    }

    public QrCodeReadResult? ReadGrayscale(ReadOnlySpan<byte> pixels, int width, int height, QrCodeReaderOptions? options = null)
    {
        options = Validate(options);
        ValidateImage(pixels.Length, width, height, 1, options);
        return QrDetector.Read(pixels, width, height, options);
    }

    public QrCodeReadResult? ReadRgba(ReadOnlySpan<byte> pixels, int width, int height, QrCodeReaderOptions? options = null)
    {
        options = Validate(options);
        ValidateImage(pixels.Length, width, height, 4, options);
        var gray = new byte[width * height];
        for (int i = 0; i < gray.Length; i++)
        {
            int offset = i * 4;
            int luminance = (pixels[offset] * 299 + pixels[offset + 1] * 587 + pixels[offset + 2] * 114 + 500) / 1000;
            gray[i] = (byte)((luminance * pixels[offset + 3] + 255 * (255 - pixels[offset + 3]) + 127) / 255);
        }
        return QrDetector.Read(gray, width, height, options);
    }

    public QrCodeReadResult? ReadPng(ReadOnlySpan<byte> png, QrCodeReaderOptions? options = null)
    {
        options = Validate(options);
        GrayImage image = PngReader.Decode(png, options.MaxPixels);
        return QrDetector.Read(image.Pixels, image.Width, image.Height, options);
    }

    private static QrCodeReaderOptions Validate(QrCodeReaderOptions? options)
    {
        options ??= new QrCodeReaderOptions();
        if (options.MaxPixels is < 1 or > 64_000_000)
            throw new ArgumentOutOfRangeException(nameof(options.MaxPixels));
        return options;
    }

    private static void ValidateImage(int length, int width, int height, int channels, QrCodeReaderOptions options)
    {
        long count = (long)width * height;
        if (width <= 0 || height <= 0 || count > options.MaxPixels || count * channels != length)
            throw new ArgumentException("Image dimensions, pixel buffer length, or pixel limit are invalid.");
    }
}
