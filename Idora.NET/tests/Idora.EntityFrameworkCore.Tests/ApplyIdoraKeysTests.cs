using Idora.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace Idora.EntityFrameworkCore.Tests;

/// <summary>
/// Covers the model-wide convention: every registered entity's key configured in one call, and
/// everything else left alone.
/// </summary>
public sealed class ApplyIdoraKeysTests : IDisposable
{
    private readonly SqliteDatabase<ConventionDbContext> _database = new(options => options
        .RegisterEntity<Customer>("cus")
        .RegisterEntity<Shipment>("shp")
        .RegisterEntity<Counter>("cnt")
        .RegisterEntity<Parcel>("par"));

    public void Dispose() => _database.Dispose();

    [Fact]
    public void ApplyIdoraKeys_ConfiguresARegisteredStringKeyFoundByConvention()
    {
        // Act
        IProperty id = Property<Customer>(nameof(Customer.Id));

        // Assert
        id.IsPrimaryKey().ShouldBeTrue();
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetMaxLength().ShouldBe(30);
        id.IsUnicode().ShouldBe(false);
    }

    [Fact]
    public void ApplyIdoraKeys_ConfiguresARegisteredIdoraIdKey()
    {
        // Act
        IProperty id = Property<Shipment>(nameof(Shipment.Id));

        // Assert
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetMaxLength().ShouldBe(30);
        id.GetValueConverter().ShouldBeOfType<IdoraIdValueConverter>();
    }

    [Fact]
    public void ApplyIdoraKeys_LeavesARegisteredTypeWithAnIntegerKeyAlone()
    {
        // Act
        IProperty id = Property<Counter>(nameof(Counter.Id));

        // Assert: still the database's identity column, exactly as by convention.
        id.GetValueGeneratorFactory().ShouldBeNull();
        id.GetMaxLength().ShouldBeNull();
    }

    [Fact]
    public void ApplyIdoraKeys_LeavesAnUnregisteredTypeAlone()
    {
        // Act
        IProperty id = Property<Memo>(nameof(Memo.Id));

        // Assert
        id.ValueGenerated.ShouldBe(ValueGenerated.Never);
        id.GetMaxLength().ShouldBeNull();
    }

    [Fact]
    public void ApplyIdoraKeys_LeavesACompositeKeyAlone()
    {
        // Act
        IProperty id = Property<Parcel>(nameof(Parcel.Id));

        // Assert
        id.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    [Fact]
    public async Task ApplyIdoraKeys_GeneratesKeysOnInsert()
    {
        // Arrange
        await using ConventionDbContext context = _database.CreateContext();
        Customer customer = new();
        Shipment shipment = new();

        // Act
        context.AddRange(customer, shipment);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        IdoraId.IsValid(customer.Id, "cus").ShouldBeTrue();
        shipment.Id.HasPrefix("shp").ShouldBeTrue();
    }

    [Fact]
    public void ApplyIdoraKeys_WithAnUndefinedFormat_ThrowsBeforeChangingAnything()
    {
        // Arrange
        ModelBuilder modelBuilder = new();
        modelBuilder.Entity<Customer>().HasKey(customer => customer.Id);
        IEntityPrefixRegistry registry = Registries.Create(options => options.RegisterEntity<Customer>("cus"));

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => modelBuilder.ApplyIdoraKeys(registry, (IdGenerationStrategy)9));
        modelBuilder.Model.FindEntityType(typeof(Customer))!.FindProperty(nameof(Customer.Id))!
            .ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    private IProperty Property<TEntity>(string name)
    {
        using ConventionDbContext context = _database.CreateContext();
        return context.Model.FindEntityType(typeof(TEntity))!.FindProperty(name)!;
    }

    public sealed class Customer
    {
        public string? Id { get; set; }
    }

    public sealed class Shipment
    {
        public IdoraId Id { get; set; }
    }

    /// <summary>Registered, but its key is an integer: the prefix must be for some other column.</summary>
    public sealed class Counter
    {
        public int Id { get; set; }
    }

    /// <summary>In the model, but not registered.</summary>
    public sealed class Memo
    {
        public string? Id { get; set; }
    }

    /// <summary>Registered, but with a composite key.</summary>
    public sealed class Parcel
    {
        public string? Id { get; set; }

        public int Sequence { get; set; }
    }

    public sealed class ConventionDbContext(DbContextOptions<ConventionDbContext> options, IEntityPrefixRegistry prefixes)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.UseIdoraIds();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>();
            modelBuilder.Entity<Shipment>();
            modelBuilder.Entity<Counter>();
            modelBuilder.Entity<Memo>();
            modelBuilder.Entity<Parcel>().HasKey(parcel => new { parcel.Id, parcel.Sequence });

            modelBuilder.ApplyIdoraKeys(prefixes);
        }
    }
}
