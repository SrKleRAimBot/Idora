using System.Text.Json;
using Shouldly;
using Idora.Internal;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Holds this package to the shared cross-language conformance vectors in
/// <c>spec/test-vectors.json</c>.
/// </summary>
/// <remarks>
/// Every Idora package runs the same vectors. Passing them is what lets an identifier minted by one
/// language be sorted, split and validated by another without either knowing where it came from.
/// The file is copied next to the test assembly at build time, from the repository's
/// <c>spec</c> folder, so there is exactly one copy of the truth.
/// </remarks>
public sealed class ConformanceTests
{
    private static readonly JsonElement Vectors = LoadVectors();

    public static TheoryData<long, string, string> UlidVectors() => EncodingVectors("ulid");

    public static TheoryData<long, string, string> UuidV7Vectors() => EncodingVectors("uuidv7");

    public static TheoryData<string> ValidPrefixes()
    {
        TheoryData<string> data = [];
        foreach (JsonElement prefix in Vectors.GetProperty("prefixes").GetProperty("valid").EnumerateArray())
        {
            data.Add(prefix.GetString()!);
        }

        return data;
    }

    public static TheoryData<string, string, int> InvalidPrefixes()
    {
        TheoryData<string, string, int> data = [];
        foreach (JsonElement vector in Vectors.GetProperty("prefixes").GetProperty("invalid").EnumerateArray())
        {
            data.Add(
                vector.GetProperty("prefix").GetString()!,
                vector.GetProperty("reason").GetString()!,
                vector.TryGetProperty("index", out JsonElement index) ? index.GetInt32() : -1);
        }

        return data;
    }

    public static TheoryData<string, string> CollidingPairs() => Pairs("collidingPairs");

    public static TheoryData<string, string> DistinctPairs() => Pairs("distinctPairs");

    [Theory]
    [MemberData(nameof(UlidVectors))]
    public void EncodeUlid_MatchesTheSharedVector(long timestampMs, string randomHex, string expected)
    {
        // Arrange
        byte[] random = Convert.FromHexString(randomHex);
        ulong high = ((ulong)random[0] << 8) | random[1];
        ulong low = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(random.AsSpan(2));
        char[] destination = new char[CrockfordBase32.UlidLength];

        // Act
        CrockfordBase32.EncodeUlid(timestampMs, high, low, destination);

        // Assert
        new string(destination).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(UuidV7Vectors))]
    public void ComposeUuidV7_MatchesTheSharedVector(long timestampMs, string randomHex, string expected)
    {
        // Arrange
        byte[] random = Convert.FromHexString(randomHex);

        // Act
        string actual = UuidV7.Compose(timestampMs, random).ToString("N");

        // Assert
        actual.ShouldBe(expected);
    }

    [Fact]
    public void MonotonicStep_MatchesEverySharedVector()
    {
        foreach (JsonElement vector in Vectors.GetProperty("ulidMonotonic").EnumerateArray())
        {
            // Arrange
            JsonElement previous = vector.GetProperty("previous");
            byte[] previousRandom = Convert.FromHexString(previous.GetProperty("randomHex").GetString()!);
            MonotonicUlidState state = new()
            {
                LastTimestampMs = previous.GetProperty("timestampMs").GetInt64(),
                RandomHigh = ((ulong)previousRandom[0] << 8) | previousRandom[1],
                RandomLow = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(previousRandom.AsSpan(2)),
            };
            char[] destination = new char[CrockfordBase32.UlidLength];
            JsonElement expected = vector.GetProperty("expected");
            string description = vector.GetProperty("description").GetString()!;

            // Act
            UlidGenerator.Next(ref state, vector.GetProperty("nowMs").GetInt64(), destination);

            // Assert
            state.LastTimestampMs.ShouldBe(expected.GetProperty("timestampMs").GetInt64(), description);
            if (expected.GetProperty("randomHex").ValueKind == JsonValueKind.String)
            {
                new string(destination).ShouldBe(expected.GetProperty("encoded").GetString(), description);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValidPrefixes))]
    public void Generate_AcceptsEverySharedValidPrefix(string prefix)
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        string id = generator.Generate(prefix);

        // Assert
        id.ShouldStartWith(prefix + "_");
    }

    [Theory]
    [MemberData(nameof(InvalidPrefixes))]
    public void Generate_RejectsEverySharedInvalidPrefixForTheStatedReason(string prefix, string reason, int index)
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        InvalidEntityPrefixException exception =
            Should.Throw<InvalidEntityPrefixException>(() => generator.Generate(prefix));

        // Assert
        exception.Prefix.ShouldBe(prefix);
        switch (reason)
        {
            case "empty":
                exception.Message.ShouldContain("cannot be empty");
                break;
            case "tooLong":
                exception.Message.ShouldContain("the maximum is 16");
                break;
            case "separator":
                exception.Message.ShouldContain($"contains the separator '_' at index {index}");
                break;
            case "character":
                exception.Message.ShouldContain($"contains the character '{prefix[index]}' at index {index}");
                break;
            default:
                throw new InvalidOperationException($"Unknown reason '{reason}' in the shared vectors.");
        }
    }

    [Theory]
    [MemberData(nameof(CollidingPairs))]
    public void RegisterEntity_RejectsEverySharedCollidingPair(string first, string second)
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>(first);

        // Act & Assert
        Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<Order>(second));
    }

    [Theory]
    [MemberData(nameof(DistinctPairs))]
    public void RegisterEntity_AcceptsEverySharedDistinctPair(string first, string second)
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>(first);

        // Act & Assert
        Should.NotThrow(() => options.RegisterEntity<Order>(second));
    }

    private static TheoryData<long, string, string> EncodingVectors(string key)
    {
        TheoryData<long, string, string> data = [];
        foreach (JsonElement vector in Vectors.GetProperty(key).EnumerateArray())
        {
            data.Add(
                vector.GetProperty("timestampMs").GetInt64(),
                vector.GetProperty("randomHex").GetString()!,
                vector.GetProperty("expected").GetString()!);
        }

        return data;
    }

    private static TheoryData<string, string> Pairs(string key)
    {
        TheoryData<string, string> data = [];
        foreach (JsonElement pair in Vectors.GetProperty("prefixes").GetProperty(key).EnumerateArray())
        {
            data.Add(pair[0].GetString()!, pair[1].GetString()!);
        }

        return data;
    }

    private static JsonElement LoadVectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "test-vectors.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
