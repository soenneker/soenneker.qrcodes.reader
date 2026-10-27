using System;
using System.Collections.Generic;
using System.Linq;

namespace Soenneker.QrCodes.Reader.Internal;

internal static class QrDetector
{
    internal static QrCodeReadResult? Read(ReadOnlySpan<byte> pixels, int width, int height, QrCodeReaderOptions options)
    {
        if (width < 21 || height < 21) return null;
        int threshold = Threshold(pixels);
        for (int adaptive = 0; adaptive < 2; adaptive++)
        {
            bool[] binary = Binarize(pixels, width, height, threshold, adaptive != 0);
            for (int polarity = 0; polarity < (options.TryInverted ? 2 : 1); polarity++)
            {
                if (polarity != 0)
                    for (int i = 0; i < binary.Length; i++) binary[i] = !binary[i];
                QrCodeReadResult? result = Detect(binary, width, height, options.TryMirrored);
                if (result != null) return result;
            }
        }
        return null;
    }

    private static int Threshold(ReadOnlySpan<byte> pixels)
    {
        Span<int> histogram = stackalloc int[256];
        histogram.Clear();
        long sum = 0;
        foreach (byte value in pixels) { histogram[value]++; sum += value; }
        long lowerSum = 0, lowerCount = 0;
        double best = -1;
        int result = 127;
        for (int i = 0; i < 255; i++)
        {
            lowerCount += histogram[i]; lowerSum += (long)i * histogram[i];
            long upperCount = pixels.Length - lowerCount;
            if (lowerCount == 0 || upperCount == 0) continue;
            double difference = (double)lowerSum / lowerCount - (double)(sum - lowerSum) / upperCount;
            double variance = lowerCount * (double)upperCount * difference * difference;
            if (variance > best) { best = variance; result = i; }
        }
        return result;
    }

    private static bool[] Binarize(ReadOnlySpan<byte> pixels, int width, int height, int global, bool adaptive)
    {
        var result = new bool[pixels.Length];
        if (!adaptive)
        {
            for (int i = 0; i < result.Length; i++) result[i] = pixels[i] <= global;
            return result;
        }
        const int block = 8;
        int columns = (width + block - 1) / block, rows = (height + block - 1) / block;
        var thresholds = new int[columns * rows];
        for (int by = 0; by < rows; by++)
            for (int bx = 0; bx < columns; bx++)
            {
                int min = 255, max = 0;
                for (int y = by * block; y < Math.Min(height, (by + 1) * block); y++)
                    for (int x = bx * block; x < Math.Min(width, (bx + 1) * block); x++)
                    {
                        int value = pixels[y * width + x];
                        min = Math.Min(min, value); max = Math.Max(max, value);
                    }
                thresholds[by * columns + bx] = max - min >= 24 ? (max + min) / 2 : -1;
            }
        for (int by = 0; by < rows; by++)
            for (int bx = 0; bx < columns; bx++)
            {
                int sum = 0, count = 0;
                for (int y = Math.Max(0, by - 2); y <= Math.Min(rows - 1, by + 2); y++)
                    for (int x = Math.Max(0, bx - 2); x <= Math.Min(columns - 1, bx + 2); x++)
                    {
                        int value = thresholds[y * columns + x];
                        if (value >= 0) { sum += value; count++; }
                    }
                int threshold = count == 0 ? global : sum / count;
                for (int y = by * block; y < Math.Min(height, (by + 1) * block); y++)
                    for (int x = bx * block; x < Math.Min(width, (bx + 1) * block); x++)
                        result[y * width + x] = pixels[y * width + x] <= threshold;
            }
        return result;
    }

    private static bool Ratio(ReadOnlySpan<int> runs)
    {
        int total = 0;
        foreach (int run in runs) { if (run == 0) return false; total += run; }
        if (total < 7) return false;
        double unit = total / 7.0;
        for (int i = 0; i < 5; i++)
        {
            double expected = i == 2 ? unit * 3 : unit;
            if (Math.Abs(runs[i] - expected) > expected * 0.65) return false;
        }
        return true;
    }

    private static double CrossCheck(bool[] image, int width, int height, int x, int y, bool vertical, double expected)
    {
        int limit = vertical ? height : width;
        int center = vertical ? y : x;
        bool At(int index) => vertical ? image[index * width + x] : image[y * width + index];
        if (center < 0 || center >= limit || x < 0 || x >= width || y < 0 || y >= height || !At(center)) return double.NaN;
        Span<int> counts = stackalloc int[5];
        counts.Clear();
        int i = center;
        int maximum = Math.Max(4, (int)(expected * 4));
        while (i >= 0 && At(i) && counts[2] <= maximum) { counts[2]++; i--; }
        int start = i + 1;
        while (i >= 0 && !At(i) && counts[1] <= maximum) { counts[1]++; i--; }
        while (i >= 0 && At(i) && counts[0] <= maximum) { counts[0]++; i--; }
        i = center + 1;
        while (i < limit && At(i) && counts[2] <= maximum) { counts[2]++; i++; }
        int end = i;
        while (i < limit && !At(i) && counts[3] <= maximum) { counts[3]++; i++; }
        while (i < limit && At(i) && counts[4] <= maximum) { counts[4]++; i++; }
        return Ratio(counts) ? (start + end) / 2.0 : double.NaN;
    }

