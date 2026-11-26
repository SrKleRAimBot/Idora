using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Idora.Internal;

namespace Idora;

/// <summary>
/// A parsed identifier such as <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>: its prefix, its value, the
/// format of that value and the moment it was minted.
/// </summary>
/// <remarks>
/// <para>
/// Generation is half of the job. Identifiers come back &#8212; in a route, a message, a foreign key
/// &#8212; and this is the type that checks them, reads them and puts them in order without
/// re-implementing the format. It accepts any conforming identifier, ULID or UUIDv7, whichever
/// strategy the local generator is configured with and whichever Idora package minted it.
/// </para>
/// <para>
/// Parsing is strict: it accepts exactly what a generator emits and nothing else. There is no
/// trimming, no case folding and no tolerance for hyphens, so an <see cref="IdoraId"/> always holds
/// the text that was stored, byte for byte. A failure throws an <see cref="InvalidIdException"/>
/// whose <see cref="InvalidIdException.Reason"/> says which rule was broken.
/// </para>
/// <para>
/// Two identifiers are equal when their text is ordinally equal. Their natural order is by
/// timestamp, then by value, then by prefix: chronological, so identifiers of different entity types
/// and different formats interleave in the order they were made, and exactly generation order for
/// ULIDs from one thread. See <see cref="CompareTo(IdoraId)"/>.
/// </para>
/// <para>
/// The struct wraps the original string, so parsing a <see cref="string"/> allocates nothing, and
/// <see cref="ToString()"/> and the conversion to <see cref="string"/> hand that same instance back.
/// <see cref="Prefix"/> and <see cref="Value"/> are cut from it on each read, so they allocate;
/// <see cref="HasPrefix"/> does not.
/// </para>
/// <para>
/// <c>default(IdoraId)</c> is the <em>empty</em> identifier. It is what an unassigned field holds,
/// and it is never the result of a successful parse. It reports <see cref="IsEmpty"/>, an empty
/// <see cref="Prefix"/>, <see cref="Value"/> and <see cref="ToString()"/>, a timestamp of the Unix
/// epoch and the <see cref="IdGenerationStrategy.Ulid"/> format; it equals only itself, and it sorts
/// before every real identifier.
/// </para>
/// <para>
/// The type converts to and from JSON as a plain string (through <see cref="IdoraIdJsonConverter"/>)
/// and has a <see cref="TypeConverter"/>, so it binds from routes, query strings and configuration
/// without further setup.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// IdoraId id = IdoraId.Parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c");
///
/// id.Prefix;      // "usr"
/// id.Format;      // IdGenerationStrategy.Ulid
/// id.Timestamp;   // 2025-08-12T09:14:23.274+00:00
///
/// if (IdoraId.TryParse(input, expectedPrefix: "ord", out IdoraId orderId))
/// {
///     // input is a well-formed order identifier.
/// }
/// </code>
/// </example>
[JsonConverter(typeof(IdoraIdJsonConverter))]
[TypeConverter(typeof(IdoraIdTypeConverter))]
public readonly struct IdoraId :
    IEquatable<IdoraId>,
    IComparable<IdoraId>,
    IComparable,
    ISpanFormattable,
    IUtf8SpanFormattable,
    ISpanParsable<IdoraId>
{
    /// <summary>
    /// The highest timestamp an identifier can carry: 2<sup>48</sup> &#8722; 1 milliseconds after the
    /// Unix epoch, some time in the year 10889.
    /// </summary>
    private const long MaxTimestampMs = (1L << 48) - 1;

    /// <summary>
    /// The last millisecond <see cref="DateTimeOffset"/> can represent, 9999-12-31T23:59:59.999Z, as a
    /// Unix millisecond count.
    /// </summary>
    private const long MaxDateTimeOffsetMs = 253_402_300_799_999;

    /// <summary>The lowest UUIDv7 random field: version 7, variant 8, and every other bit clear.</summary>
    private const string UuidV7LowerRandom = "70008000000000000000";

    /// <summary>The highest UUIDv7 random field: version 7, variant b, and every other bit set.</summary>
    private const string UuidV7UpperRandom = "7fffbfffffffffffffff";

    /// <summary>Writes a range bound's text straight into its string's buffer.</summary>
    private static readonly SpanAction<char, BoundState> WriteBound = static (destination, state) =>
    {
        state.Prefix.CopyTo(destination);
        destination[state.Prefix.Length] = EntityPrefixValidator.Separator;
        Span<char> value = destination[(state.Prefix.Length + 1)..];

        if (state.Format == IdGenerationStrategy.Ulid)
        {
            // All 80 random bits clear for the lower bound, all set for the upper: sixteen '0's or 'z's.
            CrockfordBase32.EncodeUlid(
                state.TimestampMs,
                randomHigh: state.Upper ? 0xFFFFUL : 0UL,
                randomLow: state.Upper ? ulong.MaxValue : 0UL,
                value);
            return;
        }

        // Twelve hexadecimal digits of timestamp, then the lowest or highest random field a UUIDv7
        // can carry once its version (7) and variant (binary 10xx) are fixed.
        state.TimestampMs.TryFormat(value, out _, "x12", CultureInfo.InvariantCulture);
        (state.Upper ? UuidV7UpperRandom : UuidV7LowerRandom).CopyTo(value[IdValueReader.UuidV7TimestampLength..]);
    };

    /// <summary>The whole identifier, or <see langword="null"/> for <c>default</c>.</summary>
    private readonly string? _text;

    /// <summary>The millisecond count the value begins with, decoded once when parsed.</summary>
    private readonly long _timestampMs;

    /// <summary>Index of the first underscore, which is also the length of the prefix.</summary>
    private readonly int _separatorIndex;

    /// <summary>Wraps text that has already been checked.</summary>
    private IdoraId(string text, int separatorIndex, long timestampMs)
    {
        _text = text;
        _separatorIndex = separatorIndex;
        _timestampMs = timestampMs;
    }

    /// <summary>
    /// The text before the first underscore, exactly as written: <c>usr</c> for
    /// <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>. Empty for <c>default</c>.
    /// </summary>
    /// <remarks>
    /// A new string is cut from the identifier each time this is read. To test the prefix without
    /// allocating, use <see cref="HasPrefix"/>.
    /// </remarks>
    public string Prefix => _text is null ? string.Empty : _text[.._separatorIndex];

    /// <summary>
    /// The text after the first underscore: the 26-character ULID or 32-character UUIDv7. Empty for
    /// <c>default</c>.
    /// </summary>
    /// <remarks>A new string is cut from the identifier each time this is read.</remarks>
    public string Value => _text is null ? string.Empty : _text[(_separatorIndex + 1)..];

    /// <summary>
    /// Whether the value is a ULID or a UUIDv7, decided by its length.
    /// <see cref="IdGenerationStrategy.Ulid"/> for <c>default</c>.
    /// </summary>
    public IdGenerationStrategy Format =>
        _text is not null && _text.Length - _separatorIndex - 1 == IdValueReader.UuidV7Length
            ? IdGenerationStrategy.UuidV7
            : IdGenerationStrategy.Ulid;

    /// <summary>
    /// The 48-bit count of milliseconds since the Unix epoch that the value begins with. Zero for
    /// <c>default</c>.
    /// </summary>
    /// <remarks>
    /// Read from the identifier itself, so it is the same on every machine: it depends on neither
    /// the reader's clock nor its time zone.
    /// </remarks>
    public long TimestampMilliseconds => _timestampMs;

    /// <summary>
    /// The moment the identifier was minted, in UTC, to the millisecond. The Unix epoch for
    /// <c>default</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offset is always zero. Convert it with <see cref="DateTimeOffset.ToLocalTime"/> if you
    /// want to display it in local time; the identifier itself carries no time zone.
    /// </para>
    /// <para>
    /// A 48-bit timestamp runs to the year 10889, further than <see cref="DateTimeOffset"/> can
    /// count. An identifier stamped after the last millisecond of the year 9999 &#8212; which no
    /// clock will produce, but which a hand-made identifier can carry &#8212; reports
    /// <see cref="DateTimeOffset.MaxValue"/> here rather than throwing from a property;
    /// <see cref="TimestampMilliseconds"/> always holds the exact value.
    /// </para>
    /// </remarks>
    public DateTimeOffset Timestamp => _timestampMs <= MaxDateTimeOffsetMs
        ? DateTimeOffset.FromUnixTimeMilliseconds(_timestampMs)
        : DateTimeOffset.MaxValue;

    /// <summary>
    /// Whether this is <c>default(IdoraId)</c> rather than a parsed identifier.
    /// </summary>
    public bool IsEmpty => _text is null;

    /// <summary>The prefix, without allocating. Empty for <c>default</c>.</summary>
    private ReadOnlySpan<char> PrefixSpan => _text is null ? default : _text.AsSpan(0, _separatorIndex);

    /// <summary>The value, without allocating. Empty for <c>default</c>.</summary>
    private ReadOnlySpan<char> ValueSpan => _text is null ? default : _text.AsSpan(_separatorIndex + 1);

    /// <summary>Whether two identifiers have ordinally equal text.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator ==(IdoraId left, IdoraId right) => left.Equals(right);

    /// <summary>Whether two identifiers have different text.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator !=(IdoraId left, IdoraId right) => !left.Equals(right);

    /// <summary>Whether <paramref name="left"/> comes strictly before <paramref name="right"/> in the natural order.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator <(IdoraId left, IdoraId right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes strictly after <paramref name="right"/> in the natural order.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator >(IdoraId left, IdoraId right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> comes before, or is equal to, <paramref name="right"/>.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator <=(IdoraId left, IdoraId right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> comes after, or is equal to, <paramref name="right"/>.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    public static bool operator >=(IdoraId left, IdoraId right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Returns the identifier's text, so an <see cref="IdoraId"/> can be passed wherever a string
    /// identifier is expected. The empty string for <c>default</c>.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <remarks>Implicit because nothing is lost: the text is exactly what was parsed.</remarks>
    public static implicit operator string(IdoraId id) => id.ToString();

    /// <summary>Parses <paramref name="text"/> as an identifier.</summary>
    /// <param name="text">The text to parse.</param>
    /// <remarks>
    /// Explicit because it can fail. Equivalent to <see cref="Parse(string)"/>, which reads better
    /// at most call sites.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidIdException"><paramref name="text"/> is not a valid identifier.</exception>
    public static explicit operator IdoraId(string text) => Parse(text);

    /// <summary>Parses an identifier, throwing if it is not valid.</summary>
    /// <param name="text">The text to parse, such as <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>.</param>
    /// <returns>The parsed identifier, wrapping <paramref name="text"/> itself.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidIdException">
    /// <paramref name="text"/> has no underscore, an invalid prefix, or a value that is neither a
    /// lowercase ULID nor a lowercase UUIDv7. <see cref="InvalidIdException.Reason"/> says which.
    /// </exception>
    /// <example>
    /// <code>
    /// IdoraId id = IdoraId.Parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c");
    /// </code>
    /// </example>
    public static IdoraId Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParseCore(text, expectedPrefix: null, out int separatorIndex, out long timestampMs) is { } failure
            ? throw CreateException(text, expectedPrefix: null, failure, separatorIndex)
            : new IdoraId(text, separatorIndex, timestampMs);
    }

    /// <summary>
    /// Parses an identifier that must carry a particular prefix, throwing if it is not valid or
    /// carries another.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <returns>The parsed identifier, wrapping <paramref name="text"/> itself.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="text"/> or <paramref name="expectedPrefix"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidIdException">
    /// <paramref name="text"/> is not a valid identifier, or its prefix is not
    /// <paramref name="expectedPrefix"/> (<see cref="InvalidIdReason.PrefixMismatch"/>). A malformed
    /// identifier is reported as malformed, whatever its prefix.
    /// </exception>
    /// <remarks>
    /// When the prefix belongs to a registered entity type, prefer
    /// <see cref="EntityPrefixRegistryExtensions.ParseIdFor{TEntity}(IEntityPrefixRegistry, string)"/>,
    /// which looks the prefix up rather than repeating it as a string literal.
    /// </remarks>
    /// <example>
    /// <code>
    /// IdoraId userId = IdoraId.Parse(route, expectedPrefix: "usr");
    /// </code>
    /// </example>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static IdoraId Parse(string text, string expectedPrefix)
    {
        // The expected prefix first: it is the caller's, and an invalid one is reported as such
        // whatever the text is, even a missing one (specification section 6.1).
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));
        ArgumentNullException.ThrowIfNull(text);

        return TryParseCore(text, expectedPrefix, out int separatorIndex, out long timestampMs) is { } failure
            ? throw CreateException(text, expectedPrefix, failure, separatorIndex)
            : new IdoraId(text, separatorIndex, timestampMs);
    }

    /// <summary>Parses an identifier from a span of characters, throwing if it is not valid.</summary>
    /// <param name="text">The characters to parse.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="InvalidIdException"><paramref name="text"/> is not a valid identifier.</exception>
    /// <remarks>
    /// The identifier keeps its own copy of the text, so this allocates exactly one string, and only
    /// once the text is known to be valid.
    /// </remarks>
    public static IdoraId Parse(ReadOnlySpan<char> text) =>
        TryParseCore(text, expectedPrefix: null, out int separatorIndex, out long timestampMs) is { } failure
            ? throw CreateException(text.ToString(), expectedPrefix: null, failure, separatorIndex)
            : new IdoraId(text.ToString(), separatorIndex, timestampMs);

    /// <summary>
    /// Parses an identifier that must carry a particular prefix from a span of characters, throwing
    /// if it is not valid or carries another.
    /// </summary>
    /// <param name="text">The characters to parse.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidIdException">
    /// <paramref name="text"/> is not a valid identifier, or its prefix is not
    /// <paramref name="expectedPrefix"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static IdoraId Parse(ReadOnlySpan<char> text, string expectedPrefix)
    {
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));

        return TryParseCore(text, expectedPrefix, out int separatorIndex, out long timestampMs) is { } failure
            ? throw CreateException(text.ToString(), expectedPrefix, failure, separatorIndex)
            : new IdoraId(text.ToString(), separatorIndex, timestampMs);
    }

    /// <summary>Parses an identifier, reporting failure rather than throwing.</summary>
    /// <param name="text">The text to parse. <see langword="null"/> is simply not valid.</param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> is a valid identifier.</returns>
    /// <example>
    /// <code>
    /// if (!IdoraId.TryParse(input, out IdoraId id))
    /// {
    ///     return Results.BadRequest();
    /// }
    /// </code>
    /// </example>
    public static bool TryParse([NotNullWhen(true)] string? text, out IdoraId id)
    {
        if (text is not null
            && TryParseCore(text, expectedPrefix: null, out int separatorIndex, out long timestampMs) is null)
        {
            id = new IdoraId(text, separatorIndex, timestampMs);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Parses an identifier that must carry a particular prefix, reporting failure rather than
    /// throwing.
    /// </summary>
    /// <param name="text">The text to parse. <see langword="null"/> is simply not valid.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> is a valid identifier with the prefix
    /// <paramref name="expectedPrefix"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="expectedPrefix"/> is <see langword="null"/>. That is a mistake in the calling
    /// code, not in the input, so it is not reported as a <see langword="false"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static bool TryParse([NotNullWhen(true)] string? text, string expectedPrefix, out IdoraId id)
    {
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));

        if (text is not null
            && TryParseCore(text, expectedPrefix, out int separatorIndex, out long timestampMs) is null)
        {
            id = new IdoraId(text, separatorIndex, timestampMs);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Parses an identifier from a span of characters, reporting failure rather than throwing.</summary>
    /// <param name="text">The characters to parse.</param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> is a valid identifier.</returns>
    /// <remarks>Allocates the identifier's string on success, and nothing on failure.</remarks>
    public static bool TryParse(ReadOnlySpan<char> text, out IdoraId id)
    {
        if (TryParseCore(text, expectedPrefix: null, out int separatorIndex, out long timestampMs) is null)
        {
            id = new IdoraId(text.ToString(), separatorIndex, timestampMs);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Parses an identifier that must carry a particular prefix from a span of characters, reporting
    /// failure rather than throwing.
    /// </summary>
    /// <param name="text">The characters to parse.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> is a valid identifier with the prefix
    /// <paramref name="expectedPrefix"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static bool TryParse(ReadOnlySpan<char> text, string expectedPrefix, out IdoraId id)
    {
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));

        if (TryParseCore(text, expectedPrefix, out int separatorIndex, out long timestampMs) is null)
        {
            id = new IdoraId(text.ToString(), separatorIndex, timestampMs);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Reports whether text is a valid identifier, without allocating.</summary>
    /// <param name="text">The text to check. <see langword="null"/> is simply not valid.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> would parse.</returns>
    public static bool IsValid([NotNullWhen(true)] string? text) =>
        text is not null && TryParseCore(text, expectedPrefix: null, out _, out _) is null;

    /// <summary>
    /// Reports whether text is a valid identifier with a particular prefix, without allocating.
    /// </summary>
    /// <param name="text">The text to check. <see langword="null"/> is simply not valid.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> would parse and has the prefix
    /// <paramref name="expectedPrefix"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static bool IsValid([NotNullWhen(true)] string? text, string expectedPrefix)
    {
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));

        return text is not null && TryParseCore(text, expectedPrefix, out _, out _) is null;
    }

    /// <summary>Reports whether a span of characters is a valid identifier, without allocating.</summary>
    /// <param name="text">The characters to check.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> would parse.</returns>
    public static bool IsValid(ReadOnlySpan<char> text) =>
        TryParseCore(text, expectedPrefix: null, out _, out _) is null;

    /// <summary>
    /// Reports whether a span of characters is a valid identifier with a particular prefix, without
    /// allocating.
    /// </summary>
    /// <param name="text">The characters to check.</param>
    /// <param name="expectedPrefix">
    /// The prefix the identifier must have, compared exactly, case included.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> would parse and has the prefix
    /// <paramref name="expectedPrefix"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="expectedPrefix"/> is not a valid prefix. That is a mistake in the calling code,
    /// not in the input, so it is reported whatever the text holds.
    /// </exception>
    public static bool IsValid(ReadOnlySpan<char> text, string expectedPrefix)
    {
        EntityPrefixValidator.Validate(expectedPrefix, nameof(expectedPrefix));

        return TryParseCore(text, expectedPrefix, out _, out _) is null;
    }

    /// <summary>
    /// Returns the lowest identifier any conforming generator can mint with
    /// <paramref name="prefix"/> during the millisecond <paramref name="timestamp"/> falls in.
    /// </summary>
    /// <param name="prefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <param name="timestamp">
    /// The instant. Anything below a millisecond is truncated, so every identifier minted during
    /// that millisecond is included.
    /// </param>
    /// <param name="format">The value format the identifiers use. Defaults to ULID.</param>
    /// <returns>
    /// The inclusive lower bound: the timestamp followed by the lowest possible random field.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestamp"/> is before the Unix epoch, or <paramref name="format"/> is not a
    /// defined value.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Because a value starts with its timestamp, the identifiers minted for one prefix over a span
    /// of time form a contiguous range of strings. Together with
    /// <see cref="UpperBound(string, DateTimeOffset, IdGenerationStrategy)"/> this selects them
    /// from a database column with two plain string comparisons, without parsing a row or keeping a
    /// separate creation-time column.
    /// </para>
    /// <para>
    /// The bounds are themselves valid identifiers. They compare correctly only against identifiers
    /// with the same prefix and format, compared as ordinal strings &#8212; which is exactly the shape
    /// of a key column holding one entity type.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// string from = IdoraId.LowerBound("ord", startOfDay);
    /// string to   = IdoraId.UpperBound("ord", endOfDay);
    ///
    /// var todaysOrders = db.Orders.Where(o => string.Compare(o.Id, from) >= 0 &amp;&amp; string.Compare(o.Id, to) &lt;= 0);
    /// </code>
    /// </example>
    public static IdoraId LowerBound(
        string prefix,
        DateTimeOffset timestamp,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        CreateBound(prefix, timestamp.ToUnixTimeMilliseconds(), nameof(timestamp), format, upper: false);

    /// <summary>
    /// Returns the lowest identifier any conforming generator can mint with
    /// <paramref name="prefix"/> during the millisecond <paramref name="timestampMilliseconds"/>.
    /// </summary>
    /// <param name="prefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <param name="timestampMilliseconds">
    /// Milliseconds since the Unix epoch, from 0 to 2<sup>48</sup> &#8722; 1.
    /// </param>
    /// <param name="format">The value format the identifiers use. Defaults to ULID.</param>
    /// <returns>The inclusive lower bound.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestampMilliseconds"/> is outside the 48-bit range, or
    /// <paramref name="format"/> is not a defined value.
    /// </exception>
    /// <remarks>See <see cref="LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/>.</remarks>
    public static IdoraId LowerBound(
        string prefix,
        long timestampMilliseconds,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        CreateBound(prefix, timestampMilliseconds, nameof(timestampMilliseconds), format, upper: false);

    /// <summary>
    /// Returns the highest identifier any conforming generator can mint with
    /// <paramref name="prefix"/> during the millisecond <paramref name="timestamp"/> falls in.
    /// </summary>
    /// <param name="prefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <param name="timestamp">
    /// The instant. Anything below a millisecond is truncated, and the bound covers the whole of the
    /// millisecond that remains.
    /// </param>
    /// <param name="format">The value format the identifiers use. Defaults to ULID.</param>
    /// <returns>
    /// The inclusive upper bound: the timestamp followed by the highest possible random field.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestamp"/> is before the Unix epoch, or <paramref name="format"/> is not a
    /// defined value.
    /// </exception>
    /// <remarks>See <see cref="LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/>.</remarks>
    public static IdoraId UpperBound(
        string prefix,
        DateTimeOffset timestamp,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        CreateBound(prefix, timestamp.ToUnixTimeMilliseconds(), nameof(timestamp), format, upper: true);

    /// <summary>
    /// Returns the highest identifier any conforming generator can mint with
    /// <paramref name="prefix"/> during the millisecond <paramref name="timestampMilliseconds"/>.
    /// </summary>
    /// <param name="prefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <param name="timestampMilliseconds">
    /// Milliseconds since the Unix epoch, from 0 to 2<sup>48</sup> &#8722; 1.
    /// </param>
    /// <param name="format">The value format the identifiers use. Defaults to ULID.</param>
    /// <returns>The inclusive upper bound.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestampMilliseconds"/> is outside the 48-bit range, or
    /// <paramref name="format"/> is not a defined value.
    /// </exception>
    /// <remarks>See <see cref="LowerBound(string, DateTimeOffset, IdGenerationStrategy)"/>.</remarks>
    public static IdoraId UpperBound(
        string prefix,
        long timestampMilliseconds,
        IdGenerationStrategy format = IdGenerationStrategy.Ulid) =>
        CreateBound(prefix, timestampMilliseconds, nameof(timestampMilliseconds), format, upper: true);

    /// <summary>
    /// Returns the length of every identifier with <paramref name="prefix"/> in
    /// <paramref name="format"/>: the prefix, the separator and the value.
    /// </summary>
    /// <param name="prefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <param name="format">The value format.</param>
    /// <returns>For example 30 for <c>usr</c> with ULIDs, or 36 with UUIDv7s.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException"><paramref name="prefix"/> is not a valid prefix.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    /// <remarks>
    /// Every identifier for a given prefix and format has exactly this length, which makes it the
    /// right size for a database column.
    /// </remarks>
    public static int GetLength(string prefix, IdGenerationStrategy format)
    {
        EntityPrefixValidator.Validate(prefix, nameof(prefix));

        return prefix.Length + 1 + GetValueLength(format);
    }

    /// <summary>
    /// Returns the length of the longest identifier possible in <paramref name="format"/>: a
    /// 16-character prefix, the separator and the value.
    /// </summary>
    /// <param name="format">The value format.</param>
    /// <returns>43 for ULIDs, 49 for UUIDv7s.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    /// <remarks>
    /// The size to allow when the prefix is not known, or a column holds identifiers for more than one
    /// entity type.
    /// </remarks>
    public static int GetMaxLength(IdGenerationStrategy format) =>
        EntityPrefixValidator.MaxLength + 1 + GetValueLength(format);

    /// <summary>
    /// Compares this identifier with another in their natural order: by timestamp, then by value,
    /// then by prefix.
    /// </summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns>
    /// Less than zero if this identifier comes first, zero if the two are equal, and greater than
    /// zero if it comes second.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The order is chronological and total, and consistent with <see cref="Equals(IdoraId)"/>:
    /// <list type="number">
    ///   <item><description>by <see cref="TimestampMilliseconds"/>, oldest first;</description></item>
    ///   <item><description>then by <see cref="Value"/>, ordinally;</description></item>
    ///   <item><description>then by <see cref="Prefix"/>, ordinally.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Timestamp first, rather than plain text, is what lets identifiers of different entity types
    /// and formats interleave in the order they were made: an order created after a user sorts after
    /// that user even though <c>ord</c> &lt; <c>usr</c>. Within one format the value comparison agrees
    /// with the timestamp, so ULIDs from one thread sort in exactly the order they were generated.
    /// </para>
    /// <para><c>default(IdoraId)</c> sorts before every parsed identifier.</para>
    /// </remarks>
    public int CompareTo(IdoraId other)
    {
        int byTimestamp = _timestampMs.CompareTo(other._timestampMs);
        if (byTimestamp != 0)
        {
            return byTimestamp;
        }

        int byValue = ValueSpan.SequenceCompareTo(other.ValueSpan);
        if (byValue != 0)
        {
            return byValue;
        }

        return PrefixSpan.SequenceCompareTo(other.PrefixSpan);
    }

    /// <summary>
    /// Compares this identifier with an object, which must be an <see cref="IdoraId"/> or
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>As <see cref="CompareTo(IdoraId)"/>; <see langword="null"/> sorts first.</returns>
    /// <exception cref="ArgumentException"><paramref name="obj"/> is not an <see cref="IdoraId"/>.</exception>
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        IdoraId other => CompareTo(other),
        _ => throw new ArgumentException("The object to compare with must be an IdoraId.", nameof(obj)),
    };

    /// <summary>Whether this identifier comes strictly before <paramref name="other"/> in the natural order.</summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns><see langword="true"/> if <c>CompareTo(other) &lt; 0</c>.</returns>
    /// <remarks>
    /// Two identifiers minted in the same millisecond are still strictly ordered here. To ask only
    /// about the millisecond, use <see cref="CompareTimestamp"/>.
    /// </remarks>
    public bool IsBefore(IdoraId other) => CompareTo(other) < 0;

    /// <summary>Whether this identifier comes strictly after <paramref name="other"/> in the natural order.</summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns><see langword="true"/> if <c>CompareTo(other) &gt; 0</c>.</returns>
    public bool IsAfter(IdoraId other) => CompareTo(other) > 0;

    /// <summary>Compares only the millisecond each identifier was minted in.</summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns>
    /// Less than zero if this identifier was minted in an earlier millisecond, zero if in the same
    /// one, and greater than zero if in a later one.
    /// </returns>
    /// <remarks>
    /// Identifiers from different machines within a few milliseconds of each other are only as
    /// ordered as those machines' clocks; this comparison makes no claim finer than the timestamp.
    /// </remarks>
    public int CompareTimestamp(IdoraId other) => _timestampMs.CompareTo(other._timestampMs);

    /// <summary>Whether the two identifiers were minted in the same millisecond.</summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns><see langword="true"/> if <c>CompareTimestamp(other) == 0</c>.</returns>
    public bool IsSameMillisecond(IdoraId other) => _timestampMs == other._timestampMs;

    /// <summary>
    /// Whether this identifier was minted strictly before <paramref name="instant"/>, at millisecond
    /// precision.
    /// </summary>
    /// <param name="instant">
    /// The instant to compare with. Anything below a millisecond is truncated first, so an
    /// identifier minted during the instant's own millisecond is not before it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <see cref="TimestampMilliseconds"/> is less than the instant's.
    /// </returns>
    public bool IsCreatedBefore(DateTimeOffset instant) => _timestampMs < instant.ToUnixTimeMilliseconds();

    /// <summary>
    /// Whether this identifier was minted strictly after <paramref name="instant"/>, at millisecond
    /// precision.
    /// </summary>
    /// <param name="instant">
    /// The instant to compare with. Anything below a millisecond is truncated first, so an
    /// identifier minted during the instant's own millisecond is not after it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <see cref="TimestampMilliseconds"/> is greater than the instant's.
    /// </returns>
    public bool IsCreatedAfter(DateTimeOffset instant) => _timestampMs > instant.ToUnixTimeMilliseconds();

    /// <summary>
    /// Whether the identifier's prefix is exactly <paramref name="prefix"/>, case included, without
    /// allocating.
    /// </summary>
    /// <param name="prefix">The prefix to test for, without the trailing underscore.</param>
    /// <returns><see langword="true"/> if the prefixes are ordinally equal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Exact rather than case-insensitive, even though registration treats <c>usr</c> and
    /// <c>USR</c> as a collision: generators emit the prefix exactly as registered, so a stored
    /// <c>USR_</c> identifier was not minted for the <c>usr</c> entity.
    /// </remarks>
    public bool HasPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return _text is not null && PrefixSpan.SequenceEqual(prefix);
    }

    /// <summary>Whether this identifier's text is ordinally equal to <paramref name="other"/>'s.</summary>
    /// <param name="other">The identifier to compare with.</param>
    /// <returns><see langword="true"/> if the two are the same identifier.</returns>
    public bool Equals(IdoraId other) => string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is IdoraId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _text?.GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <summary>
    /// Returns the identifier exactly as it was parsed, such as
    /// <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>, or the empty string for <c>default</c>.
    /// </summary>
    /// <returns>The identifier's text. For an identifier parsed from a string, that same string.</returns>
    public override string ToString() => _text ?? string.Empty;

    /// <summary>Returns the identifier's text. An identifier has only one textual form.</summary>
    /// <param name="format">Must be <see langword="null"/>, empty or <c>"G"</c>.</param>
    /// <param name="formatProvider">Ignored; the text does not vary by culture.</param>
    /// <returns>The same as <see cref="ToString()"/>.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is anything else.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        ThrowIfUnsupportedFormat(format);

        return ToString();
    }

    /// <summary>Copies the identifier's text into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to write to.</param>
    /// <param name="charsWritten">Receives the number of characters written.</param>
    /// <param name="format">Must be empty or <c>"G"</c>.</param>
    /// <param name="provider">Ignored; the text does not vary by culture.</param>
    /// <returns><see langword="false"/> if <paramref name="destination"/> is too small.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is anything else.</exception>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        ThrowIfUnsupportedFormat(format);

        ReadOnlySpan<char> text = _text;
        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    /// <summary>
    /// Writes the identifier's text into <paramref name="utf8Destination"/> as UTF-8, which for an
    /// identifier is one byte per character.
    /// </summary>
    /// <param name="utf8Destination">The buffer to write to.</param>
    /// <param name="bytesWritten">Receives the number of bytes written.</param>
    /// <param name="format">Must be empty or <c>"G"</c>.</param>
    /// <param name="provider">Ignored; the text does not vary by culture.</param>
    /// <returns><see langword="false"/> if <paramref name="utf8Destination"/> is too small.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is anything else.</exception>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        ThrowIfUnsupportedFormat(format);

        ReadOnlySpan<char> text = _text;
        if (text.Length > utf8Destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        // Every character of an identifier is ASCII, so the UTF-8 form is a narrowing copy.
        for (int i = 0; i < text.Length; i++)
        {
            utf8Destination[i] = (byte)text[i];
        }

        bytesWritten = text.Length;
        return true;
    }

    /// <inheritdoc cref="Parse(string)"/>
    static IdoraId IParsable<IdoraId>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc cref="TryParse(string, out IdoraId)"/>
    static bool IParsable<IdoraId>.TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out IdoraId result) =>
        TryParse(s, out result);

    /// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
    static IdoraId ISpanParsable<IdoraId>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out IdoraId)"/>
    static bool ISpanParsable<IdoraId>.TryParse(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        out IdoraId result) =>
        TryParse(s, out result);

    /// <summary>
    /// Runs every parsing rule in order, without allocating.
    /// </summary>
    /// <returns><see langword="null"/> on success, or the first rule that failed.</returns>
    private static InvalidIdReason? TryParseCore(
        ReadOnlySpan<char> text,
        string? expectedPrefix,
        out int separatorIndex,
        out long timestampMs)
    {
        timestampMs = 0;

        separatorIndex = text.IndexOf(EntityPrefixValidator.Separator);
        if (separatorIndex < 0)
        {
            return InvalidIdReason.NoSeparator;
        }

        ReadOnlySpan<char> prefix = text[..separatorIndex];
        if (!EntityPrefixValidator.IsValid(prefix))
        {
            return InvalidIdReason.Prefix;
        }

        if (!IdValueReader.TryRead(text[(separatorIndex + 1)..], out timestampMs))
        {
            return InvalidIdReason.Value;
        }

        if (expectedPrefix is not null && !prefix.SequenceEqual(expectedPrefix))
        {
            return InvalidIdReason.PrefixMismatch;
        }

        return null;
    }

    /// <summary>Explains a failure <see cref="TryParseCore"/> has already found.</summary>
    private static InvalidIdException CreateException(
        string text,
        string? expectedPrefix,
        InvalidIdReason reason,
        int separatorIndex) => reason switch
    {
        InvalidIdReason.NoSeparator => InvalidIdException.ForNoSeparator(text, expectedPrefix),
        InvalidIdReason.Prefix => InvalidIdException.ForPrefix(
            text,
            expectedPrefix,
            EntityPrefixValidator.Describe(text[..separatorIndex])),
        InvalidIdReason.Value => InvalidIdException.ForValue(
            text,
            expectedPrefix,
            IdValueReader.Describe(text.AsSpan(separatorIndex + 1))),
        _ => InvalidIdException.ForPrefixMismatch(text, text[..separatorIndex], expectedPrefix!),
    };

    /// <summary>Builds a range bound, validating everything a caller can get wrong first.</summary>
    private static IdoraId CreateBound(
        string prefix,
        long timestampMs,
        string timestampParameterName,
        IdGenerationStrategy format,
        bool upper)
    {
        EntityPrefixValidator.Validate(prefix, nameof(prefix));
        ArgumentOutOfRangeException.ThrowIfNegative(timestampMs, timestampParameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timestampMs, MaxTimestampMs, timestampParameterName);

        int valueLength = GetValueLength(format);
        string text = string.Create(
            prefix.Length + 1 + valueLength,
            new BoundState(prefix, timestampMs, format, upper),
            WriteBound);

        return new IdoraId(text, prefix.Length, timestampMs);
    }

    /// <summary>Everything <see cref="WriteBound"/> needs, passed by value so it closes over nothing.</summary>
    private readonly record struct BoundState(string Prefix, long TimestampMs, IdGenerationStrategy Format, bool Upper);

    /// <summary>The number of characters in a value of <paramref name="format"/>.</summary>
    private static int GetValueLength(IdGenerationStrategy format) => format switch
    {
        IdGenerationStrategy.Ulid => CrockfordBase32.UlidLength,
        IdGenerationStrategy.UuidV7 => IdValueReader.UuidV7Length,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "The identifier format is not one of the defined values."),
    };

    /// <summary>Rejects any format string other than the general one.</summary>
    private static void ThrowIfUnsupportedFormat(ReadOnlySpan<char> format)
    {
        if (!format.IsEmpty && !format.SequenceEqual("G"))
        {
            throw new FormatException(
                "An identifier has a single textual form, so the only format strings it accepts are "
                + "empty and \"G\".");
        }
    }
}
