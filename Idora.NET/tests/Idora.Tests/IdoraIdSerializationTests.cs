using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the conversions that let <see cref="IdoraId"/> travel without any setup: JSON through
/// <see cref="IdoraIdJsonConverter"/>, and strings through <see cref="IdoraIdTypeConverter"/>,
/// which configuration binding and MVC model binding rely on.
/// </summary>
public sealed partial class IdoraIdSerializationTests
{
    private const string UlidId = "usr_01k226e0g0008j4ct4ank7f24s";

    [Fact]
    public void Serialize_WritesAPlainString()
    {
        // Act
        string json = JsonSerializer.Serialize(IdoraId.Parse(UlidId));

        // Assert
        json.ShouldBe($"\"{UlidId}\"");
    }

    [Fact]
    public void Deserialize_ReadsAPlainString()
    {
        // Act
        IdoraId id = JsonSerializer.Deserialize<IdoraId>($"\"{UlidId}\"");

        // Assert
        id.ToString().ShouldBe(UlidId);
    }

    [Fact]
    public void RoundTrip_ThroughAnObject_KeepsEveryIdentifier()
    {
        // Arrange
        Envelope original = new(IdoraId.Parse(UlidId), IdoraId.Parse("ord_01k226e0g1zzqdvk5vnacrgxv6"), null);

        // Act
        string json = JsonSerializer.Serialize(original);
        Envelope roundTripped = JsonSerializer.Deserialize<Envelope>(json)!;

        // Assert
        json.ShouldBe($"{{\"Id\":\"{UlidId}\",\"Parent\":\"ord_01k226e0g1zzqdvk5vnacrgxv6\",\"Optional\":null}}");
        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void Deserialize_AnInvalidIdentifier_ThrowsJsonExceptionCarryingTheReason()
    {
        // Act
        JsonException exception =
            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Envelope>("{\"Id\":\"usr_nope\"}"));

        // Assert
        InvalidIdException inner = exception.InnerException.ShouldBeOfType<InvalidIdException>();
        inner.Reason.ShouldBe(InvalidIdReason.Value);
        exception.Path.ShouldBe("$.Id");
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Deserialize_ANonStringToken_ThrowsJsonException(string json)
    {
        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<IdoraId>(json));
    }

