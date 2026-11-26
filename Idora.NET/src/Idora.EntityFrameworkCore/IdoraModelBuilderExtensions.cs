using System.Globalization;
using System.Linq.Expressions;
using Idora;
using Idora.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Configures Idora identifiers as Entity Framework Core keys: generated on insert, sized exactly,
/// and stored in a non-Unicode column.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the <c>Microsoft.EntityFrameworkCore</c> namespace, the convention for model
/// building extensions, so it is available in <c>OnModelCreating</c> without an extra
/// <c>using</c> directive.
/// </para>
/// <para>
/// A generated key needs the application's <see cref="IIdoraGenerator"/> and
/// <see cref="IEntityPrefixRegistry"/> at run time. Both are registered by <c>AddIdora</c>, and EF
/// Core can see them when the context is registered with <c>AddDbContext</c> in the same container.
/// See <see cref="IdoraValueGenerator"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     modelBuilder.Entity&lt;User&gt;().HasIdoraKey(user => user.Id);
/// }
/// </code>
/// </example>
public static class IdoraModelBuilderExtensions
{
    /// <summary>
    /// Makes <paramref name="keyExpression"/> the primary key, generated on insert with the prefix
    /// registered for <typeparamref name="TEntity"/>, in a column wide enough for any prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entityTypeBuilder">The entity type being configured.</param>
    /// <param name="keyExpression">The <see cref="string"/> key property, for example <c>user =&gt; user.Id</c>.</param>
    /// <param name="format">
    /// The value format the application generates, which decides the column's length. Defaults to
    /// ULID, as <see cref="IdoraOptions.Strategy"/> does.
    /// </param>
    /// <returns>The key, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityTypeBuilder"/> or <paramref name="keyExpression"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// In one call: the property becomes the primary key, gets an <see cref="IdoraValueGenerator"/>
    /// and <see cref="ValueGenerated.OnAdd"/>, is marked non-Unicode (every character of an identifier
    /// is ASCII), and is given a maximum length of <see cref="IdoraId.GetMaxLength"/> &#8212; 43 for
    /// ULIDs, 49 for UUIDv7s.
    /// </para>
    /// <para>
    /// For a column sized to exactly the registered prefix &#8212; 30 rather than 43 for <c>usr</c>
    /// &#8212; use the overload that takes an <see cref="IEntityPrefixRegistry"/>.
    /// </para>
    /// </remarks>
    public static KeyBuilder<TEntity> HasIdoraKey<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, string?>> keyExpression,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(keyExpression);

