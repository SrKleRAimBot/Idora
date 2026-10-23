using Shouldly;
using Idora.Internal;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests.Internal;

/// <summary>
/// Covers the Crockford Base32 encoder against hand-checkable extremes and a round trip through an
/// independently written decoder.
/// </summary>
public sealed class CrockfordBase32Tests
{
    [Fact]
    public void EncodeUlid_WithEverythingZero_ProducesTwentySixZeroCharacters()
    {
        // Arrange
        char[] destination = new char[26];

        // Act
        CrockfordBase32.EncodeUlid(0, 0, 0, destination);

        // Assert
        new string(destination).ShouldBe(new string('0', 26));
    }

    [Fact]
    public void EncodeUlid_WithEveryBitSet_ProducesTheLargestRepresentableValue()
    {
        // Arrange
        char[] destination = new char[26];
        const long maxTimestamp = (1L << 48) - 1;

        // Act
        CrockfordBase32.EncodeUlid(maxTimestamp, 0xFFFF, ulong.MaxValue, destination);

        // Assert: the leading character holds only three bits, so it tops out at 7 while every
        // other character reaches the last letter of the alphabet.
        new string(destination).ShouldBe("7" + new string('z', 25));
    }

    [Theory]
    [InlineData(1L, "00000000010000000000000000")]
    [InlineData(31L, "000000000z0000000000000000")]
    [InlineData(32L, "00000000100000000000000000")]
    [InlineData(33L, "00000000110000000000000000")]
    public void EncodeUlid_WithASmallTimestamp_PlacesItInTheFirstTenCharacters(long timestamp, string expected)
    {
        // Arrange
        char[] destination = new char[26];

        // Act
        CrockfordBase32.EncodeUlid(timestamp, 0, 0, destination);

        // Assert
        new string(destination).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1UL, "00000000000000000000000001")]
    [InlineData(31UL, "0000000000000000000000000z")]
    [InlineData(32UL, "00000000000000000000000010")]
    public void EncodeUlid_WithASmallRandomValue_PlacesItAtTheEnd(ulong randomLow, string expected)
    {
        // Arrange
        char[] destination = new char[26];

        // Act
        CrockfordBase32.EncodeUlid(0, 0, randomLow, destination);

        // Assert
        new string(destination).ShouldBe(expected);
    }

    [Fact]
    public void EncodeUlid_PlacesTheHighRandomWordImmediatelyAfterTheTimestamp()
    {
        // Arrange
        char[] destination = new char[26];

        // Act: the top bit of the 80-bit random field is bit 15 of randomHigh.
        CrockfordBase32.EncodeUlid(0, 0x8000, 0, destination);

        // Assert
        new string(destination).ShouldBe("0000000000g000000000000000");
    }

    [Fact]
    public void EncodeUlid_RoundTripsThroughAnIndependentDecoder()
    {
        // Arrange
        Random random = new(Seed: 20260807);
        char[] destination = new char[26];

        for (int i = 0; i < 10_000; i++)
        {
            long timestamp = random.NextInt64(0, 1L << 48);
            ulong high = (ulong)random.Next(0, 0x10000);
            ulong low = NextUInt64(random);

            // Act
            CrockfordBase32.EncodeUlid(timestamp, high, low, destination);

            // Assert
            string encoded = new(destination);
            UlidDecoder.DecodeTimestamp(encoded).ShouldBe(timestamp);
            UlidDecoder.DecodeRandomness(encoded).ShouldBe(((UInt128)high << 64) | low);
        }
    }

    [Fact]
    public void EncodeUlid_NeverEmitsTheAmbiguousLettersCrockfordExcludes()
    {
        // Arrange
        Random random = new(Seed: 20260807);
        char[] destination = new char[26];
        HashSet<char> produced = [];

        // Act
        for (int i = 0; i < 10_000; i++)
        {
            CrockfordBase32.EncodeUlid(
                random.NextInt64(0, 1L << 48),
                (ulong)random.Next(0, 0x10000),
                NextUInt64(random),
                destination);

            produced.UnionWith(destination);
        }

        // Assert
        produced.ShouldNotContain('i');
        produced.ShouldNotContain('l');
        produced.ShouldNotContain('o');
        produced.ShouldNotContain('u');
        produced.ShouldBeSubsetOf(CrockfordBase32.Alphabet);
    }

    [Fact]
    public void EncodeUlid_WritesOnlyTheTwentySixCharactersItPromises()
    {
        // Arrange: a longer buffer, pre-filled, so an overrun would be visible.
        char[] destination = new char[30];
        Array.Fill(destination, '#');

        // Act
        CrockfordBase32.EncodeUlid(long.MaxValue & ((1L << 48) - 1), 0xFFFF, ulong.MaxValue, destination);

        // Assert
        new string(destination[26..]).ShouldBe("####");
    }

    /// <summary>
    /// Draws a full 64 bits. <see cref="Random.NextInt64()"/> only ever returns 63, which would
    /// leave the top bit of the random field untested.
    /// </summary>
    private static ulong NextUInt64(Random random)
    {
        byte[] buffer = new byte[8];
        random.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer);
    }
}
