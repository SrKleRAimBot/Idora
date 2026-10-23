using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the promise that makes these identifiers worth using as keys: sorting them as plain
/// strings puts them in the order they were created.
/// </summary>
public sealed class OrderingTests
{
    [Fact]
    public void Generate_WithUlidStrategy_ProducesStrictlyIncreasingIdentifiersOnOneThread()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);
        const int count = 10_000;

        // Act
        string[] ids = [.. Enumerable.Range(0, count).Select(_ => generator.Generate<User>())];

        // Assert: the ULID monotonic counter guarantees this even for identifiers that share a
        // millisecond, so no tolerance is needed.
        for (int i = 1; i < ids.Length; i++)
        {
            StringComparer.Ordinal.Compare(ids[i], ids[i - 1]).ShouldBeGreaterThan(
                0,
                $"identifier {i} ('{ids[i]}') should sort after identifier {i - 1} ('{ids[i - 1]}')");
        }
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_ForEitherStrategy_ProducesIncreasingIdentifiersAcrossMillisecondBoundaries(
        IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);
        List<string> ids = [];

        // Act: a pause between each one guarantees a different millisecond, which is the granularity
        // both strategies order at.
        for (int i = 0; i < 20; i++)
        {
            ids.Add(generator.Generate<User>());
            Thread.Sleep(2);
        }

        // Assert
        ids.ShouldBe([.. ids.OrderBy(id => id, StringComparer.Ordinal)]);
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_ForEitherStrategy_SortsIdentifiersOfDifferentEntitiesByPrefixThenTime(
        IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);

        // Act
        string firstOrder = generator.Generate<Order>();
        Thread.Sleep(2);
        string secondOrder = generator.Generate<Order>();
        string user = generator.Generate<User>();

        // Assert: identifiers group by prefix first, and within a prefix they are in time order.
        // That is what makes an index on the column useful rather than merely sorted.
        StringComparer.Ordinal.Compare(secondOrder, firstOrder).ShouldBeGreaterThan(0);
        StringComparer.Ordinal.Compare(user, firstOrder).ShouldBeGreaterThan(0, "'usr' sorts after 'ord'");
    }

    [Fact]
    public void Generate_WithUlidStrategy_KeepsTheSequenceIncreasingUnderConcurrentUse()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);
        const int idsPerThread = 2_000;
        int threadCount = Math.Max(2, Environment.ProcessorCount);

        // Act: each thread keeps its own monotonic sequence, so ordering is asserted per thread.
        // Across threads the guarantee is millisecond granularity, which is all any implementation
        // can offer without a shared lock.
        string[][] perThread = new string[threadCount][];
        Parallel.For(0, threadCount, threadIndex =>
        {
            string[] ids = new string[idsPerThread];
            for (int i = 0; i < idsPerThread; i++)
            {
                ids[i] = generator.Generate<User>();
            }

            perThread[threadIndex] = ids;
        });

        // Assert
        foreach (string[] ids in perThread)
        {
            ids.ShouldBe([.. ids.OrderBy(id => id, StringComparer.Ordinal)]);
        }
    }

    [Fact]
    public void Generate_WithUlidStrategy_EncodesANonDecreasingTimestamp()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);

        // Act
        long[] timestamps = [.. Enumerable
            .Range(0, 1_000)
            .Select(_ => UlidDecoder.DecodeTimestamp(UlidDecoder.Split(generator.Generate<User>()).Value))];

        // Assert
        for (int i = 1; i < timestamps.Length; i++)
        {
            timestamps[i].ShouldBeGreaterThanOrEqualTo(timestamps[i - 1]);
        }
    }

    private static IIdoraGenerator CreateGenerator(IdGenerationStrategy strategy) =>
        new IdoraGenerator(options =>
        {
            options.Strategy = strategy;
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
        });
}
