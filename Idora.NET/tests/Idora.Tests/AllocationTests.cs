using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Pins down the allocation claims: generating an identifier allocates the returned string and
/// nothing else, and parsing, checking and comparing one allocates nothing at all.
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

    [Fact]
    public void Parse_AString_AllocatesNothing()
    {
        // Arrange
        string text = new IdoraGenerator().Generate("usr");

        // Act
        long actual = Measure(() => IdoraId.Parse(text));

        // Assert: the identifier wraps the caller's string rather than copying it. The only bytes
        // measured are the box the harness puts the returned struct in.
        ShouldAllocateExactly(actual, BoxedIdoraIdSize());
    }

    [Fact]
    public void ParseAndCheck_WithAnExpectedPrefix_AllocatesNothing()
    {
        // Arrange
        string ulid = new IdoraGenerator().Generate("usr");
        string uuid = new IdoraGenerator(options => options.UseUuidV7()).Generate("usr");

        // Act
        long parse = Measure(() => IdoraId.Parse(uuid, "usr"));
        long tryParse = Measure(() => IdoraId.TryParse(ulid, "usr", out IdoraId id) ? id : default);
        long isValid = MeasureBoolean(() => IdoraId.IsValid(ulid, "usr"));
        long rejected = MeasureBoolean(() => IdoraId.TryParse("usr_not-an-identifier", out _));

        // Assert
        ShouldAllocateExactly(parse, BoxedIdoraIdSize());
        ShouldAllocateExactly(tryParse, BoxedIdoraIdSize());
        ShouldAllocateExactly(isValid, 0);
        ShouldAllocateExactly(rejected, 0);
    }

    [Fact]
    public void Parse_ASpan_AllocatesOnlyTheIdentifiersString()
    {
        // Arrange
        string text = new IdoraGenerator().Generate("usr");
        long floor = Measure(() => new string('x', text.Length));

        // Act
        long actual = Measure(() => IdoraId.Parse(text.AsSpan()));

        // Assert
        ShouldAllocateOnlyTheString(actual - BoxedIdoraIdSize(), floor);
    }

    [Fact]
    public void ComparingAndInspecting_AllocatesNothing()
    {
        // Arrange
        IdoraGenerator generator = new();
        IdoraId first = IdoraId.Parse(generator.Generate("usr"));
        IdoraId second = IdoraId.Parse(generator.Generate("ord"));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        long actual = MeasureBoolean(() =>
            first.CompareTo(second) < 0
            && first.IsBefore(second)
            && first != second
            && first.HasPrefix("usr")
            && !first.IsCreatedAfter(now)
            && first.CompareTimestamp(second) <= 0
            && first.Timestamp <= now
            && first.GetHashCode() != second.GetHashCode());

        // Assert
        ShouldAllocateExactly(actual, 0);
    }

    /// <summary>
    /// Asserts that the measured per-call allocation is <paramref name="expected"/> bytes, with the
    /// same couple of bytes' slack as <see cref="ShouldAllocateOnlyTheString"/> and for the same
    /// reason: no second object fits in it.
    /// </summary>
    private static void ShouldAllocateExactly(long actual, long expected) =>
        actual.ShouldBeInRange(expected - 2, expected + 2, $"expected {expected} bytes per call");

    /// <summary>
    /// What the measuring harness itself allocates per call to return an <see cref="IdoraId"/>
    /// through <see cref="Func{TResult}"/> of <see cref="object"/>: one box. Measured rather than
    /// hard-coded, for the same reason the string floor is.
    /// </summary>
    private static long BoxedIdoraIdSize() => Measure(() => default(IdoraId));

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

    /// <summary>
    /// Returns the bytes a predicate allocates per call, with the result kept unboxed so that the
    /// harness adds nothing of its own.
    /// </summary>
    /// <remarks>Differenced exactly as <see cref="Measure"/> is, to cancel fixed overhead.</remarks>
    private static long MeasureBoolean(Func<bool> predicate)
    {
        for (int i = 0; i < 1_000; i++)
        {
            _ = predicate();
        }

        long single = MeasureBooleanTotal(predicate, Iterations);
        long doubled = MeasureBooleanTotal(predicate, Iterations * 2);

        return (doubled - single) / Iterations;
    }

    private static long MeasureBooleanTotal(Func<bool> predicate, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            _ = predicate();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
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
