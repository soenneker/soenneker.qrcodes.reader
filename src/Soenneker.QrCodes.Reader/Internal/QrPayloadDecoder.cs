using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Soenneker.QrCodes.Reader.Internal;

internal static class QrPayloadDecoder
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    internal static QrCodeReadResult? Decode(byte[] data, int version, QrCodeErrorCorrection level, int corrected)
    {
        int bit = 0;
        int Read(int count)
        {
            if (count > data.Length * 8 - bit) throw new FormatException();
            int value = 0;
            for (int i = 0; i < count; i++, bit++) value = (value << 1) | ((data[bit / 8] >> (7 - bit % 8)) & 1);
            return value;
        }
        var text = new StringBuilder();
        var payload = new List<byte>();
        Encoding encoding = Encoding.Latin1;
        bool gs1 = false, fnc1 = false;
        byte? applicationIndicator = null;
        int? sequence = null, total = null;
        byte? parity = null;
        try
        {
            while (data.Length * 8 - bit >= 4)
            {
                int mode = Read(4);
                if (mode == 0) break;
                if (mode == 7)
                {
                    int eci = Read(8);
                    if ((eci & 0x80) == 0) { }
                    else if ((eci & 0xC0) == 0x80) eci = ((eci & 0x3F) << 8) | Read(8);
                    else if ((eci & 0xE0) == 0xC0) eci = ((eci & 0x1F) << 16) | Read(16);
                    else return null;
                    encoding = EciEncoding(eci) ?? throw new FormatException();
                    continue;
                }
                if (mode is 5 or 9)
                {
                    fnc1 = true;
                    if (mode == 5) gs1 = true;
                    else applicationIndicator = (byte)Read(8);
                    continue;
                }
                if (mode == 3)
                {
                    if (sequence != null) return null;
                    sequence = Read(4); total = Read(4) + 1; parity = (byte)Read(8);
                    if (sequence >= total) return null;
                    continue;
                }
                int group = version < 10 ? 0 : version < 27 ? 1 : 2;
                int countBits = mode switch
                {
                    1 => 10 + group * 2, 2 => 9 + group * 2, 4 => version < 10 ? 8 : 16,
                    8 or 13 => 8 + group * 2, _ => throw new FormatException()
                };
                if (mode == 13 && Read(4) != 1) return null; // Hanzi subset GB2312.
                int count = Read(countBits);
                if (mode == 4)
                {
                    var bytes = new byte[count];
                    for (int i = 0; i < count; i++) bytes[i] = (byte)Read(8);
                    payload.AddRange(bytes);
                    text.Append(encoding.GetString(bytes));
                }
                else if (mode is 8 or 13)
                {
                    var bytes = new byte[count * 2];
                    for (int i = 0; i < count; i++)
                    {
                        int value = Read(13);
                        int encoded;
                        if (mode == 8)
                        {
                            encoded = ((value / 0xC0) << 8) | (value % 0xC0);
                            encoded += encoded < 0x1F00 ? 0x8140 : 0xC140;
                        }
                        else
                        {
                            encoded = ((value / 0x60) << 8) | (value % 0x60);
                            encoded += encoded < 0x0A00 ? 0xA1A1 : 0xA6A1;
                        }
                        bytes[i * 2] = (byte)(encoded >> 8);
                        bytes[i * 2 + 1] = (byte)encoded;
                    }
                    payload.AddRange(bytes);
                    text.Append(LegacyEncoding(mode == 8 ? 932 : 936).GetString(bytes));
                }
                else
                {
                    var segment = new StringBuilder();
                    if (mode == 1)
                    {
                        while (count > 0)
                        {
                            int digits = Math.Min(3, count);
                            int value = Read(digits == 3 ? 10 : digits == 2 ? 7 : 4);
                            if (value >= (digits == 3 ? 1000 : digits == 2 ? 100 : 10)) return null;
                            segment.Append(value.ToString(digits == 3 ? "D3" : digits == 2 ? "D2" : "D1", CultureInfo.InvariantCulture));
                            count -= digits;
                        }
                    }
                    else
                    {
                        while (count >= 2)
                        {
                            int value = Read(11);
                            if (value >= 45 * 45) return null;
                            segment.Append(Alphabet[value / 45]).Append(Alphabet[value % 45]);
                            count -= 2;
                        }
                        if (count == 1)
                        {
                            int value = Read(6);
                            if (value >= 45) return null;
                            segment.Append(Alphabet[value]);
                        }
                    }
                    string valueText = segment.ToString();
                    if (fnc1 && mode == 2)
                    {
                        segment.Clear();
                        for (int i = 0; i < valueText.Length; i++)
                        {
                            if (valueText[i] != '%') segment.Append(valueText[i]);
                            else if (i + 1 < valueText.Length && valueText[i + 1] == '%') { segment.Append('%'); i++; }
                            else segment.Append('\u001D');
                        }
                        valueText = segment.ToString();
                    }
                    payload.AddRange(Encoding.ASCII.GetBytes(valueText));
                    text.Append(valueText);
                }
            }
            return new QrCodeReadResult
            {
                Text = text.ToString(), PayloadBytes = payload.ToArray(), Version = version,
                ErrorCorrection = level, CorrectedCodewords = corrected, IsGs1 = gs1,
                Fnc1ApplicationIndicator = applicationIndicator,
                SequenceNumber = sequence, SequenceCount = total, SequenceParity = parity
            };
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static Encoding LegacyEncoding(int codePage) =>
        CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new NotSupportedException();

    private static Encoding? EciEncoding(int eci) => eci switch
    {
        0 or 2 => LegacyEncoding(437),
        1 or 3 => Encoding.Latin1,
        >= 4 and <= 13 => LegacyEncoding(28590 + eci - 2),
        15 => LegacyEncoding(28603), 16 => LegacyEncoding(28604), 17 => LegacyEncoding(28605), 18 => LegacyEncoding(28606),
        20 => LegacyEncoding(932), 21 => LegacyEncoding(1250), 22 => LegacyEncoding(1251),
        23 => LegacyEncoding(1252), 24 => LegacyEncoding(1256),
        25 => new UnicodeEncoding(true, false, true), 26 => new UTF8Encoding(false, true),
        27 or 170 => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        28 => LegacyEncoding(950), 29 => LegacyEncoding(54936), 30 => LegacyEncoding(51949),
        _ => null
    };
}
