using System.Collections.Concurrent;
using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers uniqueness, including under concurrency. The concurrent cases double as the thread-safety
/// tests: a torn read of the monotonic state or a shared random buffer would show up here as a
/// duplicate or a malformed identifier.
/// </summary>
public sealed class UniquenessTests
{
    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_OneMillionTimesInSequence_ProducesNoDuplicates(IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);
        const int count = 1_000_000;
        HashSet<string> ids = new(count, StringComparer.Ordinal);

        // Act
        for (int i = 0; i < count; i++)
        {
            ids.Add(generator.Generate<User>());
        }

        // Assert
        ids.Count.ShouldBe(count);
    }

    [Theory]
    [InlineData(IdGenerationStrategy.Ulid)]
    [InlineData(IdGenerationStrategy.UuidV7)]
    public void Generate_FromEveryCoreAtOnce_ProducesNoDuplicates(IdGenerationStrategy strategy)
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(strategy);
        const int count = 100_000;
        ConcurrentBag<string> ids = [];

        // Act
        Parallel.For(
            0,
            count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            _ => ids.Add(generator.Generate<User>()));

        // Assert
        ids.Count.ShouldBe(count);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(count);
    }

    [Fact]
    public void Generate_FromEveryCoreAtOnce_ProducesOnlyWellFormedIdentifiers()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);
        const int count = 100_000;
        ConcurrentBag<string> ids = [];

        // Act
        Parallel.For(0, count, _ => ids.Add(generator.Generate<User>()));

        // Assert: a race on the shared random buffer would most likely surface as a truncated or
        // corrupted value rather than as a duplicate, so the shape is checked too.
        ids.ShouldAllBe(id => id.Length == 30 && id.StartsWith("usr_", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_FromSeveralGeneratorsOnManyThreads_ProducesNoDuplicates()
    {
        // Arrange: separate generator instances share the per-thread monotonic state, which is the
        // arrangement most likely to produce a collision if the state were mishandled.
        IIdoraGenerator first = CreateGenerator(IdGenerationStrategy.Ulid);
        IIdoraGenerator second = CreateGenerator(IdGenerationStrategy.Ulid);
        const int countPerGenerator = 50_000;
        ConcurrentBag<string> ids = [];

        // Act
        Parallel.For(0, countPerGenerator, _ =>
        {
            ids.Add(first.Generate<User>());
            ids.Add(second.Generate<User>());
        });

        // Assert
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(countPerGenerator * 2);
    }

    [Fact]
    public void Generate_WithAnAdHocPrefixConcurrently_ProducesNoDuplicates()
    {
        // Arrange
        IIdoraGenerator generator = CreateGenerator(IdGenerationStrategy.Ulid);
        const int count = 50_000;
        ConcurrentBag<string> ids = [];

        // Act
        Parallel.For(0, count, _ => ids.Add(generator.Generate("tmp")));

        // Assert
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(count);
    }

    private static IIdoraGenerator CreateGenerator(IdGenerationStrategy strategy) =>
        new IdoraGenerator(options =>
        {
            options.Strategy = strategy;
            options.RegisterEntity<User>("usr");
        });
}