    private static List<QrFinderPattern> Finders(bool[] image, int width, int height)
    {
        var found = new List<QrFinderPattern>();
        Span<int> runs = stackalloc int[5];
        for (int y = 0; y < height; y++)
        {
            runs.Clear();
            int runCount = 0, start = 0;
            bool color = image[y * width];
            for (int x = 1; x <= width; x++)
            {
                if (x < width && image[y * width + x] == color) continue;
                for (int i = 0; i < 4; i++) runs[i] = runs[i + 1];
                runs[4] = x - start;
                runCount++;
                if (color && runCount >= 5 && Ratio(runs))
                {
                    double module = (runs[0] + runs[1] + runs[2] + runs[3] + runs[4]) / 7.0;
                    double cx = x - runs[4] - runs[3] - runs[2] / 2.0;
                    double cy = CrossCheck(image, width, height, (int)cx, y, true, module);
                    if (double.IsFinite(cy))
                    {
                        double refinedX = CrossCheck(image, width, height, (int)cx, (int)cy, false, module);
                        if (double.IsFinite(refinedX) && DiagonalCheck(image, width, height, (int)refinedX, (int)cy, 1) &&
                            DiagonalCheck(image, width, height, (int)refinedX, (int)cy, -1))
                        {
                            int match = found.FindIndex(p => Math.Abs(p.X - refinedX) <= module * 2 && Math.Abs(p.Y - cy) <= module * 2 && Math.Abs(p.Module - module) < module);
                            if (match >= 0)
                            {
                                QrFinderPattern p = found[match];
                                int hits = p.Hits + 1;
                                found[match] = new QrFinderPattern((p.X * p.Hits + refinedX) / hits, (p.Y * p.Hits + cy) / hits, (p.Module * p.Hits + module) / hits, hits);
                            }
                            else if (found.Count < 512) found.Add(new QrFinderPattern(refinedX, cy, module, 1));
                        }
                    }
                }
                start = x; color = !color;
            }
        }
        return found.OrderByDescending(p => p.Hits).Take(32).ToList();
    }

    private static double Distance(QrFinderPattern a, QrFinderPattern b) => double.Hypot(a.X - b.X, a.Y - b.Y);

    private static bool DiagonalCheck(bool[] image, int width, int height, int x, int y, int slope)
    {
        bool Inside(int offset) => x + offset >= 0 && x + offset < width && y + offset * slope >= 0 && y + offset * slope < height;
        bool Dark(int offset) => image[(y + offset * slope) * width + x + offset];
        Span<int> runs = stackalloc int[5];
        runs.Clear();
        int i = 0;
        while (Inside(i) && Dark(i)) { runs[2]++; i--; }
        while (Inside(i) && !Dark(i)) { runs[1]++; i--; }
        while (Inside(i) && Dark(i)) { runs[0]++; i--; }
        i = 1;
        while (Inside(i) && Dark(i)) { runs[2]++; i++; }
        while (Inside(i) && !Dark(i)) { runs[3]++; i++; }
        while (Inside(i) && Dark(i)) { runs[4]++; i++; }
        return Ratio(runs);
    }

    private static QrCodeReadResult? Detect(bool[] image, int width, int height, bool mirrored)
    {
        List<QrFinderPattern> points = Finders(image, width, height);
        int attempts = 0;
        for (int a = 0; a < points.Count; a++)
            for (int b = a + 1; b < points.Count; b++)
                for (int c = b + 1; c < points.Count; c++)
                {
                    QrFinderPattern tl = points[a], tr = points[b], bl = points[c];
                    double ab = Distance(tl, tr), ac = Distance(tl, bl), bc = Distance(tr, bl);
                    if (ab > ac && ab > bc) (tl, bl) = (bl, tl);
                    else if (ac > ab && ac > bc) (tl, tr) = (tr, tl);
                    double ux = tr.X - tl.X, uy = tr.Y - tl.Y, vx = bl.X - tl.X, vy = bl.Y - tl.Y;
                    double u = double.Hypot(ux, uy), v = double.Hypot(vx, vy);
                    if (u < tl.Module * 10 || v < tl.Module * 10 || u / v is < 0.4 or > 2.5 || Math.Abs(ux * vx + uy * vy) / (u * v) > 0.5) continue;
                    if (ux * vy - uy * vx < 0) (tr, bl) = (bl, tr);
                    double moduleU = MeasureModule(image, width, height, tl, tr);
                    double moduleV = MeasureModule(image, width, height, tl, bl);
                    double dimension = (Distance(tl, tr) / moduleU + Distance(tl, bl) / moduleV) / 2 + 7;
                    int estimated = Math.Clamp((int)Math.Round((dimension - 17) / 4), 1, 40);
                    if (++attempts > 256) return null;
                    for (int delta = 0; delta <= 4; delta++)
                        for (int sign = 0; sign < (delta == 0 ? 1 : 2); sign++)
                        {
                            int version = estimated + (sign == 0 ? delta : -delta);
                            if (version is < 1 or > 40) continue;
                            int size = version * 4 + 17;
                            QrCodeReadResult? result = Sample(image, width, height, tl, tr, bl, size, null, mirrored);
                            if (result != null) return result;
                            if (version == 1) continue;
                            foreach (QrPoint alignment in Alignments(image, width, height, tl, tr, bl, size))
                            {
                                result = Sample(image, width, height, tl, tr, bl, size, alignment, mirrored);
                                if (result != null) return result;
                            }
                        }
                }
        return null;
    }

