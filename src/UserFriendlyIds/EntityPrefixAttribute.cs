namespace UserFriendlyIds;

/// <summary>
/// Declares the prefix an entity type's identifiers should carry, on the type itself.
/// </summary>
/// <remarks>
/// <para>
/// This is the alternative to registering the same mapping fluently with
/// <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/>. It puts the prefix next to
/// the type it describes rather than in a wall of registrations in <c>Program.cs</c>, which reads
/// better once a domain has more than a handful of entities.
/// </para>
/// <para>
/// The attribute on its own does nothing. Prefixes are collected at start-up by
/// <see cref="UserFriendlyIdOptions.RegisterEntitiesFromAssembly"/> and its companions, which you
/// call with the assemblies your entities live in; the results go into the same frozen lookup the
/// fluent registrations do. Nothing is reflected over afterwards, so identifier generation costs
/// exactly what it did before.
/// </para>
/// <para>
/// The two styles mix freely, and a prefix still cannot be claimed twice: a clash between two
/// attributes, or between an attribute and a fluent registration, fails at start-up with a
/// <see cref="DuplicateEntityPrefixException"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [EntityPrefix("usr")]
/// public sealed class User
/// {
///     public required string Id { get; init; }
/// }
///
/// // At start-up:
/// services.AddUserFriendlyIds(options => options.RegisterEntitiesFromAssemblyContaining&lt;User&gt;());
///
/// // Then:
/// ids.Generate&lt;User&gt;();   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
    Inherited = false,
    AllowMultiple = false)]
public sealed class EntityPrefixAttribute : Attribute
{
    /// <summary>
    /// Declares the prefix this entity type's identifiers should carry.
    /// </summary>
    /// <param name="prefix">
    /// The prefix, 1 to 16 ASCII letters or digits. Lowercase reads best, but the prefix is used
    /// exactly as you write it.
    /// </param>
    /// <remarks>
    /// The prefix is deliberately not validated here. An attribute constructor runs only when
    /// something reflects over the type, so throwing from it would surface as a
    /// <see cref="System.Reflection.TargetInvocationException"/> wrapping the real problem. The
    /// prefix is instead validated when the assembly is scanned &#8212; still at start-up, and with
    /// a message that names the type carrying the mistake.
    /// </remarks>
    public EntityPrefixAttribute(string prefix) => Prefix = prefix;

    /// <summary>The prefix this entity type's identifiers should carry.</summary>
    public string Prefix { get; }
}
