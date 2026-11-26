using Idora.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Idora.EntityFrameworkCore.Tests;

/// <summary>
/// Covers creation-time range queries over the key column: that they translate to plain SQL
/// comparisons with parameters, and that they select exactly the right rows, edges included.
/// </summary>
public sealed class RangeQueryTests : IDisposable
{
    /// <summary>Three milliseconds, one second apart, that the seeded orders are stamped with.</summary>
    private const long First = 1_754_568_000_000;
    private const long Second = First + 1_000;
    private const long Third = First + 2_000;

    private readonly SqliteDatabase<TestDbContext> _database = new(TestRegistrations.Configure);

    public RangeQueryTests()
    {
        using TestDbContext context = _database.CreateContext();

        // For each millisecond, an order at the very bottom of its range, one in the middle and one
        // at the very top: the cases an off-by-one in either bound would get wrong.
        foreach (long millisecond in new[] { First, Second, Third })
        {
            context.Orders.Add(new Order { Id = IdoraId.LowerBound("ord", millisecond), Total = millisecond });
            context.Orders.Add(new Order { Id = Middle("ord", millisecond), Total = millisecond });
            context.Orders.Add(new Order { Id = IdoraId.UpperBound("ord", millisecond), Total = millisecond });
        }

        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public void WhereCreatedBetween_TranslatesToTwoParameterisedComparisons()
    {
        // Arrange
        using TestDbContext context = _database.CreateContext();

        // Act
        string sql = context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(First), At(Second))
            .ToQueryString();

        // Assert: index-friendly comparisons on the column, with the bounds sent as parameters.
        sql.ShouldContain("\"o\".\"Id\" >= @");
        sql.ShouldContain("\"o\".\"Id\" <= @");
        sql.ShouldNotContain("WHERE 'ord_");
    }

