using System.Text.RegularExpressions;
using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the shape of a generated identifier: its prefix, its separator, and the alphabet, casing
/// and length of its value.
/// </summary>
public sealed class IdFormatTests
{
    /// <summary>Prefix, one underscore, then 26 lowercase Crockford Base32 characters.</summary>
    private static readonly Regex UlidId = new("^usr_[0-9a-hjkmnp-tv-z]{26}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Prefix, one underscore, then 32 lowercase hexadecimal characters.</summary>
    private static readonly Regex UuidId = new("^usr_[0-9a-f]{32}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public void Generate_WithUlidStrategy_ProducesPrefixUnderscoreAndTwentySixBase32Characters()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldMatch(UlidId.ToString());
        id.Length.ShouldBe("usr_".Length + 26);
    }

    [Fact]
    public void Generate_WithUuidV7Strategy_ProducesPrefixUnderscoreAndThirtyTwoHexCharacters()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.UuidV7);

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldMatch(UuidId.ToString());
        id.Length.ShouldBe("usr_".Length + 32);
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_ForEitherStrategy_ProducesLowercaseOnly(IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldBe(id.ToLowerInvariant());
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_ForEitherStrategy_ContainsExactlyOneSeparator(IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.Count(character => character == '_').ShouldBe(1);
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_ForDifferentEntityTypes_UsesEachRegisteredPrefix(IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);

        // Act
        string userId = generator.Generate<User>();
        string orderId = generator.Generate<Order>();

        // Assert
        userId.ShouldStartWith("usr_");
        orderId.ShouldStartWith("ord_");
    }

    [Fact]
    public void Generate_WithUlidStrategy_EncodesTheCurrentTimeInTheTimestampCharacters()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        string id = generator.Generate<User>();

        // Assert
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long encoded = UlidDecoder.DecodeTimestamp(UlidDecoder.Split(id).Value);
        encoded.ShouldBeInRange(before, after);
    }

    [Fact]
    public void Generate_WithUuidV7Strategy_ProducesAValueThatParsesAsAGuid()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.UuidV7);

        // Act
        string id = generator.Generate<User>();

        // Assert
        string value = UlidDecoder.Split(id).Value;
        Guid.TryParse(value, out Guid parsed).ShouldBeTrue();
        parsed.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Generate_WithUuidV7Strategy_SetsTheVersionAndVariantBitsRequiredByRfc9562()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.UuidV7);

        // Act
        string id = generator.Generate<User>();

        // Assert
        string value = UlidDecoder.Split(id).Value;

        // Character 12 of the hex form is the version nibble; it must read 7.
        value[12].ShouldBe('7');

        // Character 16 is the variant nibble; the top two bits must be 10, so it is 8, 9, a or b.
        value[16].ShouldBeOneOf('8', '9', 'a', 'b');
    }

    [Fact]
    public void Generate_WithUlidStrategy_VariesTheRandomFieldWithinASingleMillisecond()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);

        // Act: a hundred identifiers take microseconds, so almost every adjacent pair shares a
        // millisecond. Asserting over the pairs that do avoids depending on where the boundary lands.
        string[] values = [.. Enumerable
            .Range(0, 100)
            .Select(_ => UlidDecoder.Split(generator.Generate<User>()).Value)];

        // Assert
        (string First, string Second)[] withinOneMillisecond = [.. values
            .Zip(values.Skip(1))
            .Where(pair => UlidDecoder.DecodeTimestamp(pair.First) == UlidDecoder.DecodeTimestamp(pair.Second))];

        withinOneMillisecond.ShouldNotBeEmpty();
        withinOneMillisecond.ShouldAllBe(pair =>
            UlidDecoder.DecodeRandomness(pair.First) != UlidDecoder.DecodeRandomness(pair.Second));
    }

    [Fact]
    public void Generate_WithoutConfiguringAStrategy_DefaultsToUlid()
    {
        // Arrange
        IIdoraGenerator generator = new IdoraGenerator(
            options => options.RegisterEntity<User>("usr"));

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldMatch(UlidId.ToString());
    }

    [Fact]
    public void Generate_ForAStructEntityType_UsesItsRegisteredPrefix()
    {
        // Arrange
        IIdoraGenerator generator = new IdoraGenerator(
            options => options.RegisterEntity<Payment>("pay"));

        // Act
        string id = generator.Generate<Payment>();

        // Assert
        id.ShouldStartWith("pay_");
    }

    private static IIdoraGenerator CreateGenerator(IdGenerationStrategy strategy) =>
        new IdoraGenerator(options =>
        {
            options.Strategy = strategy;
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
        });
}
