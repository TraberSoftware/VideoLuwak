using System.Numerics;

namespace VideoLuwak.Core.Hashing;

/// <summary>
/// Dependency-free 64-bit perceptual hash (pHash) over a 32x32 grayscale image.
/// The immutable, deterministic algorithm:
/// <list type="number">
///   <item>Load the 1024 row-major bytes into a 32x32 working buffer.</item>
///   <item>Apply a separable DCT-II (rows first, then columns).</item>
///   <item>Take the low-frequency 8x8 block, excluding the DC coefficient (63 values).</item>
///   <item>Compute the median of those 63 values.</item>
///   <item>
///     Emit one bit per position in the 8x8 block: bit <c>(r*8+c)</c> is set when the
///     coefficient is strictly greater than the median. Bit 0 (the DC term) is always 0.
///   </item>
/// </list>
/// The ordering of bits is fixed and deterministic for a given input.
/// </summary>
public static class PerceptualHash64
{
    /// <summary>Side length of the grayscale input (32x32).</summary>
    public const int BlockSize = 32;

    /// <summary>Exact number of grayscale bytes required by <see cref="FromGrayscale"/>.</summary>
    public const int SampleCount = BlockSize * BlockSize; // 1024

    private const int HashSide = 8;
    private const int HashBitCount = HashSide * HashSide; // 64
    private const int LowFrequencyCount = HashBitCount - 1; // 63, DC excluded

    // Precomputed DCT-II kernels. Built once on first use.
    private static readonly double[] CosineTable = BuildCosineTable();
    private static readonly double[] NormTable = BuildNormTable();

    /// <summary>
    /// Computes the 64-bit perceptual hash of a 32x32 grayscale image supplied as
    /// 1024 row-major bytes.
    /// </summary>
    /// <param name="gray32x32">Exactly <see cref="SampleCount"/> (1024) bytes.</param>
    /// <returns>The 64-bit hash. Identical inputs always produce identical hashes.</returns>
    /// <exception cref="ArgumentException">The input length is not exactly 1024 bytes.</exception>
    public static ulong FromGrayscale(ReadOnlySpan<byte> gray32x32)
    {
        if (gray32x32.Length != SampleCount)
        {
            throw new ArgumentException(
                $"Expected exactly {SampleCount} grayscale bytes (32x32), but received {gray32x32.Length}.",
                nameof(gray32x32));
        }

        // Working buffer in row-major order. One heap allocation; small scratch buffers
        // live on the stack.
        var coefficients = new double[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            coefficients[i] = gray32x32[i];
        }

        Span<double> rowBuffer = stackalloc double[BlockSize];
        Span<double> transformed = stackalloc double[BlockSize];

        // Pass 1: DCT-II across every row (horizontal pass).
        for (int r = 0; r < BlockSize; r++)
        {
            int rowOffset = r * BlockSize;
            for (int c = 0; c < BlockSize; c++)
            {
                rowBuffer[c] = coefficients[rowOffset + c];
            }

            Dct1D(rowBuffer, transformed);

            for (int c = 0; c < BlockSize; c++)
            {
                coefficients[rowOffset + c] = transformed[c];
            }
        }

        // Pass 2: DCT-II down every column (vertical pass). Combined with pass 1 this is
        // the separable 2D DCT-II.
        for (int c = 0; c < BlockSize; c++)
        {
            for (int r = 0; r < BlockSize; r++)
            {
                rowBuffer[r] = coefficients[r * BlockSize + c];
            }

            Dct1D(rowBuffer, transformed);

            for (int r = 0; r < BlockSize; r++)
            {
                coefficients[r * BlockSize + c] = transformed[r];
            }
        }

        // DCT of a perfectly constant image yields zero AC coefficients in exact
        // arithmetic, but the separable double-precision passes leave tiny residuals.
        // Snap values far below the DC magnitude to zero so a uniform image always
        // hashes to all-zero bits (the median then equals zero and no bit is set).
        double epsilon = 1e-6 * (Math.Abs(coefficients[0]) + 1.0);
        for (int i = 0; i < SampleCount; i++)
        {
            if (Math.Abs(coefficients[i]) < epsilon)
            {
                coefficients[i] = 0.0;
            }
        }

        // Gather the 63 low-frequency AC coefficients from the top-left 8x8 block.
        Span<double> lows = stackalloc double[LowFrequencyCount];
        int n = 0;
        for (int r = 0; r < HashSide; r++)
        {
            for (int c = 0; c < HashSide; c++)
            {
                if (r == 0 && c == 0)
                {
                    continue; // DC is excluded from the median and always maps to bit 0.
                }

                lows[n++] = coefficients[r * BlockSize + c];
            }
        }

        double median = Median(lows);

        // Build the bits. Bit index (r*8+c) is set when the coefficient exceeds the median.
        // The DC bit (index 0) is intentionally left at 0.
        ulong hash = 0UL;
        for (int r = 0; r < HashSide; r++)
        {
            for (int c = 0; c < HashSide; c++)
            {
                if (r == 0 && c == 0)
                {
                    continue;
                }

                if (coefficients[r * BlockSize + c] > median)
                {
                    hash |= 1UL << (r * HashSide + c);
                }
            }
        }

        return hash;
    }

    /// <summary>Hamming distance between two hashes (0 = identical, 64 = maximally different).</summary>
    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>One-dimensional DCT-II with orthonormal scaling, writing into <paramref name="output"/>.</summary>
    private static void Dct1D(ReadOnlySpan<double> input, Span<double> output)
    {
        for (int k = 0; k < BlockSize; k++)
        {
            int kernelOffset = k * BlockSize;
            double sum = 0.0;
            for (int n = 0; n < BlockSize; n++)
            {
                sum += input[n] * CosineTable[kernelOffset + n];
            }

            output[k] = sum * NormTable[k];
        }
    }

    /// <summary>Returns the (lower) median of the supplied values. Does not modify the source.</summary>
    private static double Median(ReadOnlySpan<double> values)
    {
        Span<double> copy = stackalloc double[values.Length];
        values.CopyTo(copy);
        copy.Sort();
        return copy[copy.Length / 2];
    }

    private static double[] BuildCosineTable()
    {
        var table = new double[BlockSize * BlockSize];
        double factor = Math.PI / (2.0 * BlockSize);
        for (int k = 0; k < BlockSize; k++)
        {
            for (int n = 0; n < BlockSize; n++)
            {
                table[k * BlockSize + n] = Math.Cos(factor * k * (2 * n + 1));
            }
        }

        return table;
    }

    private static double[] BuildNormTable()
    {
        var table = new double[BlockSize];
        table[0] = Math.Sqrt(1.0 / BlockSize);
        double other = Math.Sqrt(2.0 / BlockSize);
        for (int k = 1; k < BlockSize; k++)
        {
            table[k] = other;
        }

        return table;
    }
}
