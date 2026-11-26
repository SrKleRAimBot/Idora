package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.internal.CrockfordBase32;
import io.github.srkleraimbot.idora.internal.UuidV7;

import java.io.InvalidObjectException;
import java.io.ObjectInputStream;
import java.io.Serializable;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Arrays;
import java.util.Comparator;
import java.util.Objects;
import java.util.Optional;

/**
 * A parsed identifier such as {@code usr_01k2f9v8ta7m3x0qbrn4dzeh5c}: its prefix, its value, the
 * value's format and the instant the value was minted.
 *
 * <pre>{@code
 * IdoraId id = IdoraId.parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c");
 *
 * id.prefix();      // "usr"
 * id.value();       // "01k2f9v8ta7m3x0qbrn4dzeh5c"
 * id.format();      // IdGenerationStrategy.ULID
 * id.timestamp();   // 2025-08-12T14:09:47.850Z
 * id.toString();    // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
 * }</pre>
 *
 * <p>Any conforming identifier parses, whichever strategy the local generator uses and whichever
 * Idora package minted it. Parsing is <strong>strict</strong>: it accepts exactly the text a
 * conforming generator emits and nothing else, with no trimming, no case folding and no tolerance
 * for hyphens. A rejected text fails with an {@link InvalidIdException} that says which rule it
 * broke; {@link #tryParse(CharSequence)} and {@link #isValid(CharSequence)} report the same
 * verdict without throwing.
 *
 * <p>To check an identifier against the entity a route expects, ask the generator, which looks the
 * prefix up in its registry:
 *
 * <pre>{@code
 * IdoraId orderId = ids.parseId(Order.class, pathVariable);   // throws unless it is an "ord_" identifier
 * }</pre>
 *
 * <h2>Equality and order</h2>
 *
 * <p>Two identifiers are equal when their texts are equal, character for character. The natural
 * order is chronological, total and consistent with {@code equals}: by timestamp, oldest first;
 * then by value; then by prefix, each compared ordinally. Ordering by timestamp first is what makes
 * identifiers of different entity types and different formats interleave in the order they were
 * made, so an order created after a user sorts after that user even though {@code "ord" < "usr"}.
 * For ULIDs generated on one thread the natural order is exactly generation order.
 *
 * <p>Comparing by the millisecond alone, which ties identifiers minted in the same millisecond, is
 * {@link #compareTimestamp(IdoraId)} or the {@link #TIMESTAMP_ORDER} comparator.
 *
 * <h2>Range bounds</h2>
 *
 * <p>Because a value begins with its timestamp, every identifier minted for one prefix during one
 * span of time lies in one contiguous range of strings. {@link #lowerBound(String, Instant,
 * IdGenerationStrategy)} and {@link #upperBound(String, Instant, IdGenerationStrategy)} give its
 * ends, so a query over a column of identifiers can select by creation time without a timestamp
 * column:
 *
 * <pre>{@code
 * // WHERE id >= ? AND id <= ?
 * statement.setString(1, IdoraId.lowerBound("usr", from, IdGenerationStrategy.ULID).toString());
 * statement.setString(2, IdoraId.upperBound("usr", to, IdGenerationStrategy.ULID).toString());
 * }</pre>
 *
 * <p>Instances are immutable and safe to share between threads. The serialized form is the text
 * alone, and it is parsed again, strictly, when read back.
 */
public final class IdoraId implements Comparable<IdoraId>, Serializable {

    private static final long serialVersionUID = 1L;

    /**
     * Orders identifiers by the millisecond they were minted in and nothing else, so identifiers
     * from the same millisecond compare as equal whatever their prefix or value.
     *
     * <p>This comparator is <strong>not</strong> consistent with {@code equals}: a
     * {@link java.util.TreeSet} built with it keeps only one identifier per millisecond. Use it to
     * sort or to group by time; use the natural order for anything that must tell identifiers
     * apart.
     */
    public static final Comparator<IdoraId> TIMESTAMP_ORDER = IdoraId::compareTimestamp;

    /** The largest 48-bit millisecond count, the last instant a value can carry. */
    private static final long MAX_TIMESTAMP = (1L << 48) - 1;

    /** Length of a ULID value. */
    private static final int ULID_LENGTH = CrockfordBase32.ULID_LENGTH;

    /** Length of a hyphen-free UUIDv7 value. */
    private static final int UUID_V7_LENGTH = UuidV7.HEX_LENGTH;

