package io.github.srkleraimbot.idora;

/**
 * Selects which time-ordered value follows the prefix in a generated identifier.
 *
 * <p>Both give you the two properties that matter: a value that is unique without coordination,
 * and one that sorts by the time it was created. They differ mainly in length and in how they are
 * written down.
 *
 * @see IdoraGenerator.Builder#strategy(IdGenerationStrategy)
 */
public enum IdGenerationStrategy {

    /**
     * A 26-character lowercase ULID, for example {@code 01k2f9v8ta7m3x0qbrn4dzeh5c}. This is the
     * default.
     *
     * <p>A ULID holds the same 128 bits as a UUID but writes them in Crockford Base32, which needs
     * 26 characters instead of 32, and whose alphabet omits the letters people confuse when reading
     * identifiers aloud ({@code i l o u}). ULIDs generated on one thread are strictly increasing,
     * even within a single millisecond. Prefer this unless something forces your hand.
     */
    ULID,

    /**
     * A 32-character lowercase RFC 9562 version 7 UUID with no hyphens, for example
     * {@code 0195e7b4a1c87f3d9e2b6c4a8f01d5e3}.
     *
     * <p>Choose this when something outside your control expects a real UUID: a database column
     * typed {@code uuid}, a schema that validates the format, or another service that parses the
     * value. Insert the hyphens back (8-4-4-4-12) and {@link java.util.UUID#fromString(String)}
     * parses it, reporting {@link java.util.UUID#version()} {@code 7}. Values are ordered by
     * millisecond only; within one millisecond they are in no particular order.
     */
    UUID_V7
}
