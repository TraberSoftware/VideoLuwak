using FluentAssertions;
using VideoLuwak.Core.Hashing;

namespace VideoLuwak.Core.Tests;

public sealed class PerceptualHash64Tests
{
    [Fact]
    public void FromGrayscale_IdenticalInputs_ProduceIdenticalHash()
    {
        var input = LinearPattern();
        var copy = (byte[])input.Clone();

        var first = PerceptualHash64.FromGrayscale(input);
        var second = PerceptualHash64.FromGrayscale(copy);

        second.Should().Be(first);
        PerceptualHash64.HammingDistance(first, second).Should().Be(0);
    }

    [Fact]
    public void FromGrayscale_AllZeroInput_ProducesZeroHash()
    {
        var input = new byte[PerceptualHash64.SampleCount];

        var hash = PerceptualHash64.FromGrayscale(input);

        // A perfectly constant (uniform) image has no AC energy, so no bit exceeds the
        // median of the low-frequency block. The implementation snaps numerical residue
        // to zero, hence the documented all-zero hash.
        hash.Should().Be(0UL);
    }

    [Fact]
    public void FromGrayscale_All255Input_DoesNotThrow_AndProducesZeroHash()
    {
        var input = new byte[PerceptualHash64.SampleCount];
        Array.Fill(input, (byte)255);

        var hash = PerceptualHash64.FromGrayscale(input);

        hash.Should().Be(0UL);
    }

    [Fact]
    public void FromGrayscale_DifferentPatterns_HaveNonZeroHammingDistance()
    {
        var leftBright = LeftHalfBright();
        var checkerboard = Checkerboard();

        var leftHash = PerceptualHash64.FromGrayscale(leftBright);
        var checkerHash = PerceptualHash64.FromGrayscale(checkerboard);

        leftHash.Should().NotBe(checkerHash);
        PerceptualHash64.HammingDistance(leftHash, checkerHash).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(1025)]
    public void FromGrayscale_WrongLength_ThrowsArgumentException(int length)
    {
        var input = new byte[length];

        var act = () => PerceptualHash64.FromGrayscale(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HammingDistance_IsSymmetric_AndBoundedBy0And64()
    {
        ulong[] values =
        [
            0UL,
            1UL,
            0xAAAAAAAAAAAAAAAAUL,
            0x5555555555555555UL,
            0x00000000FFFFFFFFUL,
            ulong.MaxValue,
        ];

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                var distance = PerceptualHash64.HammingDistance(a, b);

                distance.Should().BeInRange(0, 64);
                distance.Should().Be(PerceptualHash64.HammingDistance(b, a));
            }
        }

        PerceptualHash64.HammingDistance(0UL, ulong.MaxValue).Should().Be(64);
        PerceptualHash64.HammingDistance(0UL, 0UL).Should().Be(0);
    }

    private static byte[] LinearPattern()
    {
        var input = new byte[PerceptualHash64.SampleCount];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = (byte)(i % 256);
        }

        return input;
    }

    private static byte[] LeftHalfBright()
    {
        var input = new byte[PerceptualHash64.SampleCount];
        for (var r = 0; r < 32; r++)
        {
            for (var c = 0; c < 16; c++)
            {
                input[r * 32 + c] = 255;
            }
        }

        return input;
    }

    private static byte[] Checkerboard()
    {
        var input = new byte[PerceptualHash64.SampleCount];
        for (var r = 0; r < 32; r++)
        {
            for (var c = 0; c < 32; c++)
            {
                input[r * 32 + c] = (r + c) % 2 == 0 ? (byte)255 : (byte)0;
            }
        }

        return input;
    }
}