    /** The random bytes of the lowest and highest UUIDv7 values; the layout masks them as needed. */
    private static final byte[] NO_RANDOM_BITS = new byte[UuidV7.RANDOM_BYTES];
    private static final byte[] ALL_RANDOM_BITS = filled((byte) 0xFF);

    /** The whole identifier, exactly as parsed. */
    private final String text;

    /** Index of the first, and only, separator in {@link #text}. */
    private final int separator;

    /** The 48-bit millisecond count the value begins with. */
    private final long timestampMillis;

    /** The value's format. */
    private final IdGenerationStrategy format;

    private IdoraId(String text, int separator, long timestampMillis, IdGenerationStrategy format) {
        this.text = text;
        this.separator = separator;
        this.timestampMillis = timestampMillis;
        this.format = format;
    }

    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    /**
     * Parses an identifier, accepting any conforming prefix.
     *
     * <pre>{@code
     * IdoraId.parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c").prefix();   // "usr"
     * IdoraId.parse("usr-01k2f9v8ta7m3x0qbrn4dzeh5c");            // throws: no separator
     * }</pre>
     *
     * @param text the identifier, for example {@code usr_01k2f9v8ta7m3x0qbrn4dzeh5c}
     * @return the parsed identifier
     * @throws NullPointerException if {@code text} is {@code null}
     * @throws InvalidIdException if {@code text} is not a conforming identifier; its
     *     {@link InvalidIdException#getReason() reason} says which rule it broke
     */
    public static IdoraId parse(CharSequence text) {
        String string = Objects.requireNonNull(text, "text").toString();
        IdoraId id = read(string, null);
        if (id == null) {
            throw diagnose(string, null);
        }
        return id;
    }

    /**
     * Parses an identifier and requires it to carry exactly the given prefix, case included.
     *
     * <pre>{@code
     * IdoraId.parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c", "usr");   // fine
     * IdoraId.parse("ord_01k2f9v8ta7m3x0qbrn4dzeh5c", "usr");   // throws: PREFIX_MISMATCH
     * }</pre>
     *
     * <p>The text is checked fully before the prefix is compared, so a malformed identifier is
     * reported as malformed even when its prefix is also wrong. For an entity type registered with a
     * generator, {@link IdoraGenerator#parseId(Class, CharSequence)} looks the prefix up for you.
     *
     * @param text the identifier
     * @param expectedPrefix the prefix the identifier must carry, without the separator
     * @return the parsed identifier
     * @throws NullPointerException if {@code text} or {@code expectedPrefix} is {@code null}
     * @throws InvalidEntityPrefixException if {@code expectedPrefix} is not itself a valid prefix,
     *     which no identifier could carry
     * @throws InvalidIdException if {@code text} is not a conforming identifier, or carries a
     *     different prefix
     */
    public static IdoraId parse(CharSequence text, String expectedPrefix) {
        // The expected prefix first: it is the caller's, and an invalid one is reported as such
        // whatever the text is, even a missing one (specification section 6.1).
        EntityPrefixValidator.validate(expectedPrefix, "expectedPrefix");
        Objects.requireNonNull(text, "text");
        String string = text.toString();
        IdoraId id = read(string, expectedPrefix);
        if (id == null) {
            throw diagnose(string, expectedPrefix);
        }
        return id;
    }

    /**
     * Parses an identifier without throwing when it is malformed.
     *
     * <pre>{@code
     * IdoraId.tryParse(request.getParameter("user"))
     *     .map(IdoraId::timestamp)
     *     .orElse(null);
     * }</pre>
     *
     * <p>No exception is created for a rejected text, so this is the form to use on input that is
     * often malformed. A {@code null} text is still a programming error and is not treated as a
     * malformed identifier.
     *
     * @param text the identifier
     * @return the parsed identifier, or empty if {@code text} is not a conforming identifier
     * @throws NullPointerException if {@code text} is {@code null}
     */
    public static Optional<IdoraId> tryParse(CharSequence text) {
        return Optional.ofNullable(read(Objects.requireNonNull(text, "text").toString(), null));
    }

