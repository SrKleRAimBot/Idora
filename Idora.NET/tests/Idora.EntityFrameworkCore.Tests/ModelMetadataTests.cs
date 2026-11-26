using Idora.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shouldly;
using Xunit;

namespace Idora.EntityFrameworkCore.Tests;

/// <summary>
/// Covers what the configuration methods put into the model: keys, value generation, lengths,
/// Unicode and conversions.
/// </summary>
public sealed class ModelMetadataTests : IDisposable
{
    private readonly SqliteDatabase<TestDbContext> _database = new(TestRegistrations.Configure);

    public void Dispose() => _database.Dispose();

    [Fact]
    public void HasIdoraKey_WithARegistry_SizesTheColumnToTheRegisteredPrefix()
    {
        // Act
        IProperty id = Property<User>(nameof(User.Id));

        // Assert: "usr", "_" and a 26-character ULID.
        id.IsPrimaryKey().ShouldBeTrue();
        id.GetMaxLength().ShouldBe(30);
        id.IsUnicode().ShouldBe(false);
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        id.GetValueGeneratorFactory().ShouldNotBeNull();
    }

    [Fact]
    public void HasIdoraKey_WithoutARegistry_SizesTheColumnForAnyPrefix()
    {
        // Act
        IProperty id = Property<Order>(nameof(Order.Id));

        // Assert
        id.IsPrimaryKey().ShouldBeTrue();
        id.GetMaxLength().ShouldBe(43);
        id.IsUnicode().ShouldBe(false);
        id.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
    }

    [Fact]
    public void HasIdoraKey_OnAnIdoraIdProperty_ConfiguresTheKeyAndTheConversion()
    {
        // Act
        IProperty id = Property<Invoice>(nameof(Invoice.Id));

        // Assert
        id.IsPrimaryKey().ShouldBeTrue();
        id.GetMaxLength().ShouldBe(30);
        id.IsUnicode().ShouldBe(false);
        id.GetValueConverter().ShouldBeOfType<IdoraIdValueConverter>();
        id.GetValueComparer().ShouldBeOfType<IdoraIdValueComparer>();
    }

    [Fact]
    public void UseIdoraIds_MapsPlainAndNullableIdoraIdPropertiesToo()
    {
        // Act
        IProperty raisedBy = Property<Invoice>(nameof(Invoice.RaisedBy));
        IProperty correctsId = Property<Invoice>(nameof(Invoice.CorrectsId));

        // Assert
        raisedBy.GetValueConverter().ShouldBeOfType<IdoraIdValueConverter>();
        raisedBy.GetMaxLength().ShouldBe(43);
        raisedBy.IsUnicode().ShouldBe(false);
        raisedBy.IsNullable.ShouldBeFalse();
        correctsId.GetValueConverter().ShouldBeOfType<IdoraIdValueConverter>();
        correctsId.IsNullable.ShouldBeTrue();
        correctsId.IsForeignKey().ShouldBeTrue();
    }

    [Fact]
    public void HasIdoraValueGenerator_OnANonKeyColumn_GeneratesWithoutMakingItAKey()
    {
        // Act
        IProperty reference = Property<Invoice>(nameof(Invoice.PublicReference));

        // Assert
        reference.IsPrimaryKey().ShouldBeFalse();
        reference.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        reference.GetValueGeneratorFactory().ShouldNotBeNull();
    }

    [Fact]
    public void HasIdoraValueGenerator_OnAPropertyOfAnotherType_Throws()
    {
        // Arrange
        ModelBuilder modelBuilder = new();
        PropertyBuilder<decimal> total = modelBuilder.Entity<Order>().Property(order => order.Total);

        // Act
        ArgumentException exception = Should.Throw<ArgumentException>(() => total.HasIdoraValueGenerator());

        // Assert
        exception.ParamName.ShouldBe("propertyBuilder");
        exception.Message.ShouldContain("'Order.Total' is of type 'Decimal'");
    }

