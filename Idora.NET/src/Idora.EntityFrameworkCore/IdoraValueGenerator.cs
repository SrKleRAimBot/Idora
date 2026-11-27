using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Idora.EntityFrameworkCore;

/// <summary>
/// Fills in a <see cref="string"/> key with a new identifier when an entity is added, using the
/// prefix registered for the entity's type.
/// </summary>
/// <remarks>
/// <para>
/// Configure it with <c>HasIdoraKey</c> or <c>HasIdoraValueGenerator</c> rather than directly. It
/// runs when an entity is added to the context with its key still <see langword="null"/>; a key you
/// assign yourself is left alone.
/// </para>
/// <para>
/// The <see cref="IIdoraGenerator"/> and <see cref="IEntityPrefixRegistry"/> come from the
/// application's container, which EF Core can see when the context is registered with
/// <c>AddDbContext</c>. A context with no route to them fails on the first insert with a message
/// saying how to give it one.
/// </para>
/// <para>
/// The prefix is the one registered for the entity's CLR type or, in an inheritance hierarchy, for
/// the nearest mapped base type that has one. An entity type with no registered prefix anywhere in
/// its hierarchy fails with <see cref="EntityPrefixNotRegisteredException"/>; the generator never
/// invents a prefix.
/// </para>
/// <para>
/// The values are permanent, not temporary: an identifier does not need the database to be unique,
/// so the key is final from the moment the entity is added, and can be used in foreign keys and
/// logged before <c>SaveChanges</c> runs.
/// </para>
/// </remarks>
public sealed class IdoraValueGenerator : ValueGenerator<string>
{
    /// <summary>Always <see langword="false"/>: generated identifiers are final, not placeholders.</summary>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The context cannot reach an <see cref="IIdoraGenerator"/> and <see cref="IEntityPrefixRegistry"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// The entity type has no registered prefix.
    /// </exception>
    public override string Next(EntityEntry entry) => IdoraKeyGeneration.Generate(entry);
}