    /**
     * Parses an identifier that must carry exactly the given prefix, without throwing when it is
     * malformed or carries a different prefix.
     *
     * @param text the identifier
     * @param expectedPrefix the prefix the identifier must carry, without the separator
     * @return the parsed identifier, or empty if {@code text} is not a conforming identifier with
     *     that prefix
     * @throws NullPointerException if {@code text} or {@code expectedPrefix} is {@code null}
     * @throws InvalidEntityPrefixException if {@code expectedPrefix} is not itself a valid prefix
     */
    public static Optional<IdoraId> tryParse(CharSequence text, String expectedPrefix) {
        // The expected prefix first: it is the caller's, and an invalid one is reported as such
        // whatever the text is, even a missing one (specification section 6.1).
        EntityPrefixValidator.validate(expectedPrefix, "expectedPrefix");
        Objects.requireNonNull(text, "text");
        return Optional.ofNullable(read(text.toString(), expectedPrefix));
    }

    /**
     * Tells whether a text is a conforming identifier, with any prefix.
     *
     * @param text the text to check
     * @return {@code true} if {@link #parse(CharSequence)} would accept it
     * @throws NullPointerException if {@code text} is {@code null}
     */
    public static boolean isValid(CharSequence text) {
        return read(Objects.requireNonNull(text, "text").toString(), null) != null;
    }

    /**
     * Tells whether a text is a conforming identifier carrying exactly the given prefix.
     *
     * @param text the text to check
     * @param expectedPrefix the prefix the identifier must carry, without the separator
     * @return {@code true} if {@link #parse(CharSequence, String)} would accept it
     * @throws NullPointerException if {@code text} or {@code expectedPrefix} is {@code null}
     * @throws InvalidEntityPrefixException if {@code expectedPrefix} is not itself a valid prefix
     */
    public static boolean isValid(CharSequence text, String expectedPrefix) {
        // The expected prefix first: it is the caller's, and an invalid one is reported as such
        // whatever the text is, even a missing one (specification section 6.1).
        EntityPrefixValidator.validate(expectedPrefix, "expectedPrefix");
        Objects.requireNonNull(text, "text");
        return read(text.toString(), expectedPrefix) != null;
    }

    /**
     * Wraps an identifier this package has just generated, without checking it again. The
     * generators only ever emit conforming text, so a failure here is a bug in Idora itself.
     */
    static IdoraId ofGenerated(String text) {
        IdoraId id = read(text, null);
        if (id == null) {
            throw new IllegalStateException("Idora generated a non-conforming identifier: " + text);
        }
        return id;
    }

    // ---------------------------------------------------------------------------------------------
    // Members
    // ---------------------------------------------------------------------------------------------

    /**
     * Returns the prefix: the text before the separator, exactly as written.
     *
     * @return the prefix, for example {@code "usr"}; never empty
     */
    public String prefix() {
        return text.substring(0, separator);
    }

    /**
     * Returns the value: the ULID or UUIDv7 text after the separator.
     *
     * @return the value, for example {@code "01k2f9v8ta7m3x0qbrn4dzeh5c"}
     */
    public String value() {
        return text.substring(separator + 1);
    }

    /**
     * Returns the value's format, as the strategy that would generate it.
     *
     * @return {@link IdGenerationStrategy#ULID} for a 26-character value,
     *     {@link IdGenerationStrategy#UUID_V7} for a 32-character one
     */
    public IdGenerationStrategy format() {
        return format;
    }

    /**
     * Returns the instant the value was minted, at millisecond precision, in UTC.
     *
     * <p>Read from the value itself, so it does not depend on the clock or the time zone of the
     * reader.
     *
     * @return the instant; never before the Unix epoch
     */
    public Instant timestamp() {
        return Instant.ofEpochMilli(timestampMillis);
    }

    /**
     * Returns the 48-bit count of milliseconds since the Unix epoch that the value begins with.
     *
     * @return the timestamp, from {@code 0} to {@code 2^48 - 1}
     */
    public long timestampMillis() {
        return timestampMillis;
    }

    /**
     * Tells whether the identifier carries exactly the given prefix, comparing ordinally with case
     * included: {@code usr_...} has the prefix {@code "usr"}, not {@code "USR"} or {@code "us"}.
     *
     * @param prefix the prefix to test, without the separator
     * @return {@code true} if the identifier's prefix equals {@code prefix}
     * @throws NullPointerException if {@code prefix} is {@code null}
     */
    public boolean hasPrefix(String prefix) {
        Objects.requireNonNull(prefix, "prefix");
        return prefix.length() == separator && text.startsWith(prefix);
    }

    // ---------------------------------------------------------------------------------------------
    // Ordering
    // ---------------------------------------------------------------------------------------------

