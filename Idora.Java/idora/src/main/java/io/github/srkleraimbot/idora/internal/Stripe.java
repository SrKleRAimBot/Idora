package io.github.srkleraimbot.idora.internal;

import java.lang.invoke.MethodHandles;
import java.lang.invoke.VarHandle;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;

/**
 * One stripe of the process-wide generation state: a monotonic ULID stream, a 1 KiB block of
 * {@link SecureRandom} entropy, a scratch buffer to assemble identifiers in, and a short lock
 * guarding all three.
 *
 * <h2>Why stripes, not thread-locals</h2>
 *
 * <p>A {@link ThreadLocal} would give each thread its own state with no locking at all, which is
 * what Idora.NET does with {@code [ThreadStatic]}. It is the wrong trade on Java 21+: a virtual
 * thread is cheap to create and often lives for one request, and each one would allocate and seed
 * its own kilobyte of entropy and its own {@link SecureRandom} to produce perhaps a single
 * identifier. Instead there is a fixed array of stripes, sized from
 * {@link Runtime#availableProcessors()} and created once, and each thread is mapped to one of them
 * by its thread id. Platform and virtual threads cost the same, and memory is bounded however many
 * threads there are.
 *
 * <h2>Monotonicity</h2>
 *
 * <p>A thread's id never changes, so a thread always maps to the same stripe, and every ULID it
 * generates sorts strictly after the previous one, across every generator in the process. Threads
 * that share a stripe share its stream, which is still strictly monotonic in the order the lock was
 * taken. Across stripes, ordering is by millisecond only.
 *
 * <h2>Allocation</h2>
 *
 * <p>The identifier is assembled in the stripe's scratch buffer while the lock is held and copied
 * once into the returned {@link String}, a compact Latin-1 string, so a call allocates the string
 * and its backing array and nothing else. The only other allocation is inside {@link SecureRandom}
 * when a block is refilled: about 200 bytes per kilobyte of entropy, roughly 2 bytes per UUIDv7
 * amortised, and less for ULIDs, which only draw fresh randomness once per millisecond.
 */
public final class Stripe extends StripeFields {

    /** The most stripes created however many processors there are; about 300 KiB. */
    static final int MAX_STRIPES = 256;

    private static final Stripe[] STRIPES;
    private static final int MASK;

    private static final VarHandle LONG_BIG_ENDIAN = MethodHandles.byteArrayViewVarHandle(long[].class, ByteOrder.BIG_ENDIAN);

    static {
        int count = stripeCountFor(Runtime.getRuntime().availableProcessors());
        Stripe[] stripes = new Stripe[count];
        for (int i = 0; i < count; i++) {
            stripes[i] = new Stripe();
        }
        STRIPES = stripes;
        MASK = count - 1;
    }

    // Tail padding; see StripeHeadPadding. Declared in the subclass so that it follows the hot
    // fields declared in StripeFields.
    long q00, q01, q02, q03, q04, q05, q06, q07;
    long q08, q09, q10, q11, q12, q13, q14, q15;

    /**
     * Creates a stand-alone stripe, outside the shared pool. Generators never do this; it exists so
     * that tests can exercise one stripe in isolation.
     */
    public Stripe() {
    }

    /**
     * Returns the calling thread's stripe.
     *
     * <p>Selected by {@link Thread#getId()}, not {@code Thread.threadId()}: the library targets Java
     * 17, where {@code threadId()} does not exist, and against the Java 17 API {@code getId()} is
     * not deprecated. From Java 19 it is a plain delegate to {@code threadId()}. Either way the id is
     * fixed for the life of the thread, which is the property monotonicity relies on, and ids are
     * handed out sequentially, so concurrently running threads tend to land on different stripes.
     * A {@code ThreadLocal<Stripe>} cache was rejected: it would allocate a map entry per virtual
     * thread, which is the cost striping exists to avoid.
     *
     * @return the stripe for the current thread
     */
    public static Stripe current() {
        return STRIPES[(int) Thread.currentThread().getId() & MASK];
    }

    /**
     * Returns how many stripes the shared pool has.
     *
     * @return a power of two between 2 and {@value #MAX_STRIPES}
     */
    public static int count() {
        return STRIPES.length;
    }

    /**
     * Returns how many stripes of the shared pool have created their {@link SecureRandom}. Never
     * more than {@link #count()}, however many threads have generated identifiers.
     *
     * @return the number of seeded stripes
     */
    public static int seededCount() {
        int seeded = 0;
        for (Stripe stripe : STRIPES) {
            stripe.lock();
            try {
                if (stripe.random != null) {
                    seeded++;
                }
            } finally {
                stripe.unlock();
            }
        }
        return seeded;
    }

    /**
     * Chooses the pool size: twice the processor count, rounded up to a power of two so a mask can
     * replace a division, and capped. Twice rather than once because thread ids are not dense, and
     * the extra stripes make two busy threads sharing one less likely.
     *
     * @param processors the number of available processors
     * @return the number of stripes
     */
    static int stripeCountFor(int processors) {
        int target = Math.min(MAX_STRIPES, Math.max(1, processors) * 2);
        return Integer.highestOneBit(target - 1) << 1;
    }

    /**
     * Generates a ULID identifier for a prefix that already ends with the separator.
     *
     * @param prefixWithSeparator ASCII bytes of a validated prefix followed by {@code _}
     * @return the identifier
     */
    public String ulid(byte[] prefixWithSeparator) {
        // Read the clock before taking the lock, to keep the critical section short. A reading that
        // is stale by the time the lock is held is harmless: next() treats it like a clock that
        // stepped backwards.
        long now = System.currentTimeMillis();
        int length = prefixWithSeparator.length;
        lock();
        try {
            byte[] buffer = scratch;
            System.arraycopy(prefixWithSeparator, 0, buffer, 0, length);
            next(now);
            CrockfordBase32.encodeUlid(timestampMs, randomHigh, randomLow, buffer, length);
            return new String(buffer, 0, length + CrockfordBase32.ULID_LENGTH, StandardCharsets.ISO_8859_1);
        } finally {
            unlock();
        }
    }

