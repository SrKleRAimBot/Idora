namespace Idora.Tests.Support;

/// <summary>
/// Attributed types for the cases an assembly scan cannot express: clashes, malformed prefixes and
/// ordering. They live in the test assembly rather than in <c>Idora.Tests.Fixtures</c>
/// precisely because that assembly is scanned wholesale, and half of these would break every scan of
/// it. Tests reach them through the scanner's type-list seam instead.
/// </summary>
[EntityPrefix("acc")]
internal sealed class AttributedAccount;

/// <summary>
/// Clashes with <see cref="AttributedAccount"/> on case alone. Its full name sorts after
/// <c>AttributedAccount</c>, so it is always the one reported as the offender.
/// </summary>
[EntityPrefix("ACC")]
internal sealed class AttributedAccrual;

[EntityPrefix("cus")]
internal sealed class AttributedCustomer;

[EntityPrefix("shp")]
internal sealed class AttributedShipment;

/// <summary>Carries no attribute, and must be skipped rather than rejected.</summary>
internal sealed class AttributedNothing;

[EntityPrefix("bad_prefix")]
internal sealed class AttributedWithSeparator;

[EntityPrefix("")]
internal sealed class AttributedWithEmptyPrefix;

[EntityPrefix("thisprefixisfartoolong")]
internal sealed class AttributedWithOverlongPrefix;

/// <summary>Nothing stops <c>[EntityPrefix(null!)]</c> compiling, so the scan has to cope with it.</summary>
[EntityPrefix(null!)]
internal sealed class AttributedWithNullPrefix;