    [Fact]
    public async Task WhereCreatedBetween_OneMillisecond_SelectsEveryRowFromItAndNoOther()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act
        List<Order> orders = await context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(Second), At(Second))
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        orders.Count.ShouldBe(3);
        orders.ShouldAllBe(order => order.Total == Second);
    }

    [Fact]
    public async Task WhereCreatedBetween_IncludesBothEnds()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act
        int count = await context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(First), At(Third))
            .CountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(9);
    }

    [Fact]
    public async Task WhereCreatedBetween_IgnoresTheSubMillisecondPartOfEachInstant()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act: 0.9ms into Second, to 0.9ms into Second; still the whole of that millisecond.
        int count = await context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(Second).AddTicks(9_000), At(Second).AddTicks(9_999))
            .CountAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(3);
    }

    [Fact]
    public async Task WhereCreatedBetween_ExcludesTheMillisecondsEitherSide()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act
        int before = await context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(First + 1), At(Second - 1))
            .CountAsync(TestContext.Current.CancellationToken);

        // Assert
        before.ShouldBe(0);
    }

    [Fact]
    public async Task WhereCreatedBetween_ComposesWithTheRestOfTheQuery()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act
        List<string> ids = await context.Orders
            .WhereCreatedBetween(order => order.Id, "ord", At(First), At(Third))
            .Where(order => order.Total > First)
            .OrderByDescending(order => order.Id)
            .Select(order => order.Id)
            .Take(2)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        ids.ShouldBe([IdoraId.UpperBound("ord", Third).ToString(), Middle("ord", Third)]);
    }

    [Fact]
    public async Task WhereCreatedBetween_FindsGeneratedKeysByWhenTheyWereCreated()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        DateTimeOffset from = DateTimeOffset.UtcNow;
        Order order = new() { Total = 42 };
        context.Orders.Add(order);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        DateTimeOffset to = DateTimeOffset.UtcNow;

        // Act
        List<Order> found = await context.Orders
            .WhereCreatedBetween(o => o.Id, "ord", from, to)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        found.ShouldHaveSingleItem().Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task WhereIdBetween_TakesBoundsComputedElsewhere()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        IdoraId after = IdoraId.Parse(Middle("ord", First));

        // Act: a cursor, resuming after the last identifier a client saw.
        int count = await context.Orders
            .WhereIdBetween(order => order.Id, after, IdoraId.UpperBound("ord", Third))
            .CountAsync(TestContext.Current.CancellationToken);

        // Assert: the middle row itself and everything after it.
        count.ShouldBe(8);
    }

    [Fact]
    public void WhereIdBetween_WithAnEmptyBound_Throws()
    {
        // Arrange
        using TestDbContext context = _database.CreateContext();
        IdoraId bound = IdoraId.LowerBound("ord", First);

        // Act & Assert
        Should.Throw<ArgumentException>(() => context.Orders.WhereIdBetween(o => o.Id, default, bound))
            .ParamName.ShouldBe("lower");
        Should.Throw<ArgumentException>(() => context.Orders.WhereIdBetween(o => o.Id, bound, default))
            .ParamName.ShouldBe("upper");
    }

    [Fact]
    public void WhereCreatedBetween_WithBadArguments_Throws()
    {
        // Arrange
        using TestDbContext context = _database.CreateContext();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ((IQueryable<Order>)null!).WhereCreatedBetween(o => o.Id, "ord", At(First), At(Second)));
        Should.Throw<ArgumentNullException>(() => context.Orders.WhereCreatedBetween(null!, "ord", At(First), At(Second)));
        Should.Throw<ArgumentNullException>(() => context.Orders.WhereCreatedBetween(o => o.Id, null!, At(First), At(Second)));
        Should.Throw<InvalidEntityPrefixException>(() => context.Orders.WhereCreatedBetween(o => o.Id, "o_d", At(First), At(Second)));
        Should.Throw<ArgumentOutOfRangeException>(() => context.Orders.WhereCreatedBetween(o => o.Id, "ord", DateTimeOffset.MinValue, At(Second)));
    }

    [Fact]
    public void WhereCreatedBetween_OverLinqToObjects_SelectsTheSameRows()
    {
        // Arrange: the same predicate, evaluated in memory rather than by the database.
        string[] ids =
        [
            IdoraId.LowerBound("ord", First),
            Middle("ord", Second),
            IdoraId.UpperBound("ord", Third),
        ];
        IQueryable<Order> orders = ids.Select(id => new Order { Id = id }).AsQueryable();

        // Act
        List<Order> found = orders.WhereCreatedBetween(o => o.Id, "ord", At(Second), At(Second)).ToList();

        // Assert
        found.ShouldHaveSingleItem().Id.ShouldBe(ids[1]);
    }

    [Fact]
    public void WhereCreatedBetween_OnAnIdoraIdColumn_TranslatesToTwoParameterisedComparisons()
    {
        // Arrange
        using TestDbContext context = _database.CreateContext();

        // Act
        string sql = context.Invoices
            .WhereCreatedBetween(invoice => invoice.Id, "inv", At(First), At(Second))
            .ToQueryString();

        // Assert
        sql.ShouldContain("\"i\".\"Id\" >= @");
        sql.ShouldContain("\"i\".\"Id\" <= @");
        sql.ShouldContain("'inv_01k226e0g00000000000000000'");
    }

    [Fact]
    public async Task WhereCreatedBetween_OnAnIdoraIdColumn_SelectsTheRightRows()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        foreach (long millisecond in new[] { First, Second, Third })
        {
            context.Invoices.Add(new Invoice { Id = IdoraId.LowerBound("inv", millisecond) });
            context.Invoices.Add(new Invoice { Id = IdoraId.UpperBound("inv", millisecond) });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        List<IdoraId> ids = await context.Invoices
            .WhereCreatedBetween(invoice => invoice.Id, "inv", At(Second), At(Third))
            .OrderBy(invoice => invoice.Id)
            .Select(invoice => invoice.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        ids.ShouldBe(
        [
            IdoraId.LowerBound("inv", Second),
            IdoraId.UpperBound("inv", Second),
            IdoraId.LowerBound("inv", Third),
            IdoraId.UpperBound("inv", Third),
        ]);
    }

    [Fact]
    public void WhereIdBetween_OnAnIdoraIdColumn_WithAnEmptyBound_Throws()
    {
        // Arrange
        using TestDbContext context = _database.CreateContext();
        IdoraId bound = IdoraId.LowerBound("inv", First);

        // Act & Assert
        Should.Throw<ArgumentException>(() => context.Invoices.WhereIdBetween(i => i.Id, default, bound))
            .ParamName.ShouldBe("lower");
        Should.Throw<ArgumentNullException>(() => context.Invoices.WhereIdBetween((System.Linq.Expressions.Expression<Func<Invoice, IdoraId>>)null!, bound, bound));
    }

    [Fact]
    public void WhereCreatedBetween_OnAnIdoraIdColumnInMemory_SelectsTheSameRows()
    {
        // Arrange
        IQueryable<Invoice> invoices = new[] { First, Second, Third }
            .Select(millisecond => new Invoice { Id = IdoraId.UpperBound("inv", millisecond) })
            .AsQueryable();

        // Act
        List<Invoice> found = invoices.WhereCreatedBetween(i => i.Id, "inv", At(Second), At(Second)).ToList();

        // Assert
        found.ShouldHaveSingleItem().Id.TimestampMilliseconds.ShouldBe(Second);
    }

    private static DateTimeOffset At(long millisecond) => DateTimeOffset.FromUnixTimeMilliseconds(millisecond);

    /// <summary>An identifier in the middle of a millisecond's range: random field 0x8000…0.</summary>
    private static string Middle(string prefix, long millisecond) =>
        string.Concat(IdoraId.LowerBound(prefix, millisecond).ToString().AsSpan()[..^16], "g000000000000000");
}
