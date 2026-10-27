using System;
using System.Numerics;

namespace Soenneker.QrCodes.Reader.Internal;

internal static class QrMatrixDecoder
{
    internal static QrCodeReadResult? Decode(bool[,] modules, bool mirrored)
    {
        int size = modules.GetLength(0);
        for (int mirror = 0; mirror < (mirrored ? 2 : 1); mirror++)
            for (int rotation = 0; rotation < 4; rotation++)
            {
                var grid = new bool[size, size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int sx = mirror == 0 ? x : size - x - 1, sy = y;
                        for (int r = 0; r < rotation; r++) (sx, sy) = (size - sy - 1, sx);
                        grid[y, x] = modules[sy, sx];
                    }
                QrCodeReadResult? result = DecodeUpright(grid);
                if (result != null) return result;
            }
        return null;
    }

    private static QrCodeReadResult? DecodeUpright(bool[,] grid)
    {
        int size = grid.GetLength(0), version = (size - 17) / 4;
        int first = 0, second = 0;
        void Add(int bit, int x, int y, ref int value) { if (grid[y, x]) value |= 1 << bit; }
        for (int i = 0; i < 6; i++) Add(i, 8, i, ref first);
        Add(6, 8, 7, ref first); Add(7, 8, 8, ref first); Add(8, 7, 8, ref first);
        for (int i = 9; i < 15; i++) Add(i, 14 - i, 8, ref first);
        for (int i = 0; i < 8; i++) Add(i, size - 1 - i, 8, ref second);
        for (int i = 8; i < 15; i++) Add(i, 8, size - 15 + i, ref second);
        int format = -1, distance = 4;
        for (int candidate = 0; candidate < 32; candidate++)
        {
            int encoded = Bch(candidate, 10, 0x537) ^ 0x5412;
            int difference = Math.Min(BitOperations.PopCount((uint)(encoded ^ first)), BitOperations.PopCount((uint)(encoded ^ second)));
            if (difference < distance) { distance = difference; format = candidate; }
        }
        if (format < 0) return null;
        if (version >= 7)
        {
            first = second = 0;
            for (int bit = 0; bit < 18; bit++)
            {
                Add(bit, size - 11 + bit % 3, bit / 3, ref first);
                Add(bit, bit / 3, size - 11 + bit % 3, ref second);
            }
            int expected = Bch(version, 12, 0x1F25);
            if (Math.Min(BitOperations.PopCount((uint)(expected ^ first)), BitOperations.PopCount((uint)(expected ^ second))) > 3) return null;
        }
        int level = (format >> 3) switch { 1 => 0, 0 => 1, 3 => 2, _ => 3 };
        int mask = format & 7;
        int[] layout = QrBlockTable.Rows[(version - 1) * 4 + level];
        int count = layout[2] + layout[4];
        var codewords = new byte[layout[0] + count * layout[1]];
        bool[,] reserved = Reserved(version);
        int position = 0;
        bool up = true;
        for (int right = size - 1; right > 0; right -= 2)
        {
            if (right == 6) right--;
            for (int row = 0; row < size; row++)
            {
                int y = up ? size - row - 1 : row;
                for (int x = right; x >= right - 1; x--)
                {
                    if (reserved[y, x]) continue;
                    if (position < codewords.Length * 8 && grid[y, x] != Mask(mask, x, y))
                        codewords[position / 8] |= (byte)(1 << (7 - position % 8));
                    position++;
                }
            }
            up = !up;
        }
        if (position < codewords.Length * 8) return null;
        var blocks = new byte[count][];
        for (int i = 0; i < count; i++) blocks[i] = new byte[(i < layout[2] ? layout[3] : layout[5]) + layout[1]];
        position = 0;
        for (int col = 0; col < Math.Max(layout[3], layout[5]); col++)
            for (int i = 0; i < count; i++)
                if (col < blocks[i].Length - layout[1]) blocks[i][col] = codewords[position++];
        for (int col = 0; col < layout[1]; col++)
            for (int i = 0; i < count; i++) blocks[i][blocks[i].Length - layout[1] + col] = codewords[position++];
        var data = new byte[layout[0]];
        position = 0;
        int corrected = 0;
        foreach (byte[] block in blocks)
        {
            int repairs = QrReedSolomon.Correct(block, layout[1]);
            if (repairs < 0) return null;
            corrected += repairs;
            int length = block.Length - layout[1];
            block.AsSpan(0, length).CopyTo(data.AsSpan(position));
            position += length;
        }
        return QrPayloadDecoder.Decode(data, version, (QrCodeErrorCorrection)level, corrected);
    }

    private static int Bch(int value, int degree, int polynomial)
    {
        int remainder = value << degree;
        for (int bit = 30; bit >= degree; bit--)
            if ((remainder & (1 << bit)) != 0) remainder ^= polynomial << (bit - degree);
        return (value << degree) | remainder;
    }

    private static bool Mask(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0, 1 => y % 2 == 0, 2 => x % 3 == 0, 3 => (x + y) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0, 5 => x * y % 2 + x * y % 3 == 0,
        6 => (x * y % 2 + x * y % 3) % 2 == 0, _ => ((x + y) % 2 + x * y % 3) % 2 == 0
    };

    private static bool[,] Reserved(int version)
    {
        int size = version * 4 + 17;
        var result = new bool[size, size];
        void Rect(int x, int y, int width, int height)
        {
            for (int row = y; row < y + height; row++)
                for (int col = x; col < x + width; col++) result[row, col] = true;
        }
        Rect(0, 0, 9, 9); Rect(size - 8, 0, 8, 9); Rect(0, size - 8, 9, 8);
        Rect(6, 0, 1, size); Rect(0, 6, size, 1);
        if (version > 1)
        {
            int count = version / 7 + 2;
            int step = version == 32 ? 26 : ((version * 4 + count * 2 + 1) / (count * 2 - 2)) * 2;
            var centers = new int[count];
            centers[0] = 6;
            for (int i = count - 1, coordinate = size - 7; i > 0; i--, coordinate -= step) centers[i] = coordinate;
            for (int row = 0; row < count; row++)
                for (int col = 0; col < count; col++)
                {
                    if ((row == 0 && col == 0) || (row == 0 && col == count - 1) || (row == count - 1 && col == 0)) continue;
                    Rect(centers[col] - 2, centers[row] - 2, 5, 5);
                }
        }
        if (version >= 7) { Rect(size - 11, 0, 3, 6); Rect(0, size - 11, 6, 3); }
        return result;
    }
}
