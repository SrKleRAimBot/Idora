using Idora;
using Idora.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Maps every <see cref="IdoraId"/> property in a model to a string column.
/// </summary>
public static class IdoraModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Stores every <see cref="IdoraId"/> and <c>IdoraId?</c> property as its text, in a non-Unicode
    /// column, with a value comparer for change tracking.
    /// </summary>
    /// <param name="configurationBuilder">The model's pre-convention configuration.</param>
    /// <param name="format">
    /// The value format the application generates, which decides the default column length: 43 for
    /// ULIDs, 49 for UUIDv7s. Defaults to ULID.
    /// </param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configurationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    /// <remarks>
    /// <para>
    /// Call it from <c>ConfigureConventions</c>. Without it EF Core does not know how to store an
    /// <see cref="IdoraId"/> and refuses to build the model. With it, an <see cref="IdoraId"/> works
    /// anywhere a <see cref="string"/> would: as a key, a foreign key, an index, or a plain column,
    /// and in equality comparisons in queries. A length configured on an individual property, such as
    /// the one <c>HasIdoraKey</c> sets, takes precedence over the default here.
    /// </para>
    /// <para>
    /// Rows are read strictly: a stored value that is not a valid identifier fails the query. See
    /// <see cref="IdoraIdValueConverter"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    ///     configurationBuilder.UseIdoraIds();
    /// }
    /// </code>
    /// </example>
    public static ModelConfigurationBuilder UseIdoraIds(
        this ModelConfigurationBuilder configurationBuilder,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<IdoraId>()
            .HaveConversion<IdoraIdValueConverter, IdoraIdValueComparer>()
            .HaveMaxLength(IdoraId.GetMaxLength(format))
            .AreUnicode(false);

        return configurationBuilder;
    }
}
