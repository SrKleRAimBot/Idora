package io.github.srkleraimbot.idora.internal;

/**
 * The state of one stream of monotonic ULIDs, and the step that advances it (specification
 * section 3.1): the millisecond the previous value was stamped with, and its 80-bit random field.
 *
 * <p>The clock reading is a parameter rather than something this class reads, which keeps the
 * step a pure function of (state, now) plus a fresh random draw when one is needed. The awkward
 * cases (a second value in the same millisecond, a clock that steps backwards, an exhausted random
 * field) are therefore directly and deterministically testable, and are checked against the shared
 * conformance vectors.
 *
 * <p>Not thread-safe. {@link Stripe} is the production subclass and only advances it while holding
 * its lock.
 */
public abstract class MonotonicUlidState extends StripeHeadPadding {

    /** The largest value of the high random word, which holds bits 79..64. */
    static final long MAX_RANDOM_HIGH = 0xFFFFL;

    /**
     * Milliseconds since the Unix epoch of the previous value. Starts below any real reading, so
     * the first step always draws fresh randomness.
     */
    long timestampMs = Long.MIN_VALUE;

    /** Random bits 79..64 of the previous value, in the low 16 bits. */
    long randomHigh;

    /** Random bits 63..0 of the previous value. */
    long randomLow;

    /** Creates a state that has produced nothing yet. */
    protected MonotonicUlidState() {
    }

    /**
     * Advances to the next value for the clock reading {@code nowMs}.
     *
     * <ul>
     *   <li>{@code nowMs} later than the last timestamp: take it, and draw a fresh random field;
     *   <li>otherwise (the same millisecond, or a clock that stepped backwards): keep the last
     *       timestamp and add one to the 80-bit random field, carrying from the low word into the
     *       high word;
     *   <li>if that overflows 80 bits: borrow the next millisecond and draw a fresh random field.
     * </ul>
     *
     * <p>Every value from one state therefore sorts strictly after the one before it. A clock that
     * steps backwards (an NTP correction, a resumed VM snapshot) never yields an out-of-order
     * value; values made during that window carry a timestamp slightly ahead of the wall clock.
     *
     * @param nowMs the current time in milliseconds since the Unix epoch
     */
    public final void next(long nowMs) {
        if (nowMs > timestampMs) {
            timestampMs = nowMs;
            drawRandom();
            return;
        }

        long low = randomLow + 1;
        if (low != 0) {
            randomLow = low;
            return;
        }

        // The low 64 bits wrapped, so carry into the high 16.
        long high = randomHigh + 1;
        if (high <= MAX_RANDOM_HIGH) {
            randomHigh = high;
            randomLow = 0;
            return;
        }

        // 2^80 values in one millisecond. Unreachable in practice, but borrowing the next
        // millisecond keeps the stream monotonic instead of wrapping or stalling.
        timestampMs = timestampMs + 1;
        drawRandom();
    }

    /**
     * Draws a fresh 80-bit random field and stores it with {@link #setRandom(long, long)}.
     */
    protected abstract void drawRandom();

    /**
     * Stores a freshly drawn random field.
     *
     * @param high random bits 79..64; only the low 16 bits are kept
     * @param low random bits 63..0
     */
    protected final void setRandom(long high, long low) {
        randomHigh = high & MAX_RANDOM_HIGH;
        randomLow = low;
    }

    /**
     * Replaces the whole state, as if the previous value had been {@code (timestampMs, high, low)}.
     *
     * @param timestampMs the previous value's timestamp
     * @param high its random bits 79..64; only the low 16 bits are kept
     * @param low its random bits 63..0
     */
    public final void set(long timestampMs, long high, long low) {
        this.timestampMs = timestampMs;
        setRandom(high, low);
    }

    /**
     * Returns the current value's timestamp.
     *
     * @return milliseconds since the Unix epoch
     */
    public final long timestampMs() {
        return timestampMs;
    }

    /**
     * Returns the current value's random bits 79..64.
     *
     * @return a value between {@code 0} and {@code 0xFFFF}
     */
    public final long randomHigh() {
        return randomHigh;
    }

    /**
     * Returns the current value's random bits 63..0.
     *
     * @return the low random word
     */
    public final long randomLow() {
        return randomLow;
    }

    /**
     * Returns the current value, encoded.
     *
     * @return 26 lowercase Crockford Base32 characters
     */
    public final String encode() {
        return CrockfordBase32.encodeUlid(timestampMs, randomHigh, randomLow);
    }
}
