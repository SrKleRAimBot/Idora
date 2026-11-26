using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Idora;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Filters a query by when its rows' identifiers were minted, using the identifiers alone.
/// </summary>
/// <remarks>
/// <para>
/// An identifier's value starts with its timestamp, so every identifier minted for one prefix during
/// a span of time falls in one contiguous range of strings, between
/// <see cref="IdoraId.LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/> and
/// <see cref="IdoraId.UpperBound(string, DateTimeOffset, IdGenerationStrategy)"/>. These methods add
/// that range as two string comparisons on the key column &#8212; <c>Id &gt;= @lower AND Id &lt;=
/// @upper</c> &#8212; which the database answers from the primary key's index, with no
/// <c>CreatedAt</c> column and no secondary index.
/// </para>
/// <para>
/// For a <see cref="string"/> column the comparisons are written as
/// <see cref="string.Compare(string, string)"/>, the form EF Core translates to SQL comparison
/// operators; for an <see cref="IdoraId"/> column mapped by <c>UseIdoraIds</c> they use
/// <see cref="IdoraId"/>'s own operators, which EF Core translates through the value converter. Either
/// way the bounds are passed as parameters. In SQL the column's collation decides the comparison.
/// Identifiers are lowercase ASCII after a prefix that is the same on every row, so any collation
/// orders them correctly, case-sensitive or not.
/// </para>
/// <para>
/// The range is meaningful only for identifiers of one prefix and one format, which is the shape of
/// a key column holding one entity type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var lastWeek = await db.Orders
///     .WhereCreatedBetween(order => order.Id, "ord", DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow)
///     .ToListAsync();
/// </code>
/// </example>
public static class IdoraQueryableExtensions
{
    /// <summary><see cref="string.Compare(string, string)"/>, the comparison EF Core translates.</summary>
    private static readonly MethodInfo StringCompare =
        typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    /// <summary>
    /// Keeps the rows whose identifier, with <paramref name="prefix"/>, was minted between
    /// <paramref name="from"/> and <paramref name="to"/>, both inclusive, at millisecond precision.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being queried.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="idSelector">The identifier column, for example <c>order =&gt; order.Id</c>.</param>
    /// <param name="prefix">The prefix the column's identifiers carry.</param>
    /// <param name="from">The earliest instant to include. Anything below a millisecond is ignored.</param>
    /// <param name="to">
    /// The latest instant to include. Anything below a millisecond is ignored, and the whole of that
    /// millisecond is included.
    /// </param>
    /// <param name="format">The value format of the column's identifiers. Defaults to ULID.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/>, <paramref name="idSelector"/> or <paramref name="prefix"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> or <paramref name="to"/> is before the Unix epoch, or
    /// <paramref name="format"/> is not a defined value.
    /// </exception>
    public static IQueryable<TEntity> WhereCreatedBetween<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, string?>> idSelector,
        string prefix,
        DateTimeOffset from,
        DateTimeOffset to,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        source.WhereIdBetween(
            idSelector,
            IdoraId.LowerBound(prefix, from, format),
            IdoraId.UpperBound(prefix, to, format));

