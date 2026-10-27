using System;
using System.Linq;
using Soenneker.QrCodes.Reader.Internal;
using ZXing.Common.ReedSolomon;

namespace Soenneker.QrCodes.Reader.Tests;

public sealed class QrReedSolomonTests
{
    [Test]
    public void Repairs_independently_encoded_blocks_up_to_correction_capacity()
    {
        var random = new Random(7821);
        var encoder = new ReedSolomonEncoder(GenericGF.QR_CODE_FIELD_256);
        for (int ecc = 7; ecc <= 30; ecc++)
            for (int iteration = 0; iteration < 10; iteration++)
            {
                int length = random.Next(1, 160);
                int[] encoded = new int[length + ecc];
                for (int i = 0; i < length; i++) encoded[i] = random.Next(256);
                encoder.encode(encoded, ecc);
                byte[] damaged = encoded.Select(x => (byte)x).ToArray();
                int errors = iteration == 0 ? 0 : ecc / 2;
                int[] positions = Enumerable.Range(0, damaged.Length).OrderBy(_ => random.Next()).Take(errors).ToArray();
                foreach (int position in positions) damaged[position] ^= (byte)random.Next(1, 256);
                int repaired = QrReedSolomon.Correct(damaged, ecc);
                if (repaired != errors || !damaged.SequenceEqual(encoded.Select(x => (byte)x)))
                    throw new Exception($"Correction failed: ECC={ecc}, errors={errors}, data length={length}.");
            }
    }

    [Test]
    public void Rejects_unrecoverable_block()
    {
        int[] encoded = Enumerable.Repeat(42, 26).ToArray();
        new ReedSolomonEncoder(GenericGF.QR_CODE_FIELD_256).encode(encoded, 17);
        byte[] damaged = encoded.Select(x => (byte)x).ToArray();
        for (int i = 0; i < 12; i++) damaged[i] ^= (byte)(i + 1);
        if (QrReedSolomon.Correct(damaged, 17) != -1) throw new Exception("Unrecoverable block accepted.");
    }
}
