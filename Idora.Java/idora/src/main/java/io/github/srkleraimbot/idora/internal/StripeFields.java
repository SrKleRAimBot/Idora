package io.github.srkleraimbot.idora.internal;

import java.lang.invoke.MethodHandles;
import java.lang.invoke.VarHandle;
import java.security.SecureRandom;

/**
 * The hot fields of a {@link Stripe} and its lock, separated from {@link Stripe} only so that the
 * tail padding declared there is laid out after them. See {@link StripeHeadPadding}.
 */
abstract class StripeFields extends MonotonicUlidState {

    /**
     * Size of each stripe's entropy block: entropy for about a hundred ULIDs or UUIDs. Large enough
     * that refilling from the operating system is negligible, small enough not to matter in memory.
     */
    static final int ENTROPY_BYTES = 1024;

    /**
     * Size of the buffer an identifier is assembled in. The longest identifier is a 16-character
     * prefix, the separator and a 32-character UUID: 49 bytes.
     */
    static final int SCRATCH_BYTES = 64;

    /** Spins before a contended {@link #lock()} starts yielding. */
    private static final int SPINS_BEFORE_YIELD = 64;

    private static final VarHandle LOCKED;

    static {
        try {
            LOCKED = MethodHandles.lookup().findVarHandle(StripeFields.class, "locked", int.class);
        } catch (ReflectiveOperationException e) {
            throw new ExceptionInInitializerError(e);
        }
    }

    /** 0 when free, 1 when held. Only accessed through {@link #LOCKED}. */
    private int locked;

    /** Index of the first byte of {@link #entropy} not yet handed out. Starts empty. */
    int entropyOffset = ENTROPY_BYTES;

    /**
     * Allocated before {@link #entropy} on purpose: this is written for every identifier, and the
     * kilobyte of rarely written entropy then sits between it and the next stripe's fields.
     */
    final byte[] scratch = new byte[SCRATCH_BYTES];

    /** The entropy block. Filled lazily, so a stripe no thread ever maps to costs no system call. */
    final byte[] entropy = new byte[ENTROPY_BYTES];

    /** Created lazily, on the first refill, for the same reason. */
    SecureRandom random;

    StripeFields() {
    }

    /**
     * Acquires the stripe. A test-and-set lock rather than {@code synchronized} or a
     * {@link java.util.concurrent.locks.ReentrantLock}:
     *
     * <ul>
     *   <li>{@code synchronized} pins a virtual thread to its carrier on Java 21;
     *   <li>the lock word lives inside the padded stripe, so acquiring it touches no other object
     *       (a {@code ReentrantLock} keeps its state in a separate, unpadded allocation);
     *   <li>the critical section is a few dozen nanoseconds and never blocks, other than the
     *       occasional entropy refill, so the uncontended single CAS is the case worth optimising.
     * </ul>
     *
     * <p>Under contention it spins briefly with {@link Thread#onSpinWait()}, then falls back to
     * {@link Thread#yield()}, which for a virtual thread hands the carrier to another virtual thread.
     */
    final void lock() {
        if (!LOCKED.weakCompareAndSetAcquire(this, 0, 1)) {
            lockContended();
        }
    }

    private void lockContended() {
        for (int spins = 0; ; spins++) {
            if ((int) LOCKED.getOpaque(this) == 0 && LOCKED.weakCompareAndSetAcquire(this, 0, 1)) {
                return;
            }
            if (spins < SPINS_BEFORE_YIELD) {
                Thread.onSpinWait();
            } else {
                Thread.yield();
            }
        }
    }

    /** Releases the stripe, publishing every write made while it was held. */
    final void unlock() {
        LOCKED.setRelease(this, 0);
    }
}