    /**
     * Compares in the natural order: by timestamp, then value, then prefix, each ordinally.
     *
     * @param other the identifier to compare with
     * @return a negative number, zero or a positive number as this identifier sorts before, equal
     *     to or after {@code other}
     * @throws NullPointerException if {@code other} is {@code null}
     */
    @Override
    public int compareTo(IdoraId other) {
        int byTime = Long.compare(timestampMillis, other.timestampMillis);
        if (byTime != 0) {
            return byTime;
        }
        int byValue = compareOrdinal(text, separator + 1, text.length(), other.text, other.separator + 1, other.text.length());
        if (byValue != 0) {
            return byValue;
        }
        return compareOrdinal(text, 0, separator, other.text, 0, other.separator);
    }

    /**
     * Tells whether this identifier sorts strictly before another in the natural order.
     *
     * @param other the identifier to compare with
     * @return {@code true} if {@code compareTo(other) < 0}
     * @throws NullPointerException if {@code other} is {@code null}
     */
    public boolean isBefore(IdoraId other) {
        return compareTo(Objects.requireNonNull(other, "other")) < 0;
    }

    /**
     * Tells whether this identifier sorts strictly after another in the natural order.
     *
     * @param other the identifier to compare with
     * @return {@code true} if {@code compareTo(other) > 0}
     * @throws NullPointerException if {@code other} is {@code null}
     */
    public boolean isAfter(IdoraId other) {
        return compareTo(Objects.requireNonNull(other, "other")) > 0;
    }

    /**
     * Compares the millisecond the two identifiers were minted in, and nothing else.
     *
     * @param other the identifier to compare with
     * @return a negative number, zero or a positive number as this identifier was minted in an
     *     earlier, the same or a later millisecond than {@code other}
     * @throws NullPointerException if {@code other} is {@code null}
     */
    public int compareTimestamp(IdoraId other) {
        return Long.compare(timestampMillis, Objects.requireNonNull(other, "other").timestampMillis);
    }

    /**
     * Tells whether the two identifiers were minted in the same millisecond.
     *
     * @param other the identifier to compare with
     * @return {@code true} if both carry the same timestamp
     * @throws NullPointerException if {@code other} is {@code null}
     */
    public boolean isSameMillisecond(IdoraId other) {
        return compareTimestamp(other) == 0;
    }

    /**
     * Tells whether this identifier was minted strictly before an instant, at millisecond
     * precision: the instant's sub-millisecond part is truncated before comparing, so an identifier
     * minted at {@code 12:00:00.001} is not created before {@code 12:00:00.001999}.
     *
     * @param instant the instant to compare with
     * @return {@code true} if the identifier's millisecond is earlier than the instant's
     * @throws NullPointerException if {@code instant} is {@code null}
     */
    public boolean isCreatedBefore(Instant instant) {
        return timestampMillis < floorMillis(Objects.requireNonNull(instant, "instant"));
    }

    /**
     * Tells whether this identifier was minted strictly after an instant, at millisecond
     * precision: the instant's sub-millisecond part is truncated before comparing.
     *
     * @param instant the instant to compare with
     * @return {@code true} if the identifier's millisecond is later than the instant's
     * @throws NullPointerException if {@code instant} is {@code null}
     */
    public boolean isCreatedAfter(Instant instant) {
        return timestampMillis > floorMillis(Objects.requireNonNull(instant, "instant"));
    }

    // ---------------------------------------------------------------------------------------------
    // Range bounds
    // ---------------------------------------------------------------------------------------------

    /**
     * Returns the lowest identifier with the given prefix that any conforming generator can mint in
     * the millisecond of {@code instant}, in the given format.
     *
     * <pre>{@code
     * IdoraId.lowerBound("usr", Instant.parse("2025-08-07T12:00:00Z"), IdGenerationStrategy.ULID);
     * // usr_01k226e0g00000000000000000
     * }</pre>
     *
     * <p>Compare it as a plain string against identifiers of the same prefix and format, as a
     * database column holds them. It is itself a valid identifier.
     *
     * @param prefix the prefix, validated as for {@link IdoraGenerator#generate(String)}
     * @param instant the instant; its sub-millisecond part is ignored
     * @param format the value format
     * @return the lowest identifier for that millisecond
     * @throws NullPointerException if any argument is {@code null}
     * @throws InvalidEntityPrefixException if {@code prefix} breaks the rules
     * @throws IllegalArgumentException if {@code instant} is before the Unix epoch or after the last
     *     millisecond a 48-bit timestamp can hold
     */
    public static IdoraId lowerBound(String prefix, Instant instant, IdGenerationStrategy format) {
        EntityPrefixValidator.validate(prefix, "prefix");
        return bound(prefix, toTimestampMillis(instant), format, false);
    }

