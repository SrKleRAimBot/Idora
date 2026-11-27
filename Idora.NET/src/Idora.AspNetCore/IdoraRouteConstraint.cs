using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idora.AspNetCore;

/// <summary>
/// Matches a route value only when it is a well-formed identifier, optionally with a particular
/// prefix: <c>{id:idora}</c> or <c>{id:idora(usr)}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Register it with <c>services.AddIdoraRouteConstraint()</c>, which maps it to the name
/// <see cref="Name"/>.
/// </para>
/// <para>
/// A route constraint decides whether a route <em>matches</em>, not whether a request is valid, so a
/// value that fails it makes the route not match &#8212; usually a 404, or a different route that does
/// match. That is the right outcome for <c>/users/ord_01k2…</c>, which names no user, and it lets two
/// routes that differ only in the kind of identifier sit side by side. To answer a malformed
/// identifier with a 400 instead, bind it as an <see cref="IdoraId"/> without the constraint and let
/// binding reject it.
/// </para>
/// <para>
/// The check is <see cref="IdoraId.IsValid(string, string)"/>: strict, exact about the prefix's case,
/// and free of allocation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddIdoraRouteConstraint();
///
/// app.MapGet("/users/{id:idora(usr)}", (IdoraId id) => ...);
/// app.MapGet("/things/{id:idora}", (IdoraId id) => ...);
/// </code>
/// </example>
public sealed class IdoraRouteConstraint : IRouteConstraint
{
    /// <summary>The name the constraint is registered under: <c>idora</c>.</summary>
    public const string Name = "idora";

    /// <summary>Creates a constraint that accepts an identifier with any prefix: <c>{id:idora}</c>.</summary>
    public IdoraRouteConstraint()
    {
    }

    /// <summary>
    /// Creates a constraint that accepts only identifiers with <paramref name="prefix"/>:
    /// <c>{id:idora(usr)}</c>.
    /// </summary>
    /// <param name="prefix">The prefix the identifier must have, compared exactly, case included.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="prefix"/> could never appear in an identifier. A route template with such a
    /// prefix fails as soon as routing builds its tables, on the first request, rather than silently
    /// matching nothing.
    /// </exception>
    public IdoraRouteConstraint(string prefix)
    {
        // Reuses the generator's rules, and its message, by asking for a bound: the cheapest public
        // operation that validates a prefix and does nothing else of consequence.
        _ = IdoraId.GetLength(prefix, IdGenerationStrategy.Ulid);
        Prefix = prefix;
    }

    /// <summary>The prefix an identifier must have, or <see langword="null"/> if any prefix will do.</summary>
    public string? Prefix { get; }

    /// <inheritdoc />
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(routeKey, out object? value) || value is null)
        {
            return false;
        }

        string? text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

        return Prefix is null ? IdoraId.IsValid(text) : IdoraId.IsValid(text, Prefix);
    }
}
