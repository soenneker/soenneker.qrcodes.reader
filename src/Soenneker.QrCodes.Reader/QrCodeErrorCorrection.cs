namespace Soenneker.QrCodes.Reader;

/// <summary>The error correction level encoded in a QR symbol.</summary>
public enum QrCodeErrorCorrection
{
    /// <summary>Low, approximately 7 percent of codewords.</summary>
    Low,
    /// <summary>Medium, approximately 15 percent of codewords.</summary>
    Medium,
    /// <summary>Quartile, approximately 25 percent of codewords.</summary>
    Quartile,
    /// <summary>High, approximately 30 percent of codewords.</summary>
    High
}
