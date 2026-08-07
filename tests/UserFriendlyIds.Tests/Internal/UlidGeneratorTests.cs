using Shouldly;
using UserFriendlyIds.Internal;
using UserFriendlyIds.Tests.Support;
using Xunit;

namespace UserFriendlyIds.Tests.Internal;

/// <summary>
/// Covers the monotonic ULID logic directly, with the clock supplied as a parameter so that the
/// awkward cases are deterministic instead of dependent on timing luck.
/// </summary>
public sealed class UlidGeneratorTests
{
    private const long ArbitraryTimestamp = 1_700_000_000_000;

    [Fact]
    public void Next_OnFirstUse_StampsTheSuppliedTimestamp()
    {
        // Arrange
        MonotonicUlidState state = default;
        char[] destination = new char[26];

        // Act
        UlidGenerator.Next(ref state, ArbitraryTimestamp, destination);

        // Assert
        state.LastTimestampMs.ShouldBe(ArbitraryTimestamp);
        UlidDecoder.DecodeTimestamp(new string(destination)).ShouldBe(ArbitraryTimestamp);
    }

    [Fact]
    public void Next_WithinTheSameMillisecond_IncrementsTheRandomFieldByExactlyOne()
    {
        // Arrange
        MonotonicUlidState state = default;
        char[] first = new char[26];
        char[] second = new char[26];
        UlidGenerator.Next(ref state, ArbitraryTimestamp, first);

        // Act
        UlidGenerator.Next(ref state, ArbitraryTimestamp, second);

        // Assert
        UInt128 before = UlidDecoder.DecodeRandomness(new string(first));
        UInt128 after = UlidDecoder.DecodeRandomness(new string(second));
        after.ShouldBe(before + 1);
    }

    [Fact]
    public void Next_ForANewMillisecond_DrawsFreshRandomnessRatherThanCounting()
    {
        // Arrange
        MonotonicUlidState state = default;
        char[] first = new char[26];
        char[] second = new char[26];
        UlidGenerator.Next(ref state, ArbitraryTimestamp, first);

        // Act
        UlidGenerator.Next(ref state, ArbitraryTimestamp + 1, second);

        // Assert
        UInt128 before = UlidDecoder.DecodeRandomness(new string(first));
        UInt128 after = UlidDecoder.DecodeRandomness(new string(second));
        after.ShouldNotBe(before + 1);
        UlidDecoder.DecodeTimestamp(new string(second)).ShouldBe(ArbitraryTimestamp + 1);
    }

    [Fact]
    public void Next_WhenTheClockStepsBackwards_ReusesTheLastTimestampInsteadOfRegressing()
    {
        // Arrange
        MonotonicUlidState state = default;
        char[] first = new char[26];
        char[] second = new char[26];
        UlidGenerator.Next(ref state, ArbitraryTimestamp, first);

        // Act: an NTP correction or a resumed snapshot moves the clock back a full second.
        UlidGenerator.Next(ref state, ArbitraryTimestamp - 1_000, second);

        // Assert
        state.LastTimestampMs.ShouldBe(ArbitraryTimestamp);
        StringComparer.Ordinal.Compare(new string(second), new string(first)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Next_WhenTheRandomFieldIsExhausted_BorrowsTheNextMillisecond()
    {
        // Arrange: the counter is one step from wrapping, which takes 2^80 identifiers inside one
        // millisecond to reach in reality.
        MonotonicUlidState state = new()
        {
            LastTimestampMs = ArbitraryTimestamp,
            RandomHigh = 0xFFFF,
            RandomLow = ulong.MaxValue,
        };
        char[] destination = new char[26];

        // Act
        UlidGenerator.Next(ref state, ArbitraryTimestamp, destination);

        // Assert
        state.LastTimestampMs.ShouldBe(ArbitraryTimestamp + 1);
        UlidDecoder.DecodeTimestamp(new string(destination)).ShouldBe(ArbitraryTimestamp + 1);
    }

    [Fact]
    public void Next_WhenTheLowWordWraps_CarriesIntoTheHighWord()
    {
        // Arrange
        MonotonicUlidState state = new()
        {
            LastTimestampMs = ArbitraryTimestamp,
            RandomHigh = 0,
            RandomLow = ulong.MaxValue,
        };
        char[] destination = new char[26];

        // Act
        UlidGenerator.Next(ref state, ArbitraryTimestamp, destination);

        // Assert
        state.RandomHigh.ShouldBe(1UL);
        state.RandomLow.ShouldBe(0UL);
        UlidDecoder.DecodeRandomness(new string(destination)).ShouldBe(UInt128.One << 64);
    }

    [Fact]
    public void Next_CalledRepeatedlyWithAFrozenClock_StillProducesStrictlyIncreasingValues()
    {
        // Arrange
        MonotonicUlidState state = default;
        List<string> ids = [];
        char[] destination = new char[26];

        // Act: the clock never moves, so every identifier comes from the monotonic counter.
        for (int i = 0; i < 1_000; i++)
        {
            UlidGenerator.Next(ref state, ArbitraryTimestamp, destination);
            ids.Add(new string(destination));
        }

        // Assert
        ids.ShouldBe([.. ids.OrderBy(id => id, StringComparer.Ordinal)]);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void CurrentTimestampMs_MatchesTheFrameworksOwnUnixTime()
    {
        // Arrange
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        long actual = UlidGenerator.CurrentTimestampMs();

        // Assert
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        actual.ShouldBeInRange(before, after);
    }
}
