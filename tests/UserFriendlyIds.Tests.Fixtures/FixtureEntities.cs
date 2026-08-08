namespace UserFriendlyIds.Tests.Fixtures;

/// <summary>
/// A well-formed set of attributed entity types, and enough unattributed ones to prove the scan is
/// selective. Every prefix here starts with <c>fx</c> so a stray registration from elsewhere would
/// be obvious in a failing assertion.
/// </summary>
/// <remarks>
/// Deliberately no clashing or malformed prefixes: those cases are exercised through the scanner's
/// type-list seam in <c>UserFriendlyIds.Tests.Support.AttributedTestEntities</c>, so that scanning
/// this assembly always succeeds and the happy-path tests can assert an exact set.
/// </remarks>
[EntityPrefix("fxusr")]
public sealed class FixtureUser;

[EntityPrefix("fxord")]
public sealed class FixtureOrder;

/// <summary>A value type, to prove structs are discovered as well as classes.</summary>
[EntityPrefix("fxpay")]
public readonly struct FixturePayment;

/// <summary>Not public, to prove the scan does not stop at the assembly boundary's public surface.</summary>
[EntityPrefix("fxint")]
internal sealed class FixtureInternalEntity;

/// <summary>Carries no attribute, and must not be registered.</summary>
public sealed class FixtureUnattributed;

/// <summary>
/// Base of <see cref="FixtureDerived"/>. Together they guard the <c>Inherited = false</c> setting on
/// <see cref="EntityPrefixAttribute"/>: were the prefix inherited, scanning this assembly would find
/// two types claiming <c>fxbase</c> and every test in this file would fail with a duplicate.
/// </summary>
[EntityPrefix("fxbase")]
public class FixtureBase;

/// <inheritdoc cref="FixtureBase"/>
public sealed class FixtureDerived : FixtureBase;