    [Fact]
    public void Deserialize_AnEmptyString_IsRejectedLikeAnyOtherInvalidIdentifier()
    {
        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<IdoraId>("\"\""));
    }

    [Fact]
    public void DefaultIdentifier_RoundTripsThroughNull()
    {
        // Act
        string json = JsonSerializer.Serialize(default(IdoraId));
        IdoraId roundTripped = JsonSerializer.Deserialize<IdoraId>(json);

        // Assert
        json.ShouldBe("null");
        roundTripped.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void NullableIdentifier_DistinguishesNullFromAValue()
    {
        // Act
        IdoraId? missing = JsonSerializer.Deserialize<IdoraId?>("null");
        IdoraId? present = JsonSerializer.Deserialize<IdoraId?>($"\"{UlidId}\"");

        // Assert
        missing.ShouldBeNull();
        present.ShouldBe(IdoraId.Parse(UlidId));
        JsonSerializer.Serialize<IdoraId?>(null).ShouldBe("null");
    }

    [Fact]
    public void Dictionary_KeyedByIdentifier_RoundTrips()
    {
        // Arrange
        Dictionary<IdoraId, int> original = new() { [IdoraId.Parse(UlidId)] = 1 };

        // Act
        string json = JsonSerializer.Serialize(original);
        Dictionary<IdoraId, int> roundTripped = JsonSerializer.Deserialize<Dictionary<IdoraId, int>>(json)!;

        // Assert
        json.ShouldBe($"{{\"{UlidId}\":1}}");
        roundTripped[IdoraId.Parse(UlidId)].ShouldBe(1);
    }

    [Fact]
    public void Dictionary_WithAnInvalidKey_ThrowsJsonException()
    {
        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dictionary<IdoraId, int>>("{\"nope\":1}"));
    }

    [Fact]
    public void Dictionary_WithTheEmptyIdentifierAsAKey_RefusesToWriteIt()
    {
        // Arrange
        Dictionary<IdoraId, int> original = new() { [default] = 1 };

        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(original));
    }

    [Fact]
    public void SourceGeneratedContext_UsesTheConverterToo()
    {
        // Arrange
        Envelope original = new(IdoraId.Parse(UlidId), default, IdoraId.Parse(UlidId));

        // Act
        string json = JsonSerializer.Serialize(original, TestJsonContext.Default.Envelope);
        Envelope roundTripped = JsonSerializer.Deserialize(json, TestJsonContext.Default.Envelope)!;

        // Assert
        json.ShouldBe($"{{\"Id\":\"{UlidId}\",\"Parent\":null,\"Optional\":\"{UlidId}\"}}");
        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void TypeDescriptor_FindsTheTypeConverter()
    {
        // Act
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(IdoraId));

        // Assert
        converter.ShouldBeOfType<IdoraIdTypeConverter>();
        converter.CanConvertFrom(typeof(string)).ShouldBeTrue();
        converter.CanConvertTo(typeof(string)).ShouldBeTrue();
        converter.CanConvertFrom(typeof(int)).ShouldBeFalse();
    }

    [Fact]
    public void TypeConverter_ConvertsBothWays()
    {
        // Arrange
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(IdoraId));

        // Act
        object? fromString = converter.ConvertFromInvariantString(UlidId);
        object? toString = converter.ConvertToInvariantString(IdoraId.Parse(UlidId));

        // Assert
        fromString.ShouldBe(IdoraId.Parse(UlidId));
        toString.ShouldBe(UlidId);
    }

    [Fact]
    public void TypeConverter_FromAnInvalidString_ThrowsInvalidIdException()
    {
        // Arrange
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(IdoraId));

        // Act & Assert
        Should.Throw<InvalidIdException>(() => converter.ConvertFromInvariantString("nope"));
    }

    [Fact]
    public void TypeConverter_FromAnUnsupportedType_ThrowsNotSupported()
    {
        // Arrange
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(IdoraId));

        // Act & Assert
        Should.Throw<NotSupportedException>(() => converter.ConvertFrom(null, CultureInfo.InvariantCulture, 42));
    }

    [Fact]
    public void TypeConverter_IsValid_AgreesWithParsing()
    {
        // Arrange
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(IdoraId));

        // Act & Assert
        converter.IsValid(UlidId).ShouldBeTrue();
        converter.IsValid(IdoraId.Parse(UlidId)).ShouldBeTrue();
        converter.IsValid("nope").ShouldBeFalse();
        converter.IsValid(42).ShouldBeFalse();
    }

    [Fact]
    public void ConfigurationBinding_FillsIdentifierProperties()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminId"] = UlidId,
                ["Seed:Optional"] = "ord_01k226e0g1zzqdvk5vnacrgxv6",
            })
            .Build();

        // Act
        SeedSettings settings = configuration.GetSection("Seed").Get<SeedSettings>()!;

        // Assert
        settings.AdminId.ShouldBe(IdoraId.Parse(UlidId));
        settings.Optional.ShouldBe(IdoraId.Parse("ord_01k226e0g1zzqdvk5vnacrgxv6"));
    }

    [Fact]
    public void ConfigurationBinding_WithAnInvalidIdentifier_Fails()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:AdminId"] = "admin" })
            .Build();

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => configuration.GetSection("Seed").Get<SeedSettings>());

        // Assert
        exception.InnerException.ShouldBeOfType<InvalidIdException>();
    }

    /// <summary>A body carrying identifiers in the three shapes a property can take.</summary>
    internal sealed record Envelope(IdoraId Id, IdoraId Parent, IdoraId? Optional);

    /// <summary>Settings bound from configuration.</summary>
    internal sealed class SeedSettings
    {
        public IdoraId AdminId { get; set; }

        public IdoraId? Optional { get; set; }
    }

    /// <summary>Proves the converter attribute is honoured by the source generator, not just reflection.</summary>
    [JsonSerializable(typeof(Envelope))]
    internal sealed partial class TestJsonContext : JsonSerializerContext;
}