    /**
     * Returns the lowest identifier with the given prefix that any conforming generator can mint in
     * the given millisecond, in the given format.
     *
     * @param prefix the prefix, validated as for {@link IdoraGenerator#generate(String)}
     * @param timestampMillis milliseconds since the Unix epoch, from {@code 0} to {@code 2^48 - 1}
     * @param format the value format
     * @return the lowest identifier for that millisecond
     * @throws NullPointerException if {@code prefix} or {@code format} is {@code null}
     * @throws InvalidEntityPrefixException if {@code prefix} breaks the rules
     * @throws IllegalArgumentException if {@code timestampMillis} is out of range
     */
    public static IdoraId lowerBound(String prefix, long timestampMillis, IdGenerationStrategy format) {
        EntityPrefixValidator.validate(prefix, "prefix");
        return bound(prefix, checkTimestamp(timestampMillis), format, false);
    }

    /**
     * Returns the highest identifier with the given prefix that any conforming generator can mint
     * in the millisecond of {@code instant}, in the given format.
     *
     * <pre>{@code
     * IdoraId.upperBound("usr", Instant.parse("2025-08-07T12:00:00Z"), IdGenerationStrategy.ULID);
     * // usr_01k226e0g0zzzzzzzzzzzzzzzz
     * }</pre>
     *
     * @param prefix the prefix, validated as for {@link IdoraGenerator#generate(String)}
     * @param instant the instant; its sub-millisecond part is ignored, so the bound covers the whole
     *     millisecond
     * @param format the value format
     * @return the highest identifier for that millisecond
     * @throws NullPointerException if any argument is {@code null}
     * @throws InvalidEntityPrefixException if {@code prefix} breaks the rules
     * @throws IllegalArgumentException if {@code instant} is before the Unix epoch or after the last
     *     millisecond a 48-bit timestamp can hold
     */
    public static IdoraId upperBound(String prefix, Instant instant, IdGenerationStrategy format) {
        EntityPrefixValidator.validate(prefix, "prefix");
        return bound(prefix, toTimestampMillis(instant), format, true);
    }

    /**
     * Returns the highest identifier with the given prefix that any conforming generator can mint
     * in the given millisecond, in the given format.
     *
     * @param prefix the prefix, validated as for {@link IdoraGenerator#generate(String)}
     * @param timestampMillis milliseconds since the Unix epoch, from {@code 0} to {@code 2^48 - 1}
     * @param format the value format
     * @return the highest identifier for that millisecond
     * @throws NullPointerException if {@code prefix} or {@code format} is {@code null}
     * @throws InvalidEntityPrefixException if {@code prefix} breaks the rules
     * @throws IllegalArgumentException if {@code timestampMillis} is out of range
     */
    public static IdoraId upperBound(String prefix, long timestampMillis, IdGenerationStrategy format) {
        EntityPrefixValidator.validate(prefix, "prefix");
        return bound(prefix, checkTimestamp(timestampMillis), format, true);
    }

    /**
     * Returns the lowest value, without a prefix, that any conforming generator can mint in the
     * millisecond of {@code instant}: the timestamp characters followed by the smallest random
     * field the format allows.
     *
     * @param instant the instant; its sub-millisecond part is ignored
     * @param format the value format
     * @return the lowest value, for example {@code "01k226e0g00000000000000000"}
     * @throws NullPointerException if any argument is {@code null}
     * @throws IllegalArgumentException if {@code instant} is out of range
     */
    public static String lowerBoundValue(Instant instant, IdGenerationStrategy format) {
        return boundValue(toTimestampMillis(instant), format, false);
    }

    /**
     * Returns the lowest value, without a prefix, that any conforming generator can mint in the
     * given millisecond.
     *
     * @param timestampMillis milliseconds since the Unix epoch, from {@code 0} to {@code 2^48 - 1}
     * @param format the value format
     * @return the lowest value
     * @throws NullPointerException if {@code format} is {@code null}
     * @throws IllegalArgumentException if {@code timestampMillis} is out of range
     */
    public static String lowerBoundValue(long timestampMillis, IdGenerationStrategy format) {
        return boundValue(checkTimestamp(timestampMillis), format, false);
    }

