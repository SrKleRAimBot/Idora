using System.Text;
using Shouldly;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers what a parsed <see cref="IdoraId"/> exposes, how it formats, and how
/// <c>default(IdoraId)</c> behaves.
/// </summary>
public sealed class IdoraIdMemberTests
{
    private const string UlidId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string UuidV7Id = "inv_0198846702007011a233445566778899";

    /// <summary>2025-08-07T12:00:00Z, the millisecond both identifiers above were minted in.</summary>
    private static readonly DateTimeOffset MintedAt = new(2025, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Members_OfAUlid_DescribeIt()
    {
        // Act
        IdoraId id = IdoraId.Parse(UlidId);

        // Assert
        id.Prefix.ShouldBe("usr");
        id.Value.ShouldBe("01k226e0g0008j4ct4ank7f24s");
        id.Format.ShouldBe(IdGenerationStrategy.Ulid);
        id.TimestampMilliseconds.ShouldBe(1_754_568_000_000);
        id.Timestamp.ShouldBe(MintedAt);
        id.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Members_OfAUuidV7_DescribeIt()
    {
        // Act
        IdoraId id = IdoraId.Parse(UuidV7Id);

        // Assert
        id.Prefix.ShouldBe("inv");
        id.Value.ShouldBe("0198846702007011a233445566778899");
        id.Format.ShouldBe(IdGenerationStrategy.UuidV7);
        id.Timestamp.ShouldBe(MintedAt);
    }

    [Fact]
    public void Value_OfAUuidV7_RoundTripsThroughGuid()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UuidV7Id);

        // Act
        Guid guid = Guid.ParseExact(id.Value, "N");

        // Assert
        guid.Version.ShouldBe(7);
        guid.ToString("N").ShouldBe(id.Value);
    }

    [Fact]
    public void Timestamp_IsAlwaysUtc()
    {
        // Act
        DateTimeOffset timestamp = IdoraId.Parse(UlidId).Timestamp;

        // Assert: the host's time zone is deliberately not UTC in CI; the result must not care.
        timestamp.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Timestamp_BeyondTheYear9999_ReportsTheLatestRepresentableInstantRatherThanThrowing()
    {
        // Arrange: the highest timestamp a ULID can carry, in the year 10889.
        IdoraId id = IdoraId.Parse("x_7zzzzzzzzzzzzzzzzzzzzzzzzz");

        // Act
        DateTimeOffset timestamp = id.Timestamp;

        // Assert
        timestamp.ShouldBe(DateTimeOffset.MaxValue);
        id.TimestampMilliseconds.ShouldBe(281_474_976_710_655);
    }

    [Fact]
    public void Timestamp_AtTheLastMillisecondOfTheYear9999_IsExact()
    {
        // Arrange
        long lastMillisecond = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        IdoraId id = IdoraId.LowerBound("x", lastMillisecond);

        // Act
        DateTimeOffset timestamp = id.Timestamp;

        // Assert
        timestamp.ToUnixTimeMilliseconds().ShouldBe(lastMillisecond);
    }

    [Fact]
    public void Default_IsTheEmptyIdentifier()
    {
        // Act
        IdoraId empty = default;

        // Assert
        empty.IsEmpty.ShouldBeTrue();
        empty.Prefix.ShouldBe(string.Empty);
        empty.Value.ShouldBe(string.Empty);
        empty.ToString().ShouldBe(string.Empty);
        ((string)empty).ShouldBe(string.Empty);
        empty.Format.ShouldBe(IdGenerationStrategy.Ulid);
        empty.TimestampMilliseconds.ShouldBe(0);
        empty.Timestamp.ShouldBe(DateTimeOffset.UnixEpoch);
        empty.HasPrefix(string.Empty).ShouldBeFalse();
        empty.GetHashCode().ShouldBe(0);
    }

    [Fact]
    public void Default_EqualsOnlyItself()
    {
        // Arrange
        IdoraId empty = default;
        IdoraId epoch = IdoraId.Parse("ord_00000000000000000000000000");

        // Act & Assert
        empty.ShouldBe(default(IdoraId));
        (empty == default).ShouldBeTrue();
        (empty == epoch).ShouldBeFalse();
        empty.Equals(epoch).ShouldBeFalse();
    }

    [Fact]
    public void Default_SortsBeforeTheEarliestPossibleIdentifier()
    {
        // Arrange
        IdoraId earliest = IdoraId.Parse("a_00000000000000000000000000");

        // Act & Assert
        (default(IdoraId) < earliest).ShouldBeTrue();
        default(IdoraId).CompareTo(default).ShouldBe(0);
    }

    [Fact]
    public void Default_FormatsAsNothing()
    {
        // Arrange
        Span<char> chars = stackalloc char[8];
        Span<byte> bytes = stackalloc byte[8];

        // Act
        bool formattedChars = default(IdoraId).TryFormat(chars, out int charsWritten);
        bool formattedBytes = default(IdoraId).TryFormat(bytes, out int bytesWritten);

        // Assert
        formattedChars.ShouldBeTrue();
        charsWritten.ShouldBe(0);
        formattedBytes.ShouldBeTrue();
        bytesWritten.ShouldBe(0);
        $"[{default(IdoraId)}]".ShouldBe("[]");
    }

    [Fact]
    public void ImplicitConversion_ToString_GivesTheSameInstance()
    {
        // Arrange
        string text = UlidId;
        IdoraId id = IdoraId.Parse(text);

        // Act
        string converted = id;

        // Assert
        ReferenceEquals(converted, text).ShouldBeTrue();
    }

    [Fact]
    public void ToString_WithAFormat_AcceptsOnlyTheGeneralForm()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);

