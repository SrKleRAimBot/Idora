package io.github.srkleraimbot.idora.support;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.function.IntFunction;

import static org.junit.jupiter.api.Assumptions.assumeTrue;

/** Runs work on many threads at once and collects the results. */
public final class Concurrency {

    private Concurrency() {
    }

    /** At least two, so concurrency is real even on a single-core host. */
    public static int threadCount() {
        return Math.max(2, Runtime.getRuntime().availableProcessors());
    }

    /** Runs {@code tasks} copies of {@code task} on a fixed pool of platform threads. */
    public static <T> List<T> onPlatformThreads(int threads, int tasks, IntFunction<T> task) throws Exception {
        ExecutorService executor = Executors.newFixedThreadPool(threads);
        try {
            return runAll(executor, tasks, task);
        } finally {
            executor.shutdownNow();
            executor.awaitTermination(1, TimeUnit.MINUTES);
        }
    }

    /**
     * Runs {@code tasks} copies of {@code task}, each on its own new virtual thread. Skipped (as a
     * failed assumption) on Java 17 to 20, which have no virtual threads; the suite is compiled for
     * Java 17, so the executor is looked up reflectively.
     */
    public static <T> List<T> onVirtualThreads(int tasks, IntFunction<T> task) throws Exception {
        ExecutorService executor;
        try {
            executor = (ExecutorService) Executors.class.getMethod("newVirtualThreadPerTaskExecutor").invoke(null);
        } catch (NoSuchMethodException e) {
            assumeTrue(false, "virtual threads need Java 21+");
            throw new AssertionError(e);
        }
        try {
            return runAll(executor, tasks, task);
        } finally {
            executor.shutdownNow();
            executor.awaitTermination(1, TimeUnit.MINUTES);
        }
    }

    private static <T> List<T> runAll(ExecutorService executor, int tasks, IntFunction<T> task) throws Exception {
        List<Future<T>> futures = new ArrayList<>(tasks);
        for (int i = 0; i < tasks; i++) {
            int index = i;
            Callable<T> callable = () -> task.apply(index);
            futures.add(executor.submit(callable));
        }
        List<T> results = new ArrayList<>(tasks);
        for (Future<T> future : futures) {
            try {
                results.add(future.get(1, TimeUnit.MINUTES));
            } catch (ExecutionException e) {
                throw e.getCause() instanceof Exception ? (Exception) e.getCause() : e;
            }
        }
        return results;
    }
}