    /**
     * Returns the highest value, without a prefix, that any conforming generator can mint in the
     * millisecond of {@code instant}: the timestamp characters followed by the largest random field
     * the format allows.
     *
     * @param instant the instant; its sub-millisecond part is ignored
     * @param format the value format
     * @return the highest value, for example {@code "01k226e0g0zzzzzzzzzzzzzzzz"}
     * @throws NullPointerException if any argument is {@code null}
     * @throws IllegalArgumentException if {@code instant} is out of range
     */
    public static String upperBoundValue(Instant instant, IdGenerationStrategy format) {
        return boundValue(toTimestampMillis(instant), format, true);
    }

    /**
     * Returns the highest value, without a prefix, that any conforming generator can mint in the
     * given millisecond.
     *
     * @param timestampMillis milliseconds since the Unix epoch, from {@code 0} to {@code 2^48 - 1}
     * @param format the value format
     * @return the highest value
     * @throws NullPointerException if {@code format} is {@code null}
     * @throws IllegalArgumentException if {@code timestampMillis} is out of range
     */
    public static String upperBoundValue(long timestampMillis, IdGenerationStrategy format) {
        return boundValue(checkTimestamp(timestampMillis), format, true);
    }

    // ---------------------------------------------------------------------------------------------
    // Object
    // ---------------------------------------------------------------------------------------------

    /**
     * Two identifiers are equal when their texts are equal, ordinally, case included.
     *
     * @param other the object to compare with
     * @return {@code true} if {@code other} is an {@code IdoraId} with the same text
     */
    @Override
    public boolean equals(Object other) {
        return this == other || (other instanceof IdoraId && text.equals(((IdoraId) other).text));
    }

    /**
     * Returns the hash of the text, so that it follows {@link #equals(Object)}.
     *
     * @return the hash code
     */
    @Override
    public int hashCode() {
        return text.hashCode();
    }

    /**
     * Returns the identifier as text, exactly as it was parsed or generated.
     *
     * @return the identifier, for example {@code "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"}
     */
    @Override
    public String toString() {
        return text;
    }

    // ---------------------------------------------------------------------------------------------
    // Serialization: the text alone, parsed again on the way back in.
    // ---------------------------------------------------------------------------------------------

    /**
     * Serializes the identifier as its text, through {@link SerializedForm}.
     *
     * @return the serialized form
     */
    private Object writeReplace() {
        return new SerializedForm(text);
    }

    /**
     * Refuses to read an {@code IdoraId} that was not written through its serialized form, so that a
     * crafted stream cannot produce an instance that bypassed parsing.
     *
     * @param stream the stream
     * @throws InvalidObjectException always
     */
    private void readObject(ObjectInputStream stream) throws InvalidObjectException {
        throw new InvalidObjectException("IdoraId is deserialized through its serialized form");
    }

    /** The serialized form of an {@link IdoraId}: its text, and nothing that could disagree with it. */
    private static final class SerializedForm implements Serializable {

        private static final long serialVersionUID = 1L;

        /** The identifier's text. */
        private final String text;

        SerializedForm(String text) {
            this.text = text;
        }

