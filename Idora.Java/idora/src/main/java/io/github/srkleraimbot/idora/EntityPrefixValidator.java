package io.github.srkleraimbot.idora;

import java.util.Objects;

/**
 * Enforces the rules an entity prefix must satisfy (specification section 2).
 *
 * <p>A single pass over the characters with no regular expression and no allocation on the success
 * path, because it runs on every {@link IdoraGenerator#generate(String)} call as well as at
 * registration.
 */
final class EntityPrefixValidator {

    /** The character that separates the prefix from the value. */
    static final char SEPARATOR = '_';

    /**
     * The longest prefix allowed: long enough for a readable abbreviation, short enough that the
     * prefix never dominates the identifier.
     */
    static final int MAX_LENGTH = 16;

    private EntityPrefixValidator() {
    }

    /**
     * Throws unless {@code prefix} is a valid entity prefix. Rules are checked in the order the
     * specification gives, and the first failure is reported.
     *
     * @param prefix the prefix to check
     * @param parameterName the caller's parameter name, for the {@link NullPointerException}
     * @throws NullPointerException if {@code prefix} is {@code null}
     * @throws InvalidEntityPrefixException if {@code prefix} is empty, too long, or contains anything
     *     other than an ASCII letter or digit
     */
    static void validate(String prefix, String parameterName) {
        Objects.requireNonNull(prefix, parameterName);

        int length = prefix.length();
        if (length == 0) {
            throw InvalidEntityPrefixException.empty();
        }
        if (length > MAX_LENGTH) {
            throw InvalidEntityPrefixException.tooLong(prefix);
        }
        for (int i = 0; i < length; i++) {
            if (!isAsciiLetterOrDigit(prefix.charAt(i))) {
                throw InvalidEntityPrefixException.badCharacter(prefix, i);
            }
        }
    }

    /**
     * An explicit ASCII range check. {@link Character#isLetterOrDigit(char)} is Unicode-aware and
     * would accept {@code é} or {@code ١}, which the specification rejects.
     */
    static boolean isAsciiLetterOrDigit(char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }

    /**
     * Folds a validated prefix to lower case for collision checks, touching only {@code A-Z}.
     *
     * <p>Not {@link String#toLowerCase()}, which uses the default locale: under a Turkish locale
     * {@code "I"} lower-cases to a dotless {@code "ı"}, so {@code "INV"} and {@code "inv"} would stop
     * colliding on Turkish machines and nowhere else.
     */
    static String foldCase(String prefix) {
        char[] folded = null;
        for (int i = 0; i < prefix.length(); i++) {
            char c = prefix.charAt(i);
            if (c >= 'A' && c <= 'Z') {
                if (folded == null) {
                    folded = prefix.toCharArray();
                }
                folded[i] = (char) (c + ('a' - 'A'));
            }
        }
        return folded == null ? prefix : new String(folded);
    }
}