    /**
     * Generates a ULID identifier for a validated ad-hoc prefix, adding the separator.
     *
     * @param prefix a validated prefix, 1 to 16 ASCII letters or digits
     * @return the identifier
     */
    public String ulid(String prefix) {
        long now = System.currentTimeMillis();
        int length = prefix.length();
        lock();
        try {
            byte[] buffer = scratch;
            copyPrefix(prefix, length, buffer);
            next(now);
            CrockfordBase32.encodeUlid(timestampMs, randomHigh, randomLow, buffer, length + 1);
            return new String(buffer, 0, length + 1 + CrockfordBase32.ULID_LENGTH, StandardCharsets.ISO_8859_1);
        } finally {
            unlock();
        }
    }

    /**
     * Generates a UUIDv7 identifier for a prefix that already ends with the separator.
     *
     * @param prefixWithSeparator ASCII bytes of a validated prefix followed by {@code _}
     * @return the identifier
     */
    public String uuidV7(byte[] prefixWithSeparator) {
        long now = System.currentTimeMillis();
        int length = prefixWithSeparator.length;
        lock();
        try {
            byte[] buffer = scratch;
            System.arraycopy(prefixWithSeparator, 0, buffer, 0, length);
            UuidV7.writeHex(now, entropy, reserve(UuidV7.RANDOM_BYTES), buffer, length);
            return new String(buffer, 0, length + UuidV7.HEX_LENGTH, StandardCharsets.ISO_8859_1);
        } finally {
            unlock();
        }
    }

    /**
     * Generates a UUIDv7 identifier for a validated ad-hoc prefix, adding the separator.
     *
     * @param prefix a validated prefix, 1 to 16 ASCII letters or digits
     * @return the identifier
     */
    public String uuidV7(String prefix) {
        long now = System.currentTimeMillis();
        int length = prefix.length();
        lock();
        try {
            byte[] buffer = scratch;
            copyPrefix(prefix, length, buffer);
            UuidV7.writeHex(now, entropy, reserve(UuidV7.RANDOM_BYTES), buffer, length + 1);
            return new String(buffer, 0, length + 1 + UuidV7.HEX_LENGTH, StandardCharsets.ISO_8859_1);
        } finally {
            unlock();
        }
    }

    /**
     * Fills {@code destination} with bytes from this stripe's entropy block, refilling it from
     * {@link SecureRandom} when it runs out. Requests larger than the block bypass it.
     *
     * @param destination the array to fill; may be empty
     */
    public void nextBytes(byte[] destination) {
        int length = destination.length;
        lock();
        try {
            if (length > ENTROPY_BYTES) {
                secureRandom().nextBytes(destination);
            } else {
                System.arraycopy(entropy, reserve(length), destination, 0, length);
            }
        } finally {
            unlock();
        }
    }

    /** Draws 80 bits from the entropy block for a new ULID random field. Caller holds the lock. */
    @Override
    protected void drawRandom() {
        int offset = reserve(10);
        byte[] bytes = entropy;
        // The bytes are uniformly random, so which ten form which word is arbitrary.
        setRandom(((bytes[offset] & 0xFF) << 8) | (bytes[offset + 1] & 0xFF), (long) LONG_BIG_ENDIAN.get(bytes, offset + 2));
    }

    /**
     * Reserves {@code count} bytes of the entropy block, refilling it first if too few remain.
     * Caller holds the lock.
     *
     * @return the offset of the reserved bytes in {@link #entropy}
     */
    private int reserve(int count) {
        int offset = entropyOffset;
        if (offset > ENTROPY_BYTES - count) {
            secureRandom().nextBytes(entropy);
            offset = 0;
        }
        entropyOffset = offset + count;
        return offset;
    }

    private SecureRandom secureRandom() {
        SecureRandom r = random;
        if (r == null) {
            r = newSecureRandom();
            random = r;
        }
        return r;
    }

    /**
     * Creates the generator a stripe refills its block from: the JDK's {@code DRBG} (NIST SP
     * 800-90A Hash_DRBG over SHA-256, seeded from the operating system's entropy source), falling
     * back to the platform default where a security configuration has removed it.
     *
     * <p>Not {@code new SecureRandom()}, which on Linux and macOS is {@code NativePRNG}: measured on
     * JDK 21, refilling 1 KiB from it takes about 5 microseconds and allocates about 3 KiB of
     * garbage (it XORs {@code /dev/urandom} with a SHA1PRNG that allocates a digest per 20 bytes),
     * against about 1.4 microseconds and a fixed 200 bytes for DRBG. Both are cryptographically
     * strong; DRBG simply makes the refill an order of magnitude cheaper.
     *
     * @return a new, self-seeding cryptographically strong generator
     */
    static SecureRandom newSecureRandom() {
        try {
            return SecureRandom.getInstance("DRBG");
        } catch (NoSuchAlgorithmException e) {
            return new SecureRandom();
        }
    }

    /** Copies a validated, therefore ASCII, prefix and appends the separator. */
    private static void copyPrefix(String prefix, int length, byte[] buffer) {
        for (int i = 0; i < length; i++) {
            buffer[i] = (byte) prefix.charAt(i);
        }
        buffer[length] = '_';
    }
}