        /**
         * Parses the text again, strictly.
         *
         * @return the identifier
         * @throws InvalidObjectException if the text is not a conforming identifier
         */
        private Object readResolve() throws InvalidObjectException {
            if (text == null) {
                throw new InvalidObjectException("The serialized IdoraId has no text");
            }
            IdoraId id = read(text, null);
            if (id == null) {
                InvalidIdException cause = diagnose(text, null);
                InvalidObjectException exception = new InvalidObjectException(cause.getMessage());
                exception.initCause(cause);
                throw exception;
            }
            return id;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Implementation
    // ---------------------------------------------------------------------------------------------

    /**
     * The single parsing pass, with no allocation beyond the result. Returns {@code null} for any
     * rejected text; {@link #diagnose} works out why, which only the failing path pays for.
     *
     * @param expectedPrefix the prefix to require, already validated, or {@code null} for any
     */
    private static IdoraId read(String text, String expectedPrefix) {
        int separator = text.indexOf(EntityPrefixValidator.SEPARATOR);
        if (separator < 0 || !isValidPrefix(text, separator)) {
            return null;
        }

        int valueStart = separator + 1;
        int valueLength = text.length() - valueStart;
        long timestamp;
        IdGenerationStrategy format;
        if (valueLength == ULID_LENGTH) {
            timestamp = ulidTimestamp(text, valueStart);
            format = IdGenerationStrategy.ULID;
        } else if (valueLength == UUID_V7_LENGTH) {
            timestamp = uuidV7Timestamp(text, valueStart);
            format = IdGenerationStrategy.UUID_V7;
        } else {
            return null;
        }
        if (timestamp < 0) {
            return null;
        }

        if (expectedPrefix != null && !(expectedPrefix.length() == separator && text.startsWith(expectedPrefix))) {
            return null;
        }
        return new IdoraId(text, separator, timestamp, format);
    }

    /** The prefix rules of specification section 2, without allocating. */
    private static boolean isValidPrefix(String text, int length) {
        if (length == 0 || length > EntityPrefixValidator.MAX_LENGTH) {
            return false;
        }
        for (int i = 0; i < length; i++) {
            if (!EntityPrefixValidator.isAsciiLetterOrDigit(text.charAt(i))) {
                return false;
            }
        }
        return true;
    }

    /**
     * Checks a 26-character ULID value and decodes its timestamp, or returns {@code -1} if any
     * character is outside the lowercase alphabet or the first exceeds {@code 7}.
     */
    private static long ulidTimestamp(String text, int start) {
        int first = CrockfordBase32.decode(text.charAt(start));
        if (first < 0 || first > 7) {
            return -1;
        }
        long timestamp = first;
        for (int i = 1; i < 10; i++) {
            int digit = CrockfordBase32.decode(text.charAt(start + i));
            if (digit < 0) {
                return -1;
            }
            timestamp = (timestamp << 5) | digit;
        }
        for (int i = 10; i < ULID_LENGTH; i++) {
            if (CrockfordBase32.decode(text.charAt(start + i)) < 0) {
                return -1;
            }
        }
        return timestamp;
    }

    /**
     * Checks a 32-character UUIDv7 value and decodes its timestamp, or returns {@code -1} if any
     * character is not lowercase hexadecimal, the version is not {@code 7} or the variant is not
     * RFC 9562's.
     */
    private static long uuidV7Timestamp(String text, int start) {
        if (text.charAt(start + 12) != '7' || !isVariant(text.charAt(start + 16))) {
            return -1;
        }
        long timestamp = 0;
        for (int i = 0; i < 12; i++) {
            int digit = hexDigit(text.charAt(start + i));
            if (digit < 0) {
                return -1;
            }
            timestamp = (timestamp << 4) | digit;
        }
        for (int i = 12; i < UUID_V7_LENGTH; i++) {
            if (hexDigit(text.charAt(start + i)) < 0) {
                return -1;
            }
        }
        return timestamp;
    }

    /** {@code 10xx}: the RFC 9562 variant, as the first hexadecimal digit of octet 8. */
    private static boolean isVariant(char c) {
        return c == '8' || c == '9' || c == 'a' || c == 'b';
    }

    /** A lowercase hexadecimal digit's value, or {@code -1}. */
    private static int hexDigit(char c) {
        if (c >= '0' && c <= '9') {
            return c - '0';
        }
        if (c >= 'a' && c <= 'f') {
            return c - 'a' + 10;
        }
        return -1;
    }

    /**
     * Works out which rule a rejected text broke, in the order the specification checks them, and
     * builds the exception that says so.
     */
    private static InvalidIdException diagnose(String text, String expectedPrefix) {
        int separator = text.indexOf(EntityPrefixValidator.SEPARATOR);
        if (separator < 0) {
            return InvalidIdException.noSeparator(text, expectedPrefix);
        }

        String prefix = text.substring(0, separator);
        try {
            EntityPrefixValidator.validate(prefix, "prefix");
        } catch (InvalidEntityPrefixException e) {
            return InvalidIdException.prefix(text, expectedPrefix, e);
        }

        String problem = valueProblem(text.substring(separator + 1));
        if (problem != null) {
            return InvalidIdException.value(text, expectedPrefix, problem);
        }
        return InvalidIdException.prefixMismatch(text, prefix, expectedPrefix);
    }

    /** Describes what is wrong with a value, or returns {@code null} if nothing is. */
    private static String valueProblem(String value) {
        int length = value.length();
        if (length == ULID_LENGTH) {
            for (int i = 0; i < length; i++) {
                char c = value.charAt(i);
                if (CrockfordBase32.decode(c) < 0) {
                    return "the 26-character value is a ULID, and its character " + InvalidIdException.describe(c)
                            + " at index " + i + " is not in the lowercase Crockford Base32 alphabet "
                            + CrockfordBase32.ALPHABET;
                }
            }
            if (CrockfordBase32.decode(value.charAt(0)) > 7) {
                return "the 26-character value is a ULID, and its first character '" + value.charAt(0)
                        + "' exceeds '7', which would need more than 128 bits";
            }
            return null;
        }
        if (length == UUID_V7_LENGTH) {
            for (int i = 0; i < length; i++) {
                char c = value.charAt(i);
                if (hexDigit(c) < 0) {
                    return "the 32-character value is a UUIDv7, and its character " + InvalidIdException.describe(c)
                            + " at index " + i + " is not a lowercase hexadecimal digit";
                }
            }
            if (value.charAt(12) != '7') {
                return "the 32-character value is a UUIDv7, but its version character (index 12) is '"
                        + value.charAt(12) + "' rather than '7'";
            }
            if (!isVariant(value.charAt(16))) {
                return "the 32-character value is a UUIDv7, but its variant character (index 16) is '"
                        + value.charAt(16) + "' rather than one of 8, 9, a or b";
            }
            return null;
        }
        if (length == 36 && value.indexOf('-') >= 0) {
            return "the value is a hyphenated UUID; Idora writes UUIDv7 values as 32 hexadecimal characters "
                    + "without hyphens";
        }
        return "the value is " + length + " characters long, but a ULID value is exactly 26 and a UUIDv7 value "
                + "exactly 32";
    }

    private static IdoraId bound(String prefix, long timestampMillis, IdGenerationStrategy format, boolean upper) {
        String value = boundValue(timestampMillis, format, upper);
        return new IdoraId(prefix + EntityPrefixValidator.SEPARATOR + value, prefix.length(), timestampMillis, format);
    }

    private static String boundValue(long timestampMillis, IdGenerationStrategy format, boolean upper) {
        Objects.requireNonNull(format, "format");
        if (format == IdGenerationStrategy.ULID) {
            return upper
                    ? CrockfordBase32.encodeUlid(timestampMillis, 0xFFFF, -1L)
                    : CrockfordBase32.encodeUlid(timestampMillis, 0, 0);
        }
        byte[] hex = new byte[UUID_V7_LENGTH];
        UuidV7.writeHex(timestampMillis, upper ? ALL_RANDOM_BITS : NO_RANDOM_BITS, 0, hex, 0);
        return new String(hex, StandardCharsets.US_ASCII);
    }

    private static long checkTimestamp(long timestampMillis) {
        if (timestampMillis < 0 || timestampMillis > MAX_TIMESTAMP) {
            throw new IllegalArgumentException("The timestamp " + timestampMillis + " is outside the range an Idora "
                    + "value can carry: 0 (the Unix epoch) to " + MAX_TIMESTAMP + " milliseconds (" + Instant.ofEpochMilli(MAX_TIMESTAMP)
                    + "). Pass a millisecond count in that range.");
        }
        return timestampMillis;
    }

    private static long toTimestampMillis(Instant instant) {
        Objects.requireNonNull(instant, "instant");
        long millis = floorMillis(instant);
        if (millis < 0 || millis > MAX_TIMESTAMP) {
            throw new IllegalArgumentException("The instant " + instant + " is outside the range an Idora value can "
                    + "carry: " + Instant.EPOCH + " to " + Instant.ofEpochMilli(MAX_TIMESTAMP) + ". Pass an instant in that "
                    + "range.");
        }
        return millis;
    }

    /**
     * The instant's millisecond, rounding down, saturating instead of overflowing for instants far
     * outside the range a timestamp can carry.
     */
    private static long floorMillis(Instant instant) {
        long seconds = instant.getEpochSecond();
        if (seconds > MAX_TIMESTAMP / 1000 + 1) {
            return Long.MAX_VALUE;
        }
        if (seconds < -(MAX_TIMESTAMP / 1000 + 1)) {
            return Long.MIN_VALUE;
        }
        return seconds * 1000 + instant.getNano() / 1_000_000;
    }

    /** Ordinal comparison of two regions, as {@link String#compareTo} would compare the substrings. */
    private static int compareOrdinal(String a, int aFrom, int aTo, String b, int bFrom, int bTo) {
        int aLength = aTo - aFrom;
        int bLength = bTo - bFrom;
        int common = Math.min(aLength, bLength);
        for (int i = 0; i < common; i++) {
            char x = a.charAt(aFrom + i);
            char y = b.charAt(bFrom + i);
            if (x != y) {
                return x - y;
            }
        }
        return aLength - bLength;
    }

    private static byte[] filled(byte value) {
        byte[] bytes = new byte[UuidV7.RANDOM_BYTES];
        Arrays.fill(bytes, value);
        return bytes;
    }
}
