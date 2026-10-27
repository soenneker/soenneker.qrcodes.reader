using System;

namespace Soenneker.QrCodes.Reader.Internal;

internal sealed class QrProjectiveTransform
{
    private readonly double[] _coefficients;
    private QrProjectiveTransform(double[] coefficients) => _coefficients = coefficients;

    internal static QrProjectiveTransform? Create(QrPoint[] source, QrPoint[] destination)
    {
        var system = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = source[i].X, y = source[i].Y, u = destination[i].X, v = destination[i].Y;
            int row = i * 2;
            system[row, 0] = x; system[row, 1] = y; system[row, 2] = 1;
            system[row, 6] = -u * x; system[row, 7] = -u * y; system[row, 8] = u;
            system[row + 1, 3] = x; system[row + 1, 4] = y; system[row + 1, 5] = 1;
            system[row + 1, 6] = -v * x; system[row + 1, 7] = -v * y; system[row + 1, 8] = v;
        }
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 8; row++) if (Math.Abs(system[row, col]) > Math.Abs(system[pivot, col])) pivot = row;
            if (Math.Abs(system[pivot, col]) < 1e-10) return null;
            for (int j = col; j <= 8; j++) (system[col, j], system[pivot, j]) = (system[pivot, j], system[col, j]);
            double divisor = system[col, col];
            for (int j = col; j <= 8; j++) system[col, j] /= divisor;
            for (int row = 0; row < 8; row++)
            {
                if (row == col) continue;
                double factor = system[row, col];
                for (int j = col; j <= 8; j++) system[row, j] -= factor * system[col, j];
            }
        }
        var coefficients = new double[8];
        for (int i = 0; i < 8; i++) coefficients[i] = system[i, 8];
        return new QrProjectiveTransform(coefficients);
    }

    internal QrPoint Map(double x, double y)
    {
        double denominator = _coefficients[6] * x + _coefficients[7] * y + 1;
        return new QrPoint((_coefficients[0] * x + _coefficients[1] * y + _coefficients[2]) / denominator,
            (_coefficients[3] * x + _coefficients[4] * y + _coefficients[5]) / denominator);
    }
}