    private static double MeasureModule(bool[] image, int width, int height, QrFinderPattern from, QrFinderPattern to)
    {
        double distance = Distance(from, to);
        double dx = (to.X - from.X) / distance, dy = (to.Y - from.Y) / distance;
        double Measure(QrFinderPattern center, double direction)
        {
            int transitions = 0;
            bool previous = true;
            for (double t = 0; t < Math.Min(distance / 2, center.Module * 12); t += 0.25)
            {
                int x = (int)(center.X + dx * t * direction), y = (int)(center.Y + dy * t * direction);
                if (x < 0 || y < 0 || x >= width || y >= height) break;
                bool dark = image[y * width + x];
                if (dark == previous) continue;
                previous = dark;
                if (++transitions == 3) return t / 3.5;
            }
            return center.Module;
        }
        return Math.Max(0.75, (Measure(from, 1) + Measure(to, -1)) / 2);
    }

    private static QrCodeReadResult? Sample(bool[] image, int width, int height, QrFinderPattern tl, QrFinderPattern tr, QrFinderPattern bl,
        int size, QrPoint? alignment, bool mirrored)
    {
        double ux = (tr.X - tl.X) / (size - 7), uy = (tr.Y - tl.Y) / (size - 7);
        double vx = (bl.X - tl.X) / (size - 7), vy = (bl.Y - tl.Y) / (size - 7);
        QrProjectiveTransform? transform = null;
        if (alignment.HasValue)
        {
            transform = QrProjectiveTransform.Create(
                [new(3.5, 3.5), new(size - 3.5, 3.5), new(3.5, size - 3.5), new(size - 6.5, size - 6.5)],
                [new(tl.X, tl.Y), new(tr.X, tr.Y), new(bl.X, bl.Y), alignment.Value]);
            if (transform == null) return null;
        }
        var modules = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                QrPoint p = transform?.Map(x + 0.5, y + 0.5) ?? new QrPoint(tl.X + (x - 3) * ux + (y - 3) * vx, tl.Y + (x - 3) * uy + (y - 3) * vy);
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= height) return null;
                modules[y, x] = image[(int)p.Y * width + (int)p.X];
            }
        return QrMatrixDecoder.Decode(modules, mirrored);
    }

    private static IEnumerable<QrPoint> Alignments(bool[] image, int width, int height, QrFinderPattern tl, QrFinderPattern tr, QrFinderPattern bl, int size)
    {
        double ux = (tr.X - tl.X) / (size - 7), uy = (tr.Y - tl.Y) / (size - 7);
        double vx = (bl.X - tl.X) / (size - 7), vy = (bl.Y - tl.Y) / (size - 7);
        double cx = tl.X + (size - 10) * (ux + vx), cy = tl.Y + (size - 10) * (uy + vy);
        double module = (double.Hypot(ux, uy) + double.Hypot(vx, vy)) / 2;
        double step = Math.Max(1, module / 2);
        double radius = Math.Min(module * 12, Math.Min(width, height) / 3.0);
        var candidates = new List<(QrPoint Point, double Score)>();
        for (double y = Math.Max(0, cy - radius); y < Math.Min(height, cy + radius); y += step)
            for (double x = Math.Max(0, cx - radius); x < Math.Min(width, cx + radius); x += step)
            {
                if (!image[(int)y * width + (int)x]) continue;
                int mismatch = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int sx = (int)(x + dx * ux + dy * vx), sy = (int)(y + dx * uy + dy * vy);
                        bool expected = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height || image[sy * width + sx] != expected) mismatch++;
                    }
                if (mismatch > 2) continue;
                candidates.Add((new QrPoint(x, y), mismatch * radius * 2 + double.Hypot(x - cx, y - cy)));
            }
        return candidates.OrderBy(c => c.Score).Take(4).Select(c => c.Point);
    }
}
