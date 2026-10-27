namespace Soenneker.QrCodes.Reader;

/// <summary>Immutable limits and detection settings. Reuse safely across calls.</summary>
public sealed record QrCodeReaderOptions
{
    /// <summary>Try light symbols on dark backgrounds as well as normal polarity. Defaults to true.</summary>
    public bool TryInverted { get; init; } = true;

    /// <summary>Try mirrored symbols in addition to rotations. Defaults to true.</summary>
    public bool TryMirrored { get; init; } = true;

    /// <summary>Maximum decoded image pixels, from 1 to 64,000,000. Defaults to 16,000,000.</summary>
    public int MaxPixels { get; init; } = 16_000_000;
}