        return ConfigureKey(entityTypeBuilder, entityTypeBuilder.Property(keyExpression), IdoraId.GetMaxLength(format));
    }

    /// <summary>
    /// Makes <paramref name="keyExpression"/> the primary key, generated on insert with the prefix
    /// registered for <typeparamref name="TEntity"/>, in a column sized to exactly that prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type, registered in <paramref name="registry"/>.</typeparam>
    /// <param name="entityTypeBuilder">The entity type being configured.</param>
    /// <param name="keyExpression">The <see cref="string"/> key property.</param>
    /// <param name="registry">
    /// The application's prefixes. Inject it into the context's constructor to have it here.
    /// </param>
    /// <param name="format">The value format the application generates. Defaults to ULID.</param>
    /// <returns>The key, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityTypeBuilder"/>, <paramref name="keyExpression"/> or
    /// <paramref name="registry"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix. This surfaces when the model is built,
    /// rather than on the first insert.
    /// </exception>
    /// <remarks>
    /// As the overload without a registry, except that the maximum length is
    /// <see cref="IdoraId.GetLength"/> for the registered prefix: 30 for <c>usr</c> with ULIDs. Every
    /// identifier for the entity is exactly that long. If derived types in an inheritance hierarchy
    /// are registered with prefixes of their own, they share this column, so use the overload without
    /// a registry unless their prefixes are no longer than the root's.
    /// </remarks>
    public static KeyBuilder<TEntity> HasIdoraKey<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, string?>> keyExpression,
        IEntityPrefixRegistry registry,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(keyExpression);
        ArgumentNullException.ThrowIfNull(registry);

        return ConfigureKey(
            entityTypeBuilder,
            entityTypeBuilder.Property(keyExpression),
            IdoraId.GetLength(registry.GetPrefix<TEntity>(), format));
    }

    /// <summary>
    /// Makes an <see cref="IdoraId"/> property the primary key, generated on insert with the prefix
    /// registered for <typeparamref name="TEntity"/>, in a column wide enough for any prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entityTypeBuilder">The entity type being configured.</param>
    /// <param name="keyExpression">The <see cref="IdoraId"/> key property.</param>
    /// <param name="format">The value format the application generates. Defaults to ULID.</param>
    /// <returns>The key, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityTypeBuilder"/> or <paramref name="keyExpression"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// As the <see cref="string"/> overload, with an <see cref="IdoraIdValueGenerator"/>. The property
    /// also needs mapping to a string column, which
    /// <see cref="IdoraModelConfigurationBuilderExtensions.UseIdoraIds"/> does for the whole model; on
    /// its own this configures the key, not the conversion.
    /// </remarks>
    public static KeyBuilder<TEntity> HasIdoraKey<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, IdoraId>> keyExpression,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(keyExpression);

        return ConfigureKey(entityTypeBuilder, entityTypeBuilder.Property(keyExpression), IdoraId.GetMaxLength(format));
    }

    /// <summary>
    /// Makes an <see cref="IdoraId"/> property the primary key, generated on insert with the prefix
    /// registered for <typeparamref name="TEntity"/>, in a column sized to exactly that prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type, registered in <paramref name="registry"/>.</typeparam>
    /// <param name="entityTypeBuilder">The entity type being configured.</param>
    /// <param name="keyExpression">The <see cref="IdoraId"/> key property.</param>
    /// <param name="registry">The application's prefixes.</param>
    /// <param name="format">The value format the application generates. Defaults to ULID.</param>
    /// <returns>The key, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityTypeBuilder"/>, <paramref name="keyExpression"/> or
    /// <paramref name="registry"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix.
    /// </exception>
    public static KeyBuilder<TEntity> HasIdoraKey<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, IdoraId>> keyExpression,
        IEntityPrefixRegistry registry,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(keyExpression);
        ArgumentNullException.ThrowIfNull(registry);

        return ConfigureKey(
            entityTypeBuilder,
            entityTypeBuilder.Property(keyExpression),
            IdoraId.GetLength(registry.GetPrefix<TEntity>(), format));
    }

    /// <summary>
    /// Gives a <see cref="string"/> or <see cref="IdoraId"/> property a new identifier when its
    /// entity is added, using the prefix registered for the entity's type.
    /// </summary>
    /// <typeparam name="TProperty">The property's type: <see cref="string"/> or <see cref="IdoraId"/>.</typeparam>
    /// <param name="propertyBuilder">The property being configured.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="propertyBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The property is neither a <see cref="string"/> nor an <see cref="IdoraId"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Sets the value generator and <see cref="ValueGenerated.OnAdd"/>, and nothing else: no key, no
    /// length, no Unicode setting. Use it for a generated column that is not the primary key, such as
    /// an alternate public identifier; for a key, <c>HasIdoraKey</c> does everything at once.
    /// </para>
    /// <para>
    /// Generic over the property type, rather than overloaded for each, so that a property declared
    /// <c>string?</c> configures without a nullability warning.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Invoice&gt;()
    ///     .Property(invoice => invoice.PublicId)
    ///     .HasIdoraValueGenerator()
    ///     .HasMaxLength(30)
    ///     .IsUnicode(false);
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> HasIdoraValueGenerator<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        Type valueGenerator = ValueGeneratorFor(propertyBuilder.Metadata.ClrType)
            ?? throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "An Idora identifier can only be generated for a string or IdoraId property, but "
                    + "'{0}.{1}' is of type '{2}'.",
                    propertyBuilder.Metadata.DeclaringType.DisplayName(),
                    propertyBuilder.Metadata.Name,
                    propertyBuilder.Metadata.ClrType.Name),
                nameof(propertyBuilder));

        propertyBuilder.HasValueGenerator(valueGenerator);
        propertyBuilder.ValueGeneratedOnAdd();
        return propertyBuilder;
    }

    /// <summary>
    /// Configures the primary key of every entity type in the model that has a registered prefix, as
    /// <c>HasIdoraKey</c> would, sized to exactly that prefix.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="registry">
    /// The application's prefixes. Inject it into the context's constructor to have it here.
    /// </param>
    /// <param name="format">The value format the application generates. Defaults to ULID.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="modelBuilder"/> or <paramref name="registry"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call it at the end of <c>OnModelCreating</c>, once every entity type is in the model and its
    /// key has been found &#8212; by convention for a property called <c>Id</c>, or by your own
    /// <c>HasKey</c>.
    /// </para>
    /// <para>
    /// An entity type is configured when it is the root of its hierarchy, its CLR type is registered,
    /// and its primary key is a single <see cref="string"/> or <see cref="IdoraId"/> property. Anything
    /// else is left exactly as it was: registered types that are not in the model, keyless and
    /// composite-key types, and types whose key is an <see cref="int"/> or a <see cref="Guid"/> that
    /// happen to be registered for some other column.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class AppDbContext(DbContextOptions&lt;AppDbContext&gt; options, IEntityPrefixRegistry prefixes)
    ///     : DbContext(options)
    /// {
    ///     protected override void OnModelCreating(ModelBuilder modelBuilder)
    ///     {
    ///         modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    ///         modelBuilder.ApplyIdoraKeys(prefixes);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static ModelBuilder ApplyIdoraKeys(
        this ModelBuilder modelBuilder,
        IEntityPrefixRegistry registry,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(registry);

        // Checked before anything is touched, so an undefined value cannot leave the model half done.
        IdoraId.GetMaxLength(format);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is not null
                || entityType.HasSharedClrType
                || !registry.TryGetPrefix(entityType.ClrType, out string? prefix)
                || entityType.FindPrimaryKey() is not { Properties: [IMutableProperty keyProperty] }
                || ValueGeneratorFor(keyProperty.ClrType) is not { } valueGenerator)
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(keyProperty.Name)
                .HasValueGenerator(valueGenerator)
                .ValueGeneratedOnAdd()
                .HasMaxLength(IdoraId.GetLength(prefix, format))
                .IsUnicode(false);
        }

        return modelBuilder;
    }

    private static KeyBuilder<TEntity> ConfigureKey<TEntity, TProperty>(
        EntityTypeBuilder<TEntity> entityTypeBuilder,
        PropertyBuilder<TProperty> propertyBuilder,
        int maxLength)
        where TEntity : class
    {
        propertyBuilder
            .HasIdoraValueGenerator()
            .HasMaxLength(maxLength)
            .IsUnicode(false);

        return entityTypeBuilder.HasKey(propertyBuilder.Metadata.Name);
    }

    /// <summary>
    /// Chooses the value generator matching a property's CLR type, or <see langword="null"/> if it is
    /// not one an identifier can be generated for.
    /// </summary>
    private static Type? ValueGeneratorFor(Type propertyType) =>
        propertyType == typeof(string) ? typeof(IdoraValueGenerator)
        : propertyType == typeof(IdoraId) ? typeof(IdoraIdValueGenerator)
        : null;
}
