using System;

namespace Soenneker.QrCodes.Reader.Internal;

internal static class QrReedSolomon
{
    private static readonly byte[] Exp = new byte[512];
    private static readonly int[] Log = new int[256];

    static QrReedSolomon()
    {
        int value = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)value;
            Log[value] = i;
            value <<= 1;
            if (value >= 256) value ^= 0x11D;
        }
        for (int i = 255; i < Exp.Length; i++) Exp[i] = Exp[i - 255];
    }

    private static byte Multiply(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];
    private static byte Divide(byte a, byte b) => a == 0 ? (byte)0 : Exp[(Log[a] - Log[b] + 255) % 255];

    private static byte[] Syndromes(byte[] block, int count)
    {
        var result = new byte[count];
        for (int i = 0; i < count; i++)
            foreach (byte b in block)
                result[i] = (byte)(Multiply(result[i], Exp[i]) ^ b);
        return result;
    }

    internal static int Correct(byte[] block, int count)
    {
        byte[] syndromes = Syndromes(block, count);
        if (Array.TrueForAll(syndromes, b => b == 0)) return 0;

        // Berlekamp–Massey: locator coefficients are stored in increasing degree order.
        var locator = new byte[count + 1];
        var previous = new byte[count + 1];
        locator[0] = previous[0] = 1;
        int degree = 0, shift = 1;
        byte lastDiscrepancy = 1;
        for (int n = 0; n < count; n++)
        {
            byte discrepancy = syndromes[n];
            for (int i = 1; i <= degree; i++) discrepancy ^= Multiply(locator[i], syndromes[n - i]);
            if (discrepancy == 0) { shift++; continue; }
            byte[] saved = (byte[])locator.Clone();
            byte factor = Divide(discrepancy, lastDiscrepancy);
            for (int i = 0; i + shift < locator.Length; i++) locator[i + shift] ^= Multiply(factor, previous[i]);
            if (2 * degree <= n)
            {
                degree = n + 1 - degree;
                previous = saved;
                lastDiscrepancy = discrepancy;
                shift = 1;
            }
            else shift++;
        }
        if (degree == 0 || degree * 2 > count) return -1;

        var positions = new int[degree];
        var locations = new byte[degree];
        int found = 0;
        for (int position = 0; position < block.Length; position++)
        {
            int power = block.Length - 1 - position;
            byte inverse = Exp[(255 - power) % 255];
            byte value = locator[degree];
            for (int i = degree - 1; i >= 0; i--) value = (byte)(Multiply(value, inverse) ^ locator[i]);
            if (value != 0) continue;
            if (found == degree) return -1;
            positions[found] = position;
            locations[found++] = Exp[power];
        }
        if (found != degree) return -1;

        // Solve S_j = sum(error_i * location_i^j) over GF(256).
        var equations = new byte[degree, degree + 1];
        for (int col = 0; col < degree; col++)
        {
            byte power = 1;
            for (int row = 0; row < degree; row++)
            {
                equations[row, col] = power;
                power = Multiply(power, locations[col]);
            }
        }
        for (int row = 0; row < degree; row++) equations[row, degree] = syndromes[row];
        for (int col = 0; col < degree; col++)
        {
            int pivot = col;
            while (pivot < degree && equations[pivot, col] == 0) pivot++;
            if (pivot == degree) return -1;
            for (int j = col; j <= degree; j++)
                (equations[col, j], equations[pivot, j]) = (equations[pivot, j], equations[col, j]);
            byte divisor = equations[col, col];
            for (int j = col; j <= degree; j++) equations[col, j] = Divide(equations[col, j], divisor);
            for (int row = 0; row < degree; row++)
            {
                if (row == col) continue;
                byte factor = equations[row, col];
                for (int j = col; j <= degree; j++) equations[row, j] ^= Multiply(factor, equations[col, j]);
            }
        }
        for (int i = 0; i < degree; i++) block[positions[i]] ^= equations[i, degree];
        return Array.TrueForAll(Syndromes(block, count), b => b == 0) ? degree : -1;
    }
}
