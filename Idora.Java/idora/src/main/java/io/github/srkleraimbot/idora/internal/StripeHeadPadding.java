package io.github.srkleraimbot.idora.internal;

/**
 * 128 bytes of padding laid out ahead of a {@link Stripe}'s hot fields.
 *
 * <p>HotSpot lays out a superclass's fields before its subclass's, so putting padding in a
 * superclass is the dependable way to keep the fields after it off any cache line that another
 * object, possibly another stripe written by another core, might share. Apple M-series and many
 * other ARM cores use 128-byte lines, hence sixteen longs rather than the usual eight.
 */
abstract class StripeHeadPadding {
    long p00, p01, p02, p03, p04, p05, p06, p07;
    long p08, p09, p10, p11, p12, p13, p14, p15;
}
