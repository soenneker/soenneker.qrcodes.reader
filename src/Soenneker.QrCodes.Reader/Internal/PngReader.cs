using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Soenneker.QrCodes.Reader.Internal;

internal static class PngReader
{
    internal static GrayImage Decode(ReadOnlySpan<byte> png, int maxPixels)
    {
        if (png.Length > 64 * 1024 * 1024) throw new ArgumentException("PNG input exceeds 64 MiB.", nameof(png));
        if (png.Length < 8 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Invalid PNG signature.");
        int width = 0, height = 0, depth = 0, color = 0, channels = 0, interlace = 0;
        bool header = false, imageData = false, imageEnded = false, ended = false;
        byte[]? palette = null, transparency = null;
        using var compressed = new MemoryStream();
        int position = 8;
        while (position < png.Length)
        {
            if (png.Length - position < 12) throw new InvalidDataException("Truncated PNG chunk.");
            uint rawLength = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            if (rawLength > png.Length - position - 12) throw new InvalidDataException("Invalid PNG chunk length.");
            int length = (int)rawLength;
            ReadOnlySpan<byte> type = png.Slice(position + 4, 4);
            foreach (byte character in type)
                if (character is not (>= 65 and <= 90) and not (>= 97 and <= 122))
                    throw new InvalidDataException("Invalid PNG chunk type.");
            if ((type[2] & 32) != 0) throw new InvalidDataException("Invalid PNG reserved chunk bit.");
            ReadOnlySpan<byte> data = png.Slice(position + 8, length);
            uint crc = uint.MaxValue;
            foreach (byte b in type) crc = UpdateCrc(crc, b);
            foreach (byte b in data) crc = UpdateCrc(crc, b);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(png[(position + 8 + length)..]))
                throw new InvalidDataException("PNG checksum mismatch.");
            position += 12 + length;
            if (!header && !type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("IHDR must be first.");
            if (imageData && !type.SequenceEqual("IDAT"u8)) imageEnded = true;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13) throw new InvalidDataException("Invalid IHDR.");
                header = true;
                uint w = BinaryPrimitives.ReadUInt32BigEndian(data), h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (w == 0 || h == 0 || w > 32768 || h > 32768 || (ulong)w * h > (ulong)maxPixels)
                    throw new ArgumentException("PNG dimensions exceed the image limits.", nameof(png));
                width = (int)w; height = (int)h; depth = data[8]; color = data[9]; interlace = data[12];
                channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                bool validDepth = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => depth is 8 or 16 };
                if (channels == 0 || !validDepth || data[10] != 0 || data[11] != 0 || interlace > 1)
                    throw new InvalidDataException("Unsupported PNG header values.");
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (imageData || palette != null || transparency != null || length == 0 || length % 3 != 0 || length > 768 || color is 0 or 4 || (color == 3 && length / 3 > (1 << depth)))
                    throw new InvalidDataException("Invalid PNG palette.");
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (imageData || transparency != null || color is 4 or 6 || (color == 0 && length != 2) || (color == 2 && length != 6) ||
                    (color == 3 && (palette == null || length == 0 || length > palette.Length / 3)))
                    throw new InvalidDataException("Invalid PNG transparency.");
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (imageEnded || (color == 3 && palette == null)) throw new InvalidDataException("Invalid IDAT sequence.");
                imageData = true;
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!imageData || length != 0 || position != png.Length) throw new InvalidDataException("Invalid IEND.");
                ended = true;
                break;
            }
            else if ((type[0] & 32) == 0) throw new InvalidDataException("Unknown critical PNG chunk.");
        }
        if (!ended) throw new InvalidDataException("PNG has no IEND.");
        ReadOnlySpan<byte> zlibBytes = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        if (zlibBytes.Length < 6 || (zlibBytes[0] & 15) != 8 || zlibBytes[0] >> 4 > 7 ||
            ((zlibBytes[0] << 8) | zlibBytes[1]) % 31 != 0 || (zlibBytes[1] & 32) != 0)
            throw new InvalidDataException("Invalid PNG zlib header.");
        uint expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(zlibBytes[^4..]);
        uint adlerA = 1, adlerB = 0;
        var pixels = new byte[width * height];
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        try
        {
            int[] startX = interlace == 0 ? [0] : [0, 4, 0, 2, 0, 1, 0];
            int[] startY = interlace == 0 ? [0] : [0, 0, 4, 0, 2, 0, 1];
            int[] stepX = interlace == 0 ? [1] : [8, 8, 4, 4, 2, 2, 1];
            int[] stepY = interlace == 0 ? [1] : [8, 8, 8, 4, 4, 2, 2];
            int bytesPerPixel = Math.Max(1, (channels * depth + 7) / 8);
            for (int pass = 0; pass < startX.Length; pass++)
            {
                int passWidth = (width - startX[pass] + stepX[pass] - 1) / stepX[pass];
                int passHeight = (height - startY[pass] + stepY[pass] - 1) / stepY[pass];
                if (passWidth <= 0 || passHeight <= 0) continue;
                int rowBytes = (passWidth * channels * depth + 7) / 8;
                var previous = new byte[rowBytes];
                var row = new byte[rowBytes];
                for (int y = 0; y < passHeight; y++)
                {
                    int filter = zlib.ReadByte();
                    if (filter is < 0 or > 4) throw new InvalidDataException("Invalid PNG row filter.");
                    zlib.ReadExactly(row);
                    adlerA = (adlerA + (uint)filter) % 65521;
                    adlerB = (adlerB + adlerA) % 65521;
                    foreach (byte value in row)
                    {
                        adlerA = (adlerA + value) % 65521;
                        adlerB = (adlerB + adlerA) % 65521;
                    }
                    for (int i = 0; i < row.Length; i++)
                    {
                        int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                        int above = previous[i];
                        int upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                        int predictor = filter switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth(left, above, upperLeft) };
                        row[i] = unchecked((byte)(row[i] + predictor));
                    }
                    for (int x = 0; x < passWidth; x++)
                        pixels[(startY[pass] + y * stepY[pass]) * width + startX[pass] + x * stepX[pass]] =
                            Luminance(row, x, color, channels, depth, palette, transparency);
                    (row, previous) = (previous, row);
                }
            }
            if (zlib.ReadByte() != -1) throw new InvalidDataException("Excess PNG image data.");
            if ((adlerB << 16 | adlerA) != expectedAdler) throw new InvalidDataException("PNG zlib checksum mismatch.");
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated PNG image data.", e); }
        return new GrayImage(pixels, width, height);
    }

    private static byte Luminance(byte[] row, int x, int color, int channels, int depth, byte[]? palette, byte[]? transparency)
    {
        int Sample(int component)
        {
            int index = x * channels + component;
            if (depth == 16) return BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(index * 2));
            if (depth == 8) return row[index];
            int bit = index * depth;
            return (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
        }
        int max = (1 << depth) - 1;
        int red, green, blue, alpha = 255;
        if (color == 3)
        {
            int index = Sample(0);
            if (palette == null || index * 3 + 2 >= palette.Length) throw new InvalidDataException("Palette index out of range.");
            red = palette[index * 3]; green = palette[index * 3 + 1]; blue = palette[index * 3 + 2];
            if (transparency != null && index < transparency.Length) alpha = transparency[index];
        }
        else if (color is 0 or 4)
        {
            int gray = Sample(0);
            red = green = blue = gray * 255 / max;
            if (color == 4) alpha = Sample(1) * 255 / max;
            else if (transparency != null && gray == BinaryPrimitives.ReadUInt16BigEndian(transparency)) alpha = 0;
        }
        else
        {
            int r = Sample(0), g = Sample(1), b = Sample(2);
            red = r * 255 / max; green = g * 255 / max; blue = b * 255 / max;
            if (color == 6) alpha = Sample(3) * 255 / max;
            else if (transparency != null && r == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                g == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) && b == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) alpha = 0;
        }
        int luminance = (red * 299 + green * 587 + blue * 114 + 500) / 1000;
        return (byte)((luminance * alpha + 255 * (255 - alpha) + 127) / 255);
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int a = Math.Abs(estimate - left), b = Math.Abs(estimate - above), c = Math.Abs(estimate - upperLeft);
        return a <= b && a <= c ? left : b <= c ? above : upperLeft;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        return crc;
    }
}
