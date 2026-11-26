using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Idora.EntityFrameworkCore;

/// <summary>
/// Compares <see cref="IdoraId"/> values for change tracking: by their text, ordinally.
/// </summary>
/// <remarks>
/// <see cref="IdoraId"/> is an immutable value, so a snapshot is the value itself and nothing has to
/// be copied. Applied alongside <see cref="IdoraIdValueConverter"/> by <c>UseIdoraIds</c>.
/// </remarks>
public sealed class IdoraIdValueComparer : ValueComparer<IdoraId>
{
    /// <summary>Creates the comparer.</summary>
    public IdoraIdValueComparer()
        : base(
            (left, right) => left == right,
            id => id.GetHashCode(),
            id => id)
    {
    }
}
