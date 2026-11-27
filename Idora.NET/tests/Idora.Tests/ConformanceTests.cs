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

    public static TheoryData<string, string, string, string, long> ValidIds()
    {
        TheoryData<string, string, string, string, long> data = [];
        foreach (JsonElement vector in Ids().GetProperty("valid").EnumerateArray())
        {
            data.Add(
                vector.GetProperty("id").GetString()!,
                vector.GetProperty("prefix").GetString()!,
                vector.GetProperty("value").GetString()!,
                vector.GetProperty("format").GetString()!,
                vector.GetProperty("timestampMs").GetInt64());
        }

        return data;
    }

    public static TheoryData<string, string, string?> InvalidIds()
    {
        TheoryData<string, string, string?> data = [];
        foreach (JsonElement vector in Ids().GetProperty("invalid").EnumerateArray())
        {
            data.Add(
                vector.GetProperty("id").GetString()!,
                vector.GetProperty("reason").GetString()!,
                vector.TryGetProperty("expectedPrefix", out JsonElement expected) ? expected.GetString() : null);
        }

        return data;
    }

    public static TheoryData<long, string, string, string> Bounds()
    {
        TheoryData<long, string, string, string> data = [];
        foreach (JsonElement vector in Ids().GetProperty("bounds").EnumerateArray())
        {
            data.Add(
                vector.GetProperty("timestampMs").GetInt64(),
                vector.GetProperty("format").GetString()!,
                vector.GetProperty("lower").GetString()!,
                vector.GetProperty("upper").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void Parse_ReadsEverySharedValidIdentifier(
        string text,
        string prefix,
        string value,
        string format,
        long timestampMs)
    {
        // Act
        IdoraId id = IdoraId.Parse(text);

        // Assert
        id.Prefix.ShouldBe(prefix);
        id.Value.ShouldBe(value);
        id.Format.ShouldBe(ToStrategy(format));
        id.TimestampMilliseconds.ShouldBe(timestampMs);
        id.ToString().ShouldBe(text);
        IdoraId.IsValid(text).ShouldBeTrue();
        IdoraId.IsValid(text, prefix).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Parse_RejectsEverySharedInvalidIdentifierForTheStatedReason(
        string text,
        string reason,
        string? expectedPrefix)
    {
        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() =>
            expectedPrefix is null ? IdoraId.Parse(text) : IdoraId.Parse(text, expectedPrefix));

        // Assert
        exception.Reason.ShouldBe(ToReason(reason));
        exception.Text.ShouldBe(text);
        exception.ExpectedPrefix.ShouldBe(expectedPrefix);
        if (exception.Reason == InvalidIdReason.Prefix)
        {
            exception.InnerException.ShouldBeOfType<InvalidEntityPrefixException>();
        }

        bool valid = expectedPrefix is null ? IdoraId.IsValid(text) : IdoraId.IsValid(text, expectedPrefix);
        valid.ShouldBeFalse();
    }

    [Fact]
    public void Sort_ReproducesTheSharedNaturalOrderFromEveryRotationAndItsReverse()
    {
        // Arrange
        IdoraId[] expected = Ids().GetProperty("ordered").EnumerateArray()
            .Select(element => IdoraId.Parse(element.GetString()!))
            .ToArray();

        // Each rotation of the list and of its reverse puts every element in every position, and a
        // seeded shuffle adds orders that no rotation produces.
        List<IdoraId[]> permutations = [];
        IdoraId[] reversed = expected.Reverse().ToArray();
        for (int shift = 0; shift < expected.Length; shift++)
        {
            permutations.Add([.. expected[shift..], .. expected[..shift]]);
            permutations.Add([.. reversed[shift..], .. reversed[..shift]]);
        }

        Random random = new(20250807);
        for (int i = 0; i < 50; i++)
        {
            IdoraId[] shuffled = [.. expected];
            random.Shuffle(shuffled);
            permutations.Add(shuffled);
        }

        foreach (IdoraId[] permutation in permutations)
        {
            // Act
            Array.Sort(permutation);

            // Assert
            permutation.ShouldBe(expected);
        }
    }

    [Fact]
    public void CompareTo_AgreesWithTheSharedNaturalOrderForEveryPair()
    {
        // Arrange
        IdoraId[] ordered = Ids().GetProperty("ordered").EnumerateArray()
            .Select(element => IdoraId.Parse(element.GetString()!))
            .ToArray();

        for (int i = 0; i < ordered.Length; i++)
        {
            for (int j = 0; j < ordered.Length; j++)
            {
                // Act
                int comparison = ordered[i].CompareTo(ordered[j]);

                // Assert
                Math.Sign(comparison).ShouldBe(i.CompareTo(j), $"{ordered[i]} vs {ordered[j]}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Bounds))]
    public void Bounds_MatchTheSharedVectorsAndParse(long timestampMs, string format, string lower, string upper)
    {
        // Arrange
        IdGenerationStrategy strategy = ToStrategy(format);

        // Act
        IdoraId lowerBound = IdoraId.LowerBound("usr", timestampMs, strategy);
        IdoraId upperBound = IdoraId.UpperBound("usr", timestampMs, strategy);

        // Assert
        lowerBound.ToString().ShouldBe("usr_" + lower);
        upperBound.ToString().ShouldBe("usr_" + upper);
        IdoraId.Parse(lowerBound.ToString()).TimestampMilliseconds.ShouldBe(timestampMs);
        IdoraId.Parse(upperBound.ToString()).TimestampMilliseconds.ShouldBe(timestampMs);
        IdoraId.Parse(lowerBound.ToString()).Format.ShouldBe(strategy);
    }

    private static IdGenerationStrategy ToStrategy(string format) => format switch
    {
        "ulid" => IdGenerationStrategy.Ulid,
        "uuidv7" => IdGenerationStrategy.UuidV7,
        _ => throw new InvalidOperationException($"Unknown format '{format}' in the shared vectors."),
    };

    private static InvalidIdReason ToReason(string reason) => reason switch
    {
        "noSeparator" => InvalidIdReason.NoSeparator,
        "prefix" => InvalidIdReason.Prefix,
        "value" => InvalidIdReason.Value,
        "prefixMismatch" => InvalidIdReason.PrefixMismatch,
        _ => throw new InvalidOperationException($"Unknown reason '{reason}' in the shared vectors."),
    };

    private static JsonElement Ids() => Vectors.GetProperty("ids");

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
