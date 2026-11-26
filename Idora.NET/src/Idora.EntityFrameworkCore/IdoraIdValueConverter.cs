using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Idora.EntityFrameworkCore;

/// <summary>
/// Stores an <see cref="IdoraId"/> as its text in a string column.
/// </summary>
/// <remarks>
/// <para>
/// Applied to every <see cref="IdoraId"/> property by <c>UseIdoraIds</c>; reach for it directly only
/// to configure a single property by hand.
/// </para>
/// <para>
/// Reading is strict, as <see cref="IdoraId.Parse(string)"/> is: a stored value that is not a valid
/// identifier fails the query with an <see cref="InvalidIdException"/> rather than materialising
/// something half-formed. The one exception is the empty string, which reads as
/// <c>default(IdoraId)</c> &#8212; the value an unassigned non-nullable property is written as.
/// Declare the property <c>IdoraId?</c> to store a real <c>NULL</c> instead.
/// </para>
/// </remarks>
public sealed class IdoraIdValueConverter : ValueConverter<IdoraId, string>
{
    /// <summary>
    /// Creates the converter, hinting at a non-Unicode column wide enough for any identifier.
    /// </summary>
    public IdoraIdValueConverter()
        : this(new ConverterMappingHints(size: IdoraId.GetMaxLength(IdGenerationStrategy.UuidV7), unicode: false))
    {
    }

    /// <summary>Creates the converter with the column facets to suggest to the provider.</summary>
    /// <param name="mappingHints">
    /// Hints for the column; facets configured on the property itself take precedence.
    /// </param>
    public IdoraIdValueConverter(ConverterMappingHints? mappingHints)
        : base(
            id => id.ToString(),
            text => text.Length == 0 ? default : IdoraId.Parse(text),
            mappingHints)
    {
    }
}
