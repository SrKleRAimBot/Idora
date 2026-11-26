namespace Idora;

/// <summary>
/// Says which rule an identifier broke when it failed to parse.
/// </summary>
/// <remarks>
/// The rules are checked in the order listed here, and the first that fails is the one reported.
/// The same four reasons, with the same meanings, are reported by every Idora package.
/// </remarks>
public enum InvalidIdReason
{
    /// <summary>
    /// The text contains no underscore, so it cannot be split into a prefix and a value. Often a bare
    /// ULID or UUID that has lost its prefix, or an identifier from some other system altogether.
    /// </summary>
    NoSeparator = 0,

    /// <summary>
    /// Everything before the first underscore is not a valid entity prefix: it is empty, longer than
    /// 16 characters, or contains something other than an ASCII letter or digit. The
    /// <see cref="InvalidEntityPrefixException"/> describing the problem is the exception's
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    Prefix = 1,

    /// <summary>
    /// Everything after the first underscore is neither a 26-character lowercase ULID nor a
    /// 32-character lowercase UUIDv7.
    /// </summary>
    Value = 2,

    /// <summary>
    /// The identifier is well formed, but its prefix is not the one the caller asked for. Prefixes
    /// are compared exactly, case included.
    /// </summary>
    PrefixMismatch = 3,
}
