namespace Idora;

/// <summary>
/// Selects which time-ordered value follows the prefix in a generated identifier.
/// </summary>
/// <remarks>
/// Both options give you the two properties that matter: a value that is unique without
/// coordination, and one that sorts by the time it was created. They differ mainly in how long they
/// are and how they are written down.
/// </remarks>
public enum IdGenerationStrategy
{
    /// <summary>
    /// A 26-character lowercase ULID, for example <c>01k2f9v8ta7m3x0qbrn4dzeh5c</c>. This is the
    /// default.
    /// </summary>
    /// <remarks>
    /// A ULID holds the same 128 bits as a UUID but writes them in Crockford Base32, which needs 26
    /// characters instead of 32. Six characters per identifier is meaningful once it is a primary
    /// key repeated across every foreign key, index and log line, and the alphabet omits the
    /// characters people confuse when reading identifiers aloud. Prefer this unless you have a
    /// specific reason not to.
    /// </remarks>
    Ulid = 0,

    /// <summary>
    /// A 32-character lowercase UUIDv7 with no hyphens, for example
    /// <c>0195e7b4a1c87f3d9e2b6c4a8f01d5e3</c>.
    /// </summary>
    /// <remarks>
    /// Choose this when something outside your control expects a real UUID: a database column typed
    /// <c>uuid</c>, a schema that validates the format, or another service that parses the value.
    /// The value round-trips through <see cref="Guid.Parse(string)"/> and is a conformant RFC 9562
    /// version 7 UUID; it is simply printed without hyphens.
    /// </remarks>
    UuidV7 = 1,
}
