using System.Collections.Concurrent;
using Shouldly;
using UserFriendlyIds.Internal;
using Xunit;

namespace UserFriendlyIds.Tests.Internal;

/// <summary>
/// Covers the buffered entropy source: that it keeps producing fresh bytes across buffer refills,
/// that oversized requests still work, and that threads never see each other's bytes.
/// </summary>
public sealed class ThreadLocalCryptoRandomTests
{
    [Fact]
    public void Fill_ProducesDifferentBytesOnSuccessiveCalls()
    {
        // Arrange
        byte[] first = new byte[16];
        byte[] second = new byte[16];

        // Act
        ThreadLocalCryptoRandom.Fill(first);
        ThreadLocalCryptoRandom.Fill(second);

        // Assert
        first.ShouldNotBe(second);
    }

    [Fact]
    public void Fill_AcrossManyBufferRefills_NeverRepeatsABlock()
    {
        // Arrange: 16 bytes at a time for well past the 1 KiB buffer, so refills are exercised
        // repeatedly rather than once.
        const int count = 5_000;
        HashSet<string> blocks = [];
        byte[] buffer = new byte[16];

        // Act
        for (int i = 0; i < count; i++)
        {
            ThreadLocalCryptoRandom.Fill(buffer);
            blocks.Add(Convert.ToHexString(buffer));
        }

        // Assert
        blocks.Count.ShouldBe(count);
    }

    [Fact]
    public void Fill_WithARequestLargerThanTheBuffer_StillFillsEveryByte()
    {
        // Arrange: larger than the 1 KiB internal buffer, so the request bypasses it.
        byte[] destination = new byte[4096];

        // Act
        ThreadLocalCryptoRandom.Fill(destination);

        // Assert
        destination.ShouldContain(value => value != 0);
        destination.Distinct().Count().ShouldBeGreaterThan(200);
    }

    [Fact]
    public void Fill_WithAnEmptySpan_DoesNothingAndDoesNotThrow()
    {
        // Arrange
        byte[] destination = [];

        // Act
        Action act = () => ThreadLocalCryptoRandom.Fill(destination);

        // Assert
        act.ShouldNotThrow();
    }

    [Fact]
    public void NextUlidRandomness_KeepsTheHighWordWithinSixteenBits()
    {
        // Arrange & Act & Assert
        for (int i = 0; i < 10_000; i++)
        {
            ThreadLocalCryptoRandom.NextUlidRandomness(out ulong high, out ulong low);

            high.ShouldBeLessThanOrEqualTo(0xFFFFUL);
            _ = low;
        }
    }

    [Fact]
    public void NextUlidRandomness_ProducesDistinctValues()
    {
        // Arrange
        const int count = 20_000;
        HashSet<UInt128> values = [];

        // Act
        for (int i = 0; i < count; i++)
        {
            ThreadLocalCryptoRandom.NextUlidRandomness(out ulong high, out ulong low);
            values.Add(((UInt128)high << 64) | low);
        }

        // Assert
        values.Count.ShouldBe(count);
    }

    [Fact]
    public void NextUlidRandomness_OnManyThreadsAtOnce_NeverHandsOutTheSameValueTwice()
    {
        // Arrange
        const int perThread = 10_000;
        int threadCount = Math.Max(2, Environment.ProcessorCount);
        ConcurrentBag<UInt128> values = [];

        // Act
        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < perThread; i++)
            {
                ThreadLocalCryptoRandom.NextUlidRandomness(out ulong high, out ulong low);
                values.Add(((UInt128)high << 64) | low);
            }
        });

        // Assert: each thread owns its buffer, so a shared-state bug would show up as a repeat.
        values.Distinct().Count().ShouldBe(threadCount * perThread);
    }
}
