using Shouldly;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers <see cref="IdoraId.LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/>,
/// <see cref="IdoraId.UpperBound(string, DateTimeOffset, IdGenerationStrategy)"/> and their
/// millisecond overloads: that they bracket what the generator produces, and reject what they cannot
/// represent.
/// </summary>
public sealed class IdoraIdBoundsTests
{
    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Bounds_AroundNow_BracketEveryIdentifierGeneratedMeanwhile(IdGenerationStrategy format)
    {
        // Arrange
        IdoraGenerator generator = new(options => options.Strategy = format);
        DateTimeOffset from = DateTimeOffset.UtcNow;
        string[] generated = Enumerable.Range(0, 1_000).Select(_ => generator.Generate("usr")).ToArray();
        DateTimeOffset to = DateTimeOffset.UtcNow;

        // Act
        string lower = IdoraId.LowerBound("usr", from, format);
        string upper = IdoraId.UpperBound("usr", to, format);

        // Assert: as plain ordinal strings, which is how a database column compares them.
        foreach (string id in generated)
        {
            string.CompareOrdinal(id, lower).ShouldBeGreaterThanOrEqualTo(0, id);
            string.CompareOrdinal(id, upper).ShouldBeLessThanOrEqualTo(0, id);
        }
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Bounds_ExcludeTheNeighbouringMilliseconds(IdGenerationStrategy format)
    {
        // Arrange
        const long Millisecond = 1_754_568_000_000;

        // Act
        string lower = IdoraId.LowerBound("usr", Millisecond, format);
        string upper = IdoraId.UpperBound("usr", Millisecond, format);
        string previousUpper = IdoraId.UpperBound("usr", Millisecond - 1, format);
        string nextLower = IdoraId.LowerBound("usr", Millisecond + 1, format);

        // Assert
        string.CompareOrdinal(previousUpper, lower).ShouldBeLessThan(0);
        string.CompareOrdinal(lower, upper).ShouldBeLessThan(0);
        string.CompareOrdinal(upper, nextLower).ShouldBeLessThan(0);
    }

    [Fact]
    public void Bounds_FromAnInstant_TruncateToTheMillisecond()
    {
        // Arrange
        DateTimeOffset instant = DateTimeOffset.FromUnixTimeMilliseconds(1_754_568_000_000).AddTicks(9_999);

        // Act
        IdoraId lower = IdoraId.LowerBound("usr", instant);
        IdoraId upper = IdoraId.UpperBound("usr", instant);

        // Assert
        lower.TimestampMilliseconds.ShouldBe(1_754_568_000_000);
        upper.TimestampMilliseconds.ShouldBe(1_754_568_000_000);
        lower.ShouldBe(IdoraId.LowerBound("usr", 1_754_568_000_000));
    }

    [Fact]
    public void Bounds_FromAnInstantWithAnOffset_UseTheUtcInstant()
    {
        // Arrange
        DateTimeOffset utc = DateTimeOffset.FromUnixTimeMilliseconds(1_754_568_000_000);
        DateTimeOffset sydney = utc.ToOffset(TimeSpan.FromHours(10));

        // Act & Assert
        IdoraId.LowerBound("usr", sydney).ShouldBe(IdoraId.LowerBound("usr", utc));
    }

    [Fact]
    public void Bounds_DefaultToUlid()
    {
        // Act
        IdoraId lower = IdoraId.LowerBound("usr", 0);

        // Assert
        lower.Format.ShouldBe(IdGenerationStrategy.Ulid);
        lower.ToString().ShouldBe("usr_00000000000000000000000000");
    }

    [Fact]
    public void Bounds_AreValidIdentifiersCarryingThePrefixAsGiven()
    {
        // Act
        IdoraId lower = IdoraId.LowerBound("USR", 1, IdGenerationStrategy.UuidV7);
        IdoraId upper = IdoraId.UpperBound("USR", 1, IdGenerationStrategy.UuidV7);

        // Assert
        lower.Prefix.ShouldBe("USR");
        IdoraId.Parse(lower.ToString()).ShouldBe(lower);
        IdoraId.Parse(upper.ToString()).ShouldBe(upper);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(281_474_976_710_655)]
    public void Bounds_AcceptTheWholeFortyEightBitRange(long timestampMs)
    {
        // Act & Assert
        IdoraId.LowerBound("usr", timestampMs).TimestampMilliseconds.ShouldBe(timestampMs);
        IdoraId.UpperBound("usr", timestampMs, IdGenerationStrategy.UuidV7).TimestampMilliseconds.ShouldBe(timestampMs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(281_474_976_710_656)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Bounds_OutsideTheFortyEightBitRange_ThrowArgumentOutOfRange(long timestampMs)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.LowerBound("usr", timestampMs))
            .ParamName.ShouldBe("timestampMilliseconds");
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.UpperBound("usr", timestampMs))
            .ParamName.ShouldBe("timestampMilliseconds");
    }

    [Fact]
    public void Bounds_BeforeTheUnixEpoch_ThrowArgumentOutOfRange()
    {
        // Arrange
        DateTimeOffset beforeEpoch = DateTimeOffset.UnixEpoch.AddMilliseconds(-1);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.LowerBound("usr", beforeEpoch))
            .ParamName.ShouldBe("timestamp");
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.UpperBound("usr", beforeEpoch))
            .ParamName.ShouldBe("timestamp");
    }

    [Theory]
    [InlineData("")]
    [InlineData("us_r")]
    [InlineData("abcdefghijklmnopq")]
    public void Bounds_WithAnInvalidPrefix_ThrowInvalidEntityPrefix(string prefix)
    {
        // Act & Assert
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.LowerBound(prefix, 0)).Prefix.ShouldBe(prefix);
        Should.Throw<InvalidEntityPrefixException>(() => IdoraId.UpperBound(prefix, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Bounds_WithANullPrefix_ThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => IdoraId.LowerBound(null!, 0)).ParamName.ShouldBe("prefix");
        Should.Throw<ArgumentNullException>(() => IdoraId.UpperBound(null!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Bounds_WithAnUndefinedFormat_ThrowArgumentOutOfRange()
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => IdoraId.LowerBound("usr", 0, (IdGenerationStrategy)7))
            .ParamName.ShouldBe("format");
    }
}
