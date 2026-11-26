using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Idora.EntityFrameworkCore;

/// <summary>
/// Fills in an <see cref="IdoraId"/> key with a new identifier when an entity is added, using the
/// prefix registered for the entity's type.
/// </summary>
/// <remarks>
/// The <see cref="IdoraId"/> counterpart of <see cref="IdoraValueGenerator"/>, and identical in every
/// other respect. It runs when the key is still <c>default(IdoraId)</c>.
/// </remarks>
public sealed class IdoraIdValueGenerator : ValueGenerator<IdoraId>
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
    public override IdoraId Next(EntityEntry entry) => IdoraId.Parse(IdoraKeyGeneration.Generate(entry));
}
