using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Pins down the allocation claim: generating an identifier allocates the returned string and
/// nothing else.
/// </summary>
/// <remarks>
/// Each measurement is compared against the cost of allocating one bare string of the same length,
/// which is the theoretical floor. Comparing against a floor rather than a hard-coded byte count
/// keeps the test honest if the runtime ever changes its object layout.
/// </remarks>
public sealed class AllocationTests
{
    private const int Iterations = 10_000;

    [Fact]
    public void Generate_ForARegisteredEntity_AllocatesOnlyTheReturnedString()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));
        int idLength = generator.Generate<User>().Length;
        long floor = Measure(() => new string('x', idLength));

        // Act
        long actual = Measure(() => generator.Generate<User>());

        // Assert
        ShouldAllocateOnlyTheString(actual, floor);
    }

    [Fact]
    public void Generate_WithAnAdHocPrefix_AllocatesOnlyTheReturnedString()
    {
        // Arrange
        IdoraGenerator generator = new();
        int idLength = generator.Generate("tmp").Length;
        long floor = Measure(() => new string('x', idLength));

        // Act
        long actual = Measure(() => generator.Generate("tmp"));

        // Assert
        ShouldAllocateOnlyTheString(actual, floor);
    }

    [Fact]
    public void Generate_WithTheUuidV7Strategy_AllocatesOnlyTheReturnedString()
    {
        // Arrange
        IdoraGenerator generator = new(options =>
            options.UseUuidV7().RegisterEntity<User>("usr"));
        int idLength = generator.Generate<User>().Length;
        long floor = Measure(() => new string('x', idLength));

        // Act
        long actual = Measure(() => generator.Generate<User>());

        // Assert
        ShouldAllocateOnlyTheString(actual, floor);
    }

    [Fact]
    public void Generate_AcrossManyEntropyBufferRefills_StillAllocatesOnlyTheReturnedStrings()
    {
        // Arrange: 10,000 identifiers consume 100 KB of entropy, so the thread's 1 KiB buffer is
        // refilled around a hundred times during the measurement.
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));
        int idLength = generator.Generate<User>().Length;

        // Act
        long actual = Measure(() => generator.Generate<User>());

        // Assert: refilling must not allocate, or the cost would scale with volume.
        ShouldAllocateOnlyTheString(actual, Measure(() => new string('x', idLength)));
    }

    /// <summary>
    /// Asserts that the measured per-call allocation is the bare string and nothing more.
    /// </summary>
    /// <remarks>
    /// A couple of bytes of slack absorbs rounding in the differencing below. It does not weaken the
    /// claim: the smallest object the runtime can allocate is 24 bytes, so anything within a byte or
    /// two of the floor cannot possibly contain a second allocation.
    /// </remarks>
    private static void ShouldAllocateOnlyTheString(long actual, long floor) =>
        actual.ShouldBeInRange(
            floor - 2,
            floor + 2,
            $"generation should allocate only the returned string ({floor} bytes)");

    /// <summary>
    /// Returns the bytes <paramref name="action"/> allocates per call.
    /// </summary>
    /// <remarks>
    /// Measured as the difference between running the action twice as many times and running it
    /// once as many. Subtracting the two cancels any fixed overhead that lands inside the measured
    /// window &#8212; the runtime re-jitting a method at tier 1, for instance &#8212; leaving only
    /// the cost that actually scales with the number of identifiers generated. That is the number
    /// this package makes a claim about.
    /// </remarks>
    private static long Measure(Func<object> action)
    {
        for (int i = 0; i < 1_000; i++)
        {
            _ = action();
        }

        long single = MeasureTotal(action, Iterations);
        long doubled = MeasureTotal(action, Iterations * 2);

        return (doubled - single) / Iterations;
    }

    /// <summary>Returns the bytes allocated by running <paramref name="action"/> a set number of times.</summary>
    private static long MeasureTotal(Func<object> action, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            _ = action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
