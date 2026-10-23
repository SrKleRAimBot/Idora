package io.github.srkleraimbot.idora.unscanned;

import io.github.srkleraimbot.idora.EntityPrefix;

/**
 * Annotated types for the cases a whole-package scan cannot express: clashes, malformed prefixes
 * and ordering. No test scans this package whole (only its {@code invalid} and {@code clash}
 * sub-packages, individually); tests list these types explicitly with {@code registerEntities}.
 */
public final class UnscannedEntities {

    private UnscannedEntities() {
    }

    /** Claims {@code acc}. */
    @EntityPrefix("acc")
    public static final class AnnotatedAccount {
    }

    /**
     * Clashes with {@link AnnotatedAccount} on case alone. Its binary name sorts after
     * {@code AnnotatedAccount}'s, so it is always the one reported as the offender.
     */
    @EntityPrefix("ACC")
    public static final class AnnotatedAccrual {
    }

    /** Claims {@code cus}. */
    @EntityPrefix("cus")
    public static class AnnotatedCustomer {
    }

    /** Claims {@code shp}. */
    @EntityPrefix("shp")
    public static final class AnnotatedShipment {
    }

    /** Inherits from an annotated type, but the annotation is not inherited. */
    public static final class DerivedCustomer extends AnnotatedCustomer {
    }

    /** Carries no annotation. */
    public static final class Nothing {
    }

    /** Declares a prefix containing the separator. */
    @EntityPrefix("bad_prefix")
    public static final class WithSeparator {
    }

    /** Declares an empty prefix. */
    @EntityPrefix("")
    public static final class WithEmptyPrefix {
    }

    /** Declares a prefix over 16 characters. */
    @EntityPrefix("thisprefixisfartoolong")
    public static final class WithOverlongPrefix {
    }

    /** Declares a prefix with punctuation. */
    @EntityPrefix("usr!")
    public static final class WithBadCharacter {
    }
}
