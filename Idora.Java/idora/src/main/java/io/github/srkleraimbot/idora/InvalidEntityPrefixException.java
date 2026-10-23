package io.github.srkleraimbot.idora;

import java.util.Locale;
import java.util.Optional;
import java.util.OptionalInt;

/**
 * Thrown when an entity prefix breaks the rules: it must be 1 to 16 characters long and contain
 * only ASCII letters ({@code A-Z}, {@code a-z}) and digits ({@code 0-9}).
 *
 * <pre>{@code
 * builder.registerEntity(User.class, "user_account");   // throws: contains the separator
 * builder.registerEntity(User.class, "");               // throws: empty
 * builder.registerEntity(User.class, "usr");            // fine
 * }</pre>
 *
 * <p>The underscore is excluded because it separates the prefix from the value; if a prefix could
 * contain one, an identifier could no longer be split back into its two parts unambiguously.
 * Non-ASCII letters and digits ({@code é}, {@code ١}) are rejected too.
 *
 * <p>Thrown while building a generator for an explicit registration or an {@link EntityPrefix}
 * annotation, and on every {@link IdoraGenerator#generate(String)} call given a bad ad-hoc prefix.
 * When the prefix came from an annotation, which leaves no line of your code in the stack trace,
 * the message names the annotated type and {@link #getEntityType()} returns it.
 */
public final class InvalidEntityPrefixException extends IdoraException {

    private static final long serialVersionUID = 1L;

    /** Which rule a rejected prefix broke. The rules are checked in this order. */
    public enum Reason {
        /** The prefix is the empty string. */
        EMPTY,
        /** The prefix is longer than 16 characters. */
        TOO_LONG,
        /** The prefix contains the separator, {@code _}. */
        SEPARATOR,
        /** The prefix contains a character that is not an ASCII letter or digit. */
        CHARACTER
    }

    /** No offending index, for the reasons that have none. Never exposed; see {@link #getIndex()}. */
    private static final int NO_INDEX = -1;

    /** The rejected prefix. */
    private final String prefix;
    /** The rule it broke. */
    private final Reason reason;
    /** The offending index, or {@code NO_INDEX}. */
    private final int index;
    /** The annotated type that declared it, or {@code null}. */
    private final Class<?> entityType;

    private InvalidEntityPrefixException(
            String message, String prefix, Reason reason, int index, Class<?> entityType) {
        super(message);
        this.prefix = prefix;
        this.reason = reason;
        this.index = index;
        this.entityType = entityType;
    }

    /**
     * Returns the prefix that was rejected, exactly as supplied.
     *
     * @return the rejected prefix; never {@code null}
     */
    public String getPrefix() {
        return prefix;
    }

    /**
     * Returns which rule the prefix broke.
     *
     * @return the reason; never {@code null}
     */
    public Reason getReason() {
        return reason;
    }

    /**
     * Returns the zero-based index of the first offending character, for {@link Reason#SEPARATOR}
     * and {@link Reason#CHARACTER}. The index counts UTF-16 code units, as {@link String#charAt}
     * does.
     *
     * @return the index, or empty for {@link Reason#EMPTY} and {@link Reason#TOO_LONG}
     */
    public OptionalInt getIndex() {
        return index == NO_INDEX ? OptionalInt.empty() : OptionalInt.of(index);
    }

    /**
     * Returns the type whose {@link EntityPrefix} annotation declared the prefix, when it came from
     * one.
     *
     * @return the annotated type, or empty for an explicit registration or an ad-hoc prefix
     */
    public Optional<Class<?>> getEntityType() {
        return Optional.ofNullable(entityType);
    }

    static InvalidEntityPrefixException empty() {
        return new InvalidEntityPrefixException(
                "An entity prefix cannot be empty. Supply 1 to " + EntityPrefixValidator.MAX_LENGTH
                        + " ASCII letters or digits, for example \"usr\".",
                "",
                Reason.EMPTY,
                NO_INDEX,
                null);
    }

    static InvalidEntityPrefixException tooLong(String prefix) {
        return new InvalidEntityPrefixException(
                "The entity prefix '" + prefix + "' is " + prefix.length() + " characters long; the maximum is "
                        + EntityPrefixValidator.MAX_LENGTH + ". Use a shorter abbreviation of ASCII letters "
                        + "or digits, for example \"usr\".",
                prefix,
                Reason.TOO_LONG,
                NO_INDEX,
                null);
    }

    static InvalidEntityPrefixException badCharacter(String prefix, int index) {
        char character = prefix.charAt(index);
        if (character == EntityPrefixValidator.SEPARATOR) {
            return new InvalidEntityPrefixException(
                    "The entity prefix '" + prefix + "' contains the separator '_' at index " + index
                            + ". The separator divides the prefix from the value, so a prefix cannot contain "
                            + "one. Remove it, for example \"" + withoutSeparators(prefix) + "\".",
                    prefix,
                    Reason.SEPARATOR,
                    index,
                    null);
        }

        int codePoint = prefix.codePointAt(index);
        return new InvalidEntityPrefixException(
                "The entity prefix '" + prefix + "' contains the character '" + printable(codePoint) + "' ("
                        + String.format(Locale.ROOT, "U+%04X", codePoint) + ") at index " + index
                        + ". Prefixes may contain only ASCII letters and digits.",
                prefix,
                Reason.CHARACTER,
                index,
                null);
    }

    /**
     * Restates a complaint about a prefix declared with {@link EntityPrefix}, naming the type that
     * carries the annotation. A bad explicit registration leaves a stack trace pointing at the line
     * that made it; an annotation leaves nothing, so the type is the only useful pointer.
     */
    static InvalidEntityPrefixException forAnnotation(Class<?> entityType, InvalidEntityPrefixException detail) {
        return new InvalidEntityPrefixException(
                "The @EntityPrefix annotation on '" + entityType.getName()
                        + "' does not declare a valid prefix. " + detail.getMessage(),
                detail.prefix,
                detail.reason,
                detail.index,
                entityType);
    }

    private static String withoutSeparators(String prefix) {
        String stripped = prefix.replace("_", "");
        return stripped.isEmpty() ? "usr" : stripped;
    }

    /** Shows control characters as their escape rather than printing them raw into a log line. */
    private static String printable(int codePoint) {
        if (Character.isISOControl(codePoint)) {
            return String.format(Locale.ROOT, "\\u%04x", codePoint);
        }
        return new String(Character.toChars(codePoint));
    }
}
