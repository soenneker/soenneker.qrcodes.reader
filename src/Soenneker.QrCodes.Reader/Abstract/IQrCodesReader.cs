using System;

namespace Soenneker.QrCodes.Reader.Abstract;

/// <summary>
/// Reads QR Model 2 symbols using managed code. Instances support concurrent calls.
/// Returns null when no supported, recoverable symbol is found; invalid input containers and dimensions throw.
/// </summary>
public interface IQrCodesReader
{
    /// <summary>Reads the first decodable QR symbol in a PNG image, compositing transparency on white.</summary>
    /// <param name="png">A complete PNG file, up to 64 MiB. Supports static PNG and the default APNG image.</param>
    /// <param name="options">Detection settings, or null for defaults.</param>
    /// <returns>The decoded symbol, or null when none can be decoded.</returns>
    /// <exception cref="System.IO.InvalidDataException">PNG structure, checksum, or compressed image data is invalid.</exception>
    /// <exception cref="ArgumentException">Input exceeds the size limit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its supported range.</exception>
    QrCodeReadResult? ReadPng(ReadOnlySpan<byte> png, QrCodeReaderOptions? options = null);

    /// <summary>Reads an 8-bit grayscale image with 0 representing black and 255 white.</summary>
    /// <param name="pixels">Row-major pixels. Length must be exactly width times height.</param>
    /// <param name="width">Positive image width.</param>
    /// <param name="height">Positive image height.</param>
    /// <param name="options">Detection settings, or null for defaults.</param>
    /// <returns>The first decoded symbol, or null.</returns>
    /// <exception cref="ArgumentException">Dimensions, buffer length, or image size are invalid.</exception>
    QrCodeReadResult? ReadGrayscale(ReadOnlySpan<byte> pixels, int width, int height, QrCodeReaderOptions? options = null);

    /// <summary>Reads packed 8-bit RGBA pixels, compositing alpha on white.</summary>
    /// <param name="pixels">Row-major R,G,B,A bytes. Length must be exactly width times height times four.</param>
    /// <param name="width">Positive image width.</param>
    /// <param name="height">Positive image height.</param>
    /// <param name="options">Detection settings, or null for defaults.</param>
    /// <returns>The first decoded symbol, or null.</returns>
    /// <exception cref="ArgumentException">Dimensions, buffer length, or image size are invalid.</exception>
    QrCodeReadResult? ReadRgba(ReadOnlySpan<byte> pixels, int width, int height, QrCodeReaderOptions? options = null);

    /// <summary>Decodes a sampled square QR grid, without its quiet zone. Indexed [row, column]; true is dark.</summary>
    /// <param name="modules">A 21–177 module square with a valid Model 2 dimension (21 + 4n).</param>
    /// <param name="options">Polarity and mirroring settings, or null for defaults.</param>
    /// <returns>The decoded symbol, or null when the symbol is damaged beyond recovery or uses unsupported encoding.</returns>
    /// <exception cref="ArgumentException">The grid is null or its dimensions are invalid.</exception>
    QrCodeReadResult? ReadModules(bool[,] modules, QrCodeReaderOptions? options = null);
}