        // Act & Assert
        id.ToString(null, null).ShouldBe(UlidId);
        id.ToString(string.Empty, null).ShouldBe(UlidId);
        id.ToString("G", null).ShouldBe(UlidId);
        Should.Throw<FormatException>(() => id.ToString("N", null));
    }

    [Fact]
    public void StringInterpolation_WritesTheIdentifier()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);

        // Act
        string interpolated = $"created {id}";

        // Assert
        interpolated.ShouldBe("created " + UlidId);
    }

    [Fact]
    public void TryFormat_IntoCharacters_WritesTheTextWhenItFits()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UuidV7Id);
        char[] destination = new char[64];

        // Act
        bool formatted = id.TryFormat(destination, out int written);

        // Assert
        formatted.ShouldBeTrue();
        new string(destination, 0, written).ShouldBe(UuidV7Id);
    }

    [Fact]
    public void TryFormat_IntoTooFewCharacters_ReturnsFalse()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UuidV7Id);
        char[] destination = new char[UuidV7Id.Length - 1];

        // Act
        bool formatted = id.TryFormat(destination, out int written);

        // Assert
        formatted.ShouldBeFalse();
        written.ShouldBe(0);
    }

    [Fact]
    public void TryFormat_IntoUtf8_WritesOneBytePerCharacter()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);
        byte[] destination = new byte[64];

        // Act
        bool formatted = id.TryFormat(destination, out int written);

        // Assert
        formatted.ShouldBeTrue();
        Encoding.UTF8.GetString(destination, 0, written).ShouldBe(UlidId);
    }

    [Fact]
    public void TryFormat_IntoTooFewBytes_ReturnsFalse()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);
        byte[] destination = new byte[10];

        // Act
        bool formatted = id.TryFormat(destination, out int written);

        // Assert
        formatted.ShouldBeFalse();
        written.ShouldBe(0);
    }

    [Fact]
    public void TryFormat_WithAnUnsupportedFormat_Throws()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);

        // Act & Assert
        Should.Throw<FormatException>(() => id.TryFormat(new char[64], out _, "x"));
        Should.Throw<FormatException>(() => id.TryFormat(new byte[64], out _, "x"));
    }

    [Fact]
    public void Utf8Interpolation_WritesTheIdentifier()
    {
        // Arrange
        IdoraId id = IdoraId.Parse(UlidId);
        byte[] destination = new byte[64];

        // Act
        bool written = System.Text.Unicode.Utf8.TryWrite(destination, $"{id}", out int bytesWritten);

        // Assert
        written.ShouldBeTrue();
        Encoding.UTF8.GetString(destination, 0, bytesWritten).ShouldBe(UlidId);
    }

    [Theory]
    [InlineData("usr", IdGenerationStrategy.Ulid, 30)]
    [InlineData("usr", IdGenerationStrategy.UuidV7, 36)]
    [InlineData("a", IdGenerationStrategy.Ulid, 28)]
    [InlineData("abcdefghijklmnop", IdGenerationStrategy.UuidV7, 49)]
    public void GetLength_MatchesWhatTheGeneratorProduces(string prefix, IdGenerationStrategy format, int expected)
    {
        // Arrange
        IdoraGenerator generator = new(options => options.Strategy = format);

        // Act
        int length = IdoraId.GetLength(prefix, format);

        // Assert
        length.ShouldBe(expected);
        generator.Generate(prefix).Length.ShouldBe(expected);
    }

    [Fact]
    public void GetLength_WithAnInvalidPrefix_Throws()
    {
        // Act & Assert
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.GetLength("us_r", IdGenerationStrategy.Ulid));
        Should.Throw<ArgumentNullException>(() => IdoraId.GetLength(null!, IdGenerationStrategy.Ulid));
    }

    [Fact]
    public void GetMaxLength_AllowsForTheLongestPrefix()
    {
        // Act & Assert
        IdoraId.GetMaxLength(IdGenerationStrategy.Ulid).ShouldBe(43);
        IdoraId.GetMaxLength(IdGenerationStrategy.UuidV7).ShouldBe(49);
    }

    [Fact]
    public void GetLength_WithAnUndefinedFormat_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.GetLength("usr", (IdGenerationStrategy)42))
            .ParamName.ShouldBe("format");
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.GetMaxLength((IdGenerationStrategy)42));
    }
}
