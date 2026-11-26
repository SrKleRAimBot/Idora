package io.github.srkleraimbot.idora;

import java.util.Locale;
import java.util.Optional;

/**
 * Thrown when a text is not a conforming identifier, or is one with a different prefix from the
 * one the caller asked for.
 *
 * <pre>{@code
 * try {
 *     IdoraId.parse("ord_01k2f9v8ta7m3x0qbrn4dzeh5c", "usr");
 * } catch (InvalidIdException e) {
 *     e.getReason();           // Reason.PREFIX_MISMATCH
 *     e.getText();             // "ord_01k2f9v8ta7m3x0qbrn4dzeh5c"
 *     e.getExpectedPrefix();   // Optional[usr]
 * }
 * }</pre>
 *
 * <p>Thrown by {@link IdoraId#parse(CharSequence)}, {@link IdoraId#parse(CharSequence, String)} and
 * {@link IdoraGenerator#parseId(Class, CharSequence)}. The rules are checked in the order of
 * {@link Reason}, and the first one broken is reported. When the prefix is at fault the
 * {@link InvalidEntityPrefixException} describing exactly how is attached as the
 * {@linkplain #getCause() cause}.
 *
 * <p>The identifier usually comes from outside (a URL, a message, a form field), so treat this as
 * bad input rather than a bug: in a web application it is a {@code 400} or a {@code 404}, not a
 * {@code 500}.
 */
public final class InvalidIdException extends IdoraException {

    private static final long serialVersionUID = 1L;

    /** Which rule a rejected identifier broke. The rules are checked in this order. */
    public enum Reason {
        /** The text contains no separator, {@code _}. */
        NO_SEPARATOR,
        /** The text before the first separator is not a valid prefix. */
        PREFIX,
        /** The text after the first separator is neither a lowercase ULID nor a lowercase UUIDv7. */
        VALUE,
        /** The identifier is well formed, but its prefix is not exactly the one expected. */
        PREFIX_MISMATCH
    }

    /** How much of a rejected text a message repeats; the full text is always on {@link #getText()}. */
    private static final int QUOTED_LENGTH = 64;

    /** The rejected text. */
    private final String text;
    /** The rule it broke. */
    private final Reason reason;
    /** The prefix the caller required, or {@code null}. */
    private final String expectedPrefix;

    private InvalidIdException(
            String message, String text, Reason reason, String expectedPrefix, InvalidEntityPrefixException cause) {
        super(message, cause);
        this.text = text;
        this.reason = reason;
        this.expectedPrefix = expectedPrefix;
    }

    /**
     * Returns the text that was rejected, exactly as supplied.
     *
     * @return the rejected text; never {@code null}, possibly empty
     */
    public String getText() {
        return text;
    }

    /**
     * Returns which rule the text broke.
     *
     * @return the reason; never {@code null}
     */
    public Reason getReason() {
        return reason;
    }

    /**
     * Returns the prefix the caller required, when the parse asked for one. It is present for every
     * {@link Reason#PREFIX_MISMATCH}, and for the other reasons when the failing call named a
     * prefix.
     *
     * @return the expected prefix, or empty if any prefix was acceptable
     */
    public Optional<String> getExpectedPrefix() {
        return Optional.ofNullable(expectedPrefix);
    }

    static InvalidIdException noSeparator(String text, String expectedPrefix) {
        String example = expectedPrefix == null ? "usr" : expectedPrefix;
        return new InvalidIdException(
                (text.isEmpty() ? "An identifier cannot be empty." : "The text " + quote(text) + " is not an identifier: "
                        + "it contains no separator '_'.")
                        + " An identifier is a prefix, an underscore and a value, for example \"" + example
                        + "_01k2f9v8ta7m3x0qbrn4dzeh5c\".",
                text,
                Reason.NO_SEPARATOR,
                expectedPrefix,
                null);
    }

    static InvalidIdException prefix(String text, String expectedPrefix, InvalidEntityPrefixException cause) {
        return new InvalidIdException(
                "The text " + quote(text) + " is not an identifier: the part before the first '_' is not a valid "
                        + "prefix. " + cause.getMessage(),
                text,
                Reason.PREFIX,
                expectedPrefix,
                cause);
    }

    static InvalidIdException value(String text, String expectedPrefix, String problem) {
        return new InvalidIdException(
                "The text " + quote(text) + " is not an identifier: " + problem + ". The value after the first '_' "
                        + "must be a 26-character lowercase ULID or a 32-character lowercase UUIDv7, exactly as "
                        + "generated; identifiers are not trimmed or case-folded.",
                text,
                Reason.VALUE,
                expectedPrefix,
                null);
    }

    static InvalidIdException prefixMismatch(String text, String prefix, String expectedPrefix) {
        boolean caseOnly = EntityPrefixValidator.foldCase(prefix).equals(EntityPrefixValidator.foldCase(expectedPrefix));
        return new InvalidIdException(
                "The identifier " + quote(text) + " has the prefix '" + prefix + "', but '" + expectedPrefix
                        + "' was expected" + (caseOnly ? " (prefixes are compared with case included)" : "")
                        + ". Pass an identifier of the expected entity type.",
                text,
                Reason.PREFIX_MISMATCH,
                expectedPrefix,
                null);
    }

    /** One character for a message, with control characters escaped rather than printed raw. */
    static String describe(char c) {
        return Character.isISOControl(c) || Character.isWhitespace(c)
                ? String.format(Locale.ROOT, "U+%04X", (int) c)
                : "'" + c + "' (" + String.format(Locale.ROOT, "U+%04X", (int) c) + ")";
    }

    /**
     * Quotes a rejected text for a message: escaped, so that input cannot forge log lines, and
     * shortened, so that an enormous input does not become an enormous message.
     */
    private static String quote(String text) {
        StringBuilder quoted = new StringBuilder(Math.min(text.length(), QUOTED_LENGTH) + 16).append('\'');
        int shown = Math.min(text.length(), QUOTED_LENGTH);
        for (int i = 0; i < shown; i++) {
            char c = text.charAt(i);
            if (Character.isISOControl(c)) {
                quoted.append(String.format(Locale.ROOT, "\\u%04x", (int) c));
            } else {
                quoted.append(c);
            }
        }
        if (shown < text.length()) {
            quoted.append("...");
        }
        quoted.append('\'');
        if (shown < text.length()) {
            quoted.append(" (").append(text.length()).append(" characters)");
        }
        return quoted.toString();
    }
}