    [Fact]
    public void HasIdoraKey_WithARegistryThatLacksTheEntity_FailsWhileBuildingTheModel()
    {
        // Arrange
        ModelBuilder modelBuilder = new();
        IEntityPrefixRegistry registry = Registries.Create(options => options.RegisterEntity<User>("usr"));

        // Act & Assert
        Should.Throw<EntityPrefixNotRegisteredException>(() => modelBuilder.Entity<Order>().HasIdoraKey(o => o.Id, registry))
            .EntityType.ShouldBe(typeof(Order));
    }

    [Fact]
    public void HasIdoraKey_ForUuidV7_AllowsForTheLongerValue()
    {
        // Arrange
        ModelBuilder modelBuilder = new();
        IEntityPrefixRegistry registry = Registries.Create(options => options.RegisterEntity<User>("usr"));

        // Act
        modelBuilder.Entity<Order>().HasIdoraKey(order => order.Id, IdGenerationStrategy.UuidV7);
        modelBuilder.Entity<User>().HasIdoraKey(user => user.Id, registry, IdGenerationStrategy.UuidV7);

        // Assert
        modelBuilder.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!.GetMaxLength().ShouldBe(49);
        modelBuilder.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.Id))!.GetMaxLength().ShouldBe(36);
    }

    [Fact]
    public void ConfigurationMethods_WithNullArguments_ThrowArgumentNullException()
    {
        // Arrange
        ModelBuilder modelBuilder = new();
        EntityTypeBuilder<Order> order = modelBuilder.Entity<Order>();
        IEntityPrefixRegistry registry = Registries.Create(_ => { });

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => order.HasIdoraKey((System.Linq.Expressions.Expression<Func<Order, string?>>)null!));
        Should.Throw<ArgumentNullException>(() => order.HasIdoraKey(o => o.Id, (IEntityPrefixRegistry)null!));
        Should.Throw<ArgumentNullException>(() => ((EntityTypeBuilder<Order>)null!).HasIdoraKey(o => o.Id));
        Should.Throw<ArgumentNullException>(() => ((PropertyBuilder<string>)null!).HasIdoraValueGenerator());
        Should.Throw<ArgumentNullException>(() => ((ModelBuilder)null!).ApplyIdoraKeys(registry));
        Should.Throw<ArgumentNullException>(() => modelBuilder.ApplyIdoraKeys(null!));
        Should.Throw<ArgumentNullException>(() => ((ModelConfigurationBuilder)null!).UseIdoraIds());
    }

    [Fact]
    public void ValueConverter_MapsTheEmptyIdentifierToTheEmptyStringAndBack()
    {
        // Arrange
        IdoraIdValueConverter converter = new();

        // Act & Assert
        converter.ConvertToProvider(default(IdoraId)).ShouldBe(string.Empty);
        converter.ConvertFromProvider(string.Empty).ShouldBe(default(IdoraId));
        converter.ConvertToProvider(IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s")).ShouldBe("usr_01k226e0g0008j4ct4ank7f24s");
        converter.ConvertFromProvider("usr_01k226e0g0008j4ct4ank7f24s").ShouldBe(IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s"));
        converter.MappingHints!.Size.ShouldBe(49);
        converter.MappingHints.IsUnicode.ShouldBe(false);
    }

    [Fact]
    public void ValueComparer_ComparesByText()
    {
        // Arrange
        IdoraIdValueComparer comparer = new();
        IdoraId id = IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s");

        // Act & Assert
        comparer.Equals(id, IdoraId.Parse(new string(id.ToString().AsSpan()))).ShouldBeTrue();
        comparer.Equals(id, default).ShouldBeFalse();
        comparer.GetHashCode(id).ShouldBe(id.GetHashCode());
        comparer.Snapshot(id).ShouldBe(id);
    }

    [Fact]
    public void ValueGenerators_AreNotTemporary()
    {
        // Act & Assert
        new IdoraValueGenerator().GeneratesTemporaryValues.ShouldBeFalse();
        new IdoraIdValueGenerator().GeneratesTemporaryValues.ShouldBeFalse();
    }

    private IProperty Property<TEntity>(string name)
    {
        using TestDbContext context = _database.CreateContext();
        return context.Model.FindEntityType(typeof(TEntity))!.FindProperty(name)!;
    }
}