    /// <summary>
    /// Keeps the rows whose identifier lies between <paramref name="lower"/> and
    /// <paramref name="upper"/> as strings, both inclusive.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being queried.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="idSelector">The identifier column, for example <c>order =&gt; order.Id</c>.</param>
    /// <param name="lower">The lowest identifier to include, typically an <see cref="IdoraId.LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/>.</param>
    /// <param name="upper">The highest identifier to include, typically an <see cref="IdoraId.UpperBound(string, DateTimeOffset, IdGenerationStrategy)"/>.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="idSelector"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lower"/> or <paramref name="upper"/> is <c>default(IdoraId)</c>.
    /// </exception>
    /// <remarks>
    /// The building block under <c>WhereCreatedBetween</c>, for a range computed elsewhere
    /// &#8212; resuming a cursor from the last identifier a client saw, say.
    /// </remarks>
    public static IQueryable<TEntity> WhereIdBetween<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, string?>> idSelector,
        IdoraId lower,
        IdoraId upper)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(idSelector);
        ThrowIfEmpty(lower, upper);

        Expression column = idSelector.Body;
        Expression zero = Expression.Constant(0);

        // id >= lower && id <= upper, as string.Compare(id, bound) against zero.
        Expression predicate = Expression.AndAlso(
            Expression.GreaterThanOrEqual(Expression.Call(StringCompare, column, TextParameter(lower)), zero),
            Expression.LessThanOrEqual(Expression.Call(StringCompare, column, TextParameter(upper)), zero));

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(predicate, idSelector.Parameters));
    }

    /// <summary>
    /// Keeps the rows whose <see cref="IdoraId"/> identifier, with <paramref name="prefix"/>, was
    /// minted between <paramref name="from"/> and <paramref name="to"/>, both inclusive, at
    /// millisecond precision.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being queried.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="idSelector">The identifier column, for example <c>invoice =&gt; invoice.Id</c>.</param>
    /// <param name="prefix">The prefix the column's identifiers carry.</param>
    /// <param name="from">The earliest instant to include. Anything below a millisecond is ignored.</param>
    /// <param name="to">The latest instant to include, the whole of its millisecond included.</param>
    /// <param name="format">The value format of the column's identifiers. Defaults to ULID.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/>, <paramref name="idSelector"/> or <paramref name="prefix"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> or <paramref name="to"/> is before the Unix epoch, or
    /// <paramref name="format"/> is not a defined value.
    /// </exception>
    /// <remarks>The column must be mapped to a string column, as <c>UseIdoraIds</c> does.</remarks>
    public static IQueryable<TEntity> WhereCreatedBetween<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, IdoraId>> idSelector,
        string prefix,
        DateTimeOffset from,
        DateTimeOffset to,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        source.WhereIdBetween(
            idSelector,
            IdoraId.LowerBound(prefix, from, format),
            IdoraId.UpperBound(prefix, to, format));

    /// <summary>
    /// Keeps the rows whose <see cref="IdoraId"/> identifier lies between <paramref name="lower"/> and
    /// <paramref name="upper"/>, both inclusive.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being queried.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="idSelector">The identifier column.</param>
    /// <param name="lower">The lowest identifier to include.</param>
    /// <param name="upper">The highest identifier to include.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="idSelector"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lower"/> or <paramref name="upper"/> is <c>default(IdoraId)</c>.
    /// </exception>
    /// <remarks>
    /// In the database this compares the stored text; in memory it uses <see cref="IdoraId"/>'s natural
    /// order. The two agree for identifiers of one prefix and one format, which is what a range over
    /// one column holds.
    /// </remarks>
    public static IQueryable<TEntity> WhereIdBetween<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, IdoraId>> idSelector,
        IdoraId lower,
        IdoraId upper)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(idSelector);
        ThrowIfEmpty(lower, upper);

        Expression column = idSelector.Body;
        Expression predicate = Expression.AndAlso(
            Expression.GreaterThanOrEqual(column, Parameter(lower)),
            Expression.LessThanOrEqual(column, Parameter(upper)));

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(predicate, idSelector.Parameters));
    }

    private static void ThrowIfEmpty(IdoraId lower, IdoraId upper)
    {
        if (lower.IsEmpty)
        {
            throw new ArgumentException("The lower bound is an empty IdoraId.", nameof(lower));
        }

        if (upper.IsEmpty)
        {
            throw new ArgumentException("The upper bound is an empty IdoraId.", nameof(upper));
        }
    }

    /// <summary>
    /// Wraps a bound's text so that EF Core sends it as a query parameter rather than inlining it as a
    /// literal, which would compile a new query plan for every range.
    /// </summary>
    /// <remarks>
    /// A field read on a captured object is exactly the shape a C# closure produces, which is the
    /// shape EF Core recognises and parameterises.
    /// </remarks>
    private static MemberExpression TextParameter(IdoraId bound) =>
        Expression.Field(Expression.Constant(new StrongBox<string>(bound.ToString())), nameof(StrongBox<string>.Value));

    /// <summary>Wraps a bound as an <see cref="IdoraId"/> parameter, converted by the column's converter.</summary>
    private static MemberExpression Parameter(IdoraId bound) =>
        Expression.Field(Expression.Constant(new StrongBox<IdoraId>(bound)), nameof(StrongBox<IdoraId>.Value));
}
