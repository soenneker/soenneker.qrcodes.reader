namespace Soenneker.QrCodes.Reader;

/// <summary>A decoded QR Model 2 symbol. Each result and its payload array belong to the caller.</summary>
public sealed record QrCodeReadResult
{
    /// <summary>Decoded text. Byte segments without an ECI use ISO-8859-1.</summary>
    public required string Text { get; init; }

    /// <summary>Concatenated segment bytes: original bytes for byte/Kanji/Hanzi segments,
    /// ASCII for numeric and alphanumeric segments. Mode and ECI headers are excluded.</summary>
    public required byte[] PayloadBytes { get; init; }

    /// <summary>QR version, from 1 through 40.</summary>
    public required int Version { get; init; }

    /// <summary>The symbol's declared correction level.</summary>
    public required QrCodeErrorCorrection ErrorCorrection { get; init; }

    /// <summary>Number of codewords repaired by Reed–Solomon correction.</summary>
    public int CorrectedCodewords { get; init; }

    /// <summary>Whether the symbol carries an FNC1 first-position (GS1) header.</summary>
    public bool IsGs1 { get; init; }

    /// <summary>Application indicator from an FNC1 second-position header, or null.</summary>
    public byte? Fnc1ApplicationIndicator { get; init; }

    /// <summary>Zero-based structured-append sequence number, or null for a standalone symbol.</summary>
    public int? SequenceNumber { get; init; }

    /// <summary>Number of symbols in the structured-append sequence, or null.</summary>
    public int? SequenceCount { get; init; }

    /// <summary>Structured-append parity byte, or null. Sequence assembly is the caller's responsibility.</summary>
    public byte? SequenceParity { get; init; }
}
