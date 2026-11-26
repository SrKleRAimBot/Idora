using Idora.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Idora.EntityFrameworkCore.Tests;

/// <summary>
/// Covers keys generated on insert against a real SQLite database: the prefix chosen, when the value
/// appears, that it survives a round trip, and what happens when generation cannot work.
/// </summary>
public sealed class KeyGenerationTests : IDisposable
{
    private readonly SqliteDatabase<TestDbContext> _database = new(TestRegistrations.Configure);

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Add_AnEntityWithNoKey_GeneratesOneWithTheRegisteredPrefix()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        User user = new() { Name = "Ada" };

        // Act
        context.Users.Add(user);

        // Assert
        IdoraId.IsValid(user.Id, "usr").ShouldBeTrue();
    }

    [Fact]
    public async Task Add_GeneratesAPermanentValueBeforeSaving()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        User user = new() { Name = "Ada" };

        // Act
        EntityEntry<User> entry = context.Users.Add(user);

        // Assert: usable as a foreign key or in a log line straight away.
        entry.Property(u => u.Id).IsTemporary.ShouldBeFalse();
        entry.State.ShouldBe(EntityState.Added);
    }

    [Fact]
    public async Task SaveChanges_StoresTheGeneratedKeyAndReadsItBack()
    {
        // Arrange
        string id;
        await using (TestDbContext context = _database.CreateContext())
        {
            User user = new() { Name = "Ada" };
            context.Users.Add(user);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = user.Id!;
        }

        // Act
        await using TestDbContext reader = _database.CreateContext();
        User? found = await reader.Users.FindAsync([id], TestContext.Current.CancellationToken);

        // Assert
        found.ShouldNotBeNull();
        found.Name.ShouldBe("Ada");
    }

    [Fact]
    public async Task Add_ManyEntities_GeneratesDistinctKeysInCreationOrder()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        List<Order> orders = Enumerable.Range(0, 200).Select(i => new Order { Total = i }).ToList();

        // Act
        foreach (Order order in orders)
        {
            context.Orders.Add(order);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        List<Order> stored = await context.Orders
            .OrderBy(order => order.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert: ordering by the key column is ordering by creation.
        stored.Select(order => order.Total).ShouldBe(orders.Select(order => order.Total));
        stored.Select(order => order.Id).Distinct().Count().ShouldBe(200);
    }

    [Fact]
    public async Task Add_AnEntityWithAKeyAlreadyAssigned_KeepsIt()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        User user = new() { Id = "usr_01k226e0g0008j4ct4ank7f24s", Name = "Ada" };

        // Act
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        user.Id.ShouldBe("usr_01k226e0g0008j4ct4ank7f24s");
    }

    [Fact]
    public async Task Add_AnEntityGraph_FillsInTheForeignKeyFromTheGeneratedKey()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        User user = new() { Name = "Ada" };
        user.Orders.Add(new Order { Total = 10 });

        // Act
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Order order = user.Orders.Single();
        IdoraId.IsValid(order.Id, "ord").ShouldBeTrue();
        order.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Add_AnIdoraIdKey_GeneratesItAndRoundTripsEveryIdoraIdColumn()
    {
        // Arrange
        IdoraId raisedBy = IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s");
        IdoraId originalId;
        IdoraId correctionId;

        await using (TestDbContext context = _database.CreateContext())
        {
            Invoice original = new() { RaisedBy = raisedBy };
            context.Invoices.Add(original);
            Invoice correction = new() { RaisedBy = raisedBy, CorrectsId = original.Id };
            context.Invoices.Add(correction);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            (originalId, correctionId) = (original.Id, correction.Id);
        }

        // Act
        await using TestDbContext reader = _database.CreateContext();
        Invoice storedOriginal = await reader.Invoices.SingleAsync(i => i.Id == originalId, TestContext.Current.CancellationToken);
        Invoice storedCorrection = await reader.Invoices.SingleAsync(i => i.CorrectsId == originalId, TestContext.Current.CancellationToken);

        // Assert
        originalId.HasPrefix("inv").ShouldBeTrue();
        storedOriginal.CorrectsId.ShouldBeNull();
        storedOriginal.RaisedBy.ShouldBe(raisedBy);
        storedCorrection.Id.ShouldBe(correctionId);
        IdoraId.IsValid(storedOriginal.PublicReference, "inv").ShouldBeTrue();
        storedOriginal.PublicReference.ShouldNotBe(storedOriginal.Id.ToString());
    }

    [Fact]
    public async Task Add_AnIdoraIdWithAnUnassignedColumn_StoresTheEmptyIdentifierAndReadsItBack()
    {
        // Arrange
        IdoraId id;
        await using (TestDbContext context = _database.CreateContext())
        {
            Invoice invoice = new();
            context.Invoices.Add(invoice);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = invoice.Id;
        }

        // Act
        await using TestDbContext reader = _database.CreateContext();
        Invoice stored = await reader.Invoices.SingleAsync(i => i.Id == id, TestContext.Current.CancellationToken);

        // Assert
        stored.RaisedBy.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Query_ARowHoldingSomethingThatIsNotAnIdentifier_FailsRatherThanMaterialisingIt()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Invoices (Id, RaisedBy) VALUES ('inv_01k226e0g0008j4ct4ank7f24s', 'not an identifier')",
            TestContext.Current.CancellationToken);

        // Act & Assert
        await Should.ThrowAsync<InvalidIdException>(() => context.Invoices.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_ADerivedTypeWithNoPrefixOfItsOwn_UsesTheRootsPrefix()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        Dog dog = new();

        // Act
        context.Animals.Add(dog);

        // Assert
        IdoraId.Parse(dog.Id!).Prefix.ShouldBe("ani");
    }

    [Fact]
    public async Task Add_ADerivedTypeRegisteredInItsOwnRight_UsesItsOwnPrefix()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();
        Cat cat = new();

        // Act
        context.Animals.Add(cat);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        IdoraId.Parse(cat.Id!).Prefix.ShouldBe("cat");
    }

    [Fact]
    public async Task Add_AnEntityWhoseTypeIsNotRegistered_ThrowsNotRegistered()
    {
        // Arrange
        await using TestDbContext context = _database.CreateContext();

        // Act
        EntityPrefixNotRegisteredException exception =
            Should.Throw<EntityPrefixNotRegisteredException>(() => context.Notes.Add(new Note()));

        // Assert
        exception.EntityType.ShouldBe(typeof(Note));
    }

    [Fact]
    public void Add_WithAContextThatCannotSeeTheApplicationsServices_ExplainsHowToFixIt()
    {
        // Arrange: a context built by hand, with no route to a container that has Idora in it.
        using Microsoft.Data.Sqlite.SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        DbContextOptions<StandaloneDbContext> options = new DbContextOptionsBuilder<StandaloneDbContext>()
            .UseSqlite(connection)
            .Options;
        using StandaloneDbContext context = new(options);

        // Act
        InvalidOperationException exception =
            Should.Throw<InvalidOperationException>(() => context.Add(new Order()));

        // Assert
        exception.Message.ShouldContain("could not find an IIdoraGenerator");
        exception.Message.ShouldContain("services.AddIdora(...)");
        exception.Message.ShouldContain("UseApplicationServiceProvider");
    }

    [Fact]
    public void Add_WithTheApplicationsServicesSuppliedByHand_Works()
    {
        // Arrange
        IServiceProvider services = new ServiceCollection()
            .AddIdora(options => options.RegisterEntity<Order>("ord"))
            .BuildServiceProvider();
        using Microsoft.Data.Sqlite.SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        DbContextOptions<StandaloneDbContext> options = new DbContextOptionsBuilder<StandaloneDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(services)
            .Options;
        using StandaloneDbContext context = new(options);
        Order order = new();

        // Act
        context.Add(order);

        // Assert
        IdoraId.IsValid(order.Id, "ord").ShouldBeTrue();
    }

    /// <summary>A context with an Idora key and no dependency on the container.</summary>
    private sealed class StandaloneDbContext(DbContextOptions<StandaloneDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.HasIdoraKey(o => o.Id);
                order.Ignore(o => o.User);
                order.Ignore(o => o.UserId);
            });
    }
}
