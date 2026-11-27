using Idora.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the <c>idora</c> route constraint.
/// </summary>
/// <remarks>
/// This lives in the <c>Microsoft.Extensions.DependencyInjection</c> namespace, the convention for
/// registration extensions, so it is available in <c>Program.cs</c> without an extra
/// <c>using</c> directive.
/// </remarks>
public static class IdoraRoutingServiceCollectionExtensions
{
    /// <summary>
    /// Makes <c>{id:idora}</c> and <c>{id:idora(prefix)}</c> available in route templates, for
    /// minimal APIs, controllers and Razor Pages alike.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Independent of <c>AddIdora</c>: the constraint checks the format and, if asked, an explicit
    /// prefix, so it needs no registered entities. Calling it more than once is harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddIdoraRouteConstraint();
    ///
    /// app.MapGet("/orders/{id:idora(ord)}", (IdoraId id) => ...);
    /// </code>
    /// </example>
    public static IServiceCollection AddIdoraRouteConstraint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // SetParameterPolicy rather than ConstraintMap: it keeps the constraint's constructors visible
        // to the trimmer, so the constraint still resolves in an ahead-of-time compiled application.
        return services.AddRouting(static options =>
            options.SetParameterPolicy<IdoraRouteConstraint>(IdoraRouteConstraint.Name));
    }
}
