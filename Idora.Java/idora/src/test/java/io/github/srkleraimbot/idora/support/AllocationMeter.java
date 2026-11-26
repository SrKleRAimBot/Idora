package io.github.srkleraimbot.idora.support;

import java.lang.reflect.Method;
import java.util.function.Supplier;

/**
 * Measures the bytes the current thread allocates, through HotSpot's
 * {@code com.sun.management.ThreadMXBean}.
 *
 * <p>Reached reflectively because the tests run patched into the library's module, which does not
 * (and must not) read {@code java.management}; core reflection does not need readability.
 */
public final class AllocationMeter {

    private static final Object THREAD_BEAN;
    private static final Method ALLOCATED_BYTES;

    /** Results are written here so the JIT cannot prove them dead and elide the allocation. */
    public static volatile Object sink;

    static {
        try {
            THREAD_BEAN = Class.forName("java.lang.management.ManagementFactory").getMethod("getThreadMXBean").invoke(null);
            ALLOCATED_BYTES = Class.forName("com.sun.management.ThreadMXBean").getMethod("getCurrentThreadAllocatedBytes");
        } catch (ReflectiveOperationException e) {
            throw new ExceptionInInitializerError(e);
        }
    }

    private AllocationMeter() {
    }

    /** Calls made before measuring, enough for the JIT to compile a path taken on every call. */
    private static final int DEFAULT_WARM_UP = 20_000;

    /**
     * Returns the bytes {@code action} allocates per call, as the difference between running it
     * {@code 2n} and {@code n} times. Subtracting cancels any fixed cost inside the window (a JIT
     * compilation, the meter's own boxing) and leaves only what scales with the number of calls.
     */
    public static double bytesPerCall(Supplier<?> action, int iterations) {
        return bytesPerCall(action, iterations, DEFAULT_WARM_UP);
    }

    /**
     * As {@link #bytesPerCall(Supplier, int)}, after {@code warmUp} unmeasured calls.
     *
     * <p>Subtraction cancels a fixed cost, but not code that is still running unoptimised in both
     * windows. A path taken only on a fraction of calls needs a longer warm-up to be compiled: the
     * entropy refill runs once per hundred or so identifiers, and until C2 compiles the JDK's DRBG
     * with escape analysis, each 1 KiB refill allocates about a kilobyte of temporaries rather than
     * about two hundred bytes.
     */
    public static double bytesPerCall(Supplier<?> action, int iterations, int warmUp) {
        for (int i = 0; i < warmUp; i++) {
            sink = action.get();
        }
        long single = total(action, iterations);
        long doubled = total(action, iterations * 2);
        return (double) (doubled - single) / iterations;
    }

    private static long total(Supplier<?> action, int iterations) {
        long before = allocatedBytes();
        for (int i = 0; i < iterations; i++) {
            sink = action.get();
        }
        return allocatedBytes() - before;
    }

    private static long allocatedBytes() {
        try {
            return (Long) ALLOCATED_BYTES.invoke(THREAD_BEAN);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException(e);
        }
    }
}
