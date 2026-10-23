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

    /**
     * Returns the bytes {@code action} allocates per call, as the difference between running it
     * {@code 2n} and {@code n} times. Subtracting cancels any fixed cost inside the window (a JIT
     * compilation, the meter's own boxing) and leaves only what scales with the number of calls.
     */
    public static double bytesPerCall(Supplier<?> action, int iterations) {
        for (int i = 0; i < 20_000; i++) {
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
