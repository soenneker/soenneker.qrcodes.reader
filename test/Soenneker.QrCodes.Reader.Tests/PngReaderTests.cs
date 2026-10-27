using System;
using System.IO;
using System.Linq;

namespace Soenneker.QrCodes.Reader.Tests;

public sealed class PngReaderTests
{
    private readonly QrCodesReader _reader = new();

    [Test]
    public void Reads_independent_png_color_depth_filter_and_interlace_fixtures()
    {
        string[] files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.png");
        if (files.Length != 35) throw new Exception($"Expected 35 fixtures, found {files.Length}.");
        foreach (string file in files)
        {
            QrCodeReadResult? result = _reader.ReadPng(File.ReadAllBytes(file));
            if (result?.Text != "PNG fixtures: Hello 世界 🌍") throw new Exception($"Failed to read {Path.GetFileName(file)}.");
        }
    }

    [Test]
    public void Reads_hanzi_and_structured_append_metadata()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modes");
        QrCodesReaderTests.Equal("汉字测试", _reader.ReadPng(File.ReadAllBytes(Path.Combine(directory, "hanzi.png"))));
        QrCodeReadResult first = _reader.ReadPng(File.ReadAllBytes(Path.Combine(directory, "sequence-0.png")))!;
        QrCodeReadResult second = _reader.ReadPng(File.ReadAllBytes(Path.Combine(directory, "sequence-1.png")))!;
        if (first == null || second == null || first.SequenceNumber != 0 || second.SequenceNumber != 1 ||
            first.SequenceCount != 2 || second.SequenceCount != 2 || first.SequenceParity == null || first.SequenceParity != second.SequenceParity ||
            first.Text + second.Text != string.Concat(Enumerable.Repeat("Structured append payload ", 4)))
            throw new Exception("Structured append decoding failed.");
    }

    [Test]
    public void Rejects_corruption_truncation_and_size_limits()
    {
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gray-8-0.png"));
        QrCodesReaderTests.Expect<InvalidDataException>(() => _reader.ReadPng([]));
        QrCodesReaderTests.Expect<InvalidDataException>(() => _reader.ReadPng(png.AsSpan(0, png.Length - 1)));
        QrCodesReaderTests.Expect<ArgumentException>(() => _reader.ReadPng(png, new QrCodeReaderOptions { MaxPixels = 100 }));
        byte[] corrupt = (byte[])png.Clone();
        corrupt[40] ^= 1;
        QrCodesReaderTests.Expect<InvalidDataException>(() => _reader.ReadPng(corrupt));
        QrCodesReaderTests.Expect<InvalidDataException>(() => _reader.ReadPng(png.Concat(new byte[] { 0 }).ToArray()));
    }
}
