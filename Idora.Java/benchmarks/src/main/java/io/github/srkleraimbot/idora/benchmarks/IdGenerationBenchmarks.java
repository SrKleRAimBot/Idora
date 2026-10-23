package io.github.srkleraimbot.idora.benchmarks;

import io.github.srkleraimbot.idora.IdoraGenerator;
import org.openjdk.jmh.annotations.Benchmark;
import org.openjdk.jmh.annotations.BenchmarkMode;
import org.openjdk.jmh.annotations.Fork;
import org.openjdk.jmh.annotations.Measurement;
import org.openjdk.jmh.annotations.Mode;
import org.openjdk.jmh.annotations.OutputTimeUnit;
import org.openjdk.jmh.annotations.Scope;
import org.openjdk.jmh.annotations.State;
import org.openjdk.jmh.annotations.Threads;
import org.openjdk.jmh.annotations.Warmup;

import java.util.UUID;
import java.util.concurrent.TimeUnit;

/**
 * Identifier generation against {@link UUID#randomUUID()}, the JDK's nearest built-in identifier.
 *
 * <pre>
 * mvn -pl benchmarks -am package -DskipTests
 * java -jar benchmarks/target/benchmarks.jar -prof gc
 * </pre>
 *
 * <p>Run with {@code -prof gc} to see bytes allocated per operation ({@code gc.alloc.rate.norm}).
 */
@BenchmarkMode(Mode.AverageTime)
@OutputTimeUnit(TimeUnit.NANOSECONDS)
@State(Scope.Benchmark)
@Fork(1)
@Warmup(iterations = 3, time = 1)
@Measurement(iterations = 5, time = 1)
public class IdGenerationBenchmarks {

    /** The entity the registered-type benchmarks generate identifiers for. */
    public static final class Customer {
    }

    private final IdoraGenerator ulid = IdoraGenerator.builder().registerEntity(Customer.class, "cus").build();

    private final IdoraGenerator uuidV7 = IdoraGenerator.builder().useUuidV7().registerEntity(Customer.class, "cus").build();

    /** The baseline: a random (version 4) UUID, formatted with hyphens. */
    @Benchmark
    public String uuidRandomUuidToString() {
        return UUID.randomUUID().toString();
    }

    /** What people write by hand to get a prefixed identifier without a library. */
    @Benchmark
    public String prefixedRandomUuid() {
        return "cus_" + UUID.randomUUID();
    }

    /** A registered entity type, ULID value: the recommended path. */
    @Benchmark
    public String ulidGenerateClass() {
        return ulid.generate(Customer.class);
    }

    /** An ad-hoc prefix, ULID value: validates the prefix on every call. */
    @Benchmark
    public String ulidGenerateString() {
        return ulid.generate("cus");
    }

    /** A registered entity type, UUIDv7 value. */
    @Benchmark
    public String uuidV7GenerateClass() {
        return uuidV7.generate(Customer.class);
    }

    /** An ad-hoc prefix, UUIDv7 value. */
    @Benchmark
    public String uuidV7GenerateString() {
        return uuidV7.generate("cus");
    }

    /** The baseline from four threads at once, sharing the JDK's one SecureRandom. */
    @Benchmark
    @Threads(4)
    public String uuidRandomUuidToStringFourThreads() {
        return UUID.randomUUID().toString();
    }

    /** A registered entity type, ULID value, from four threads at once. */
    @Benchmark
    @Threads(4)
    public String ulidGenerateClassFourThreads() {
        return ulid.generate(Customer.class);
    }

    /**
     * The wall-clock read every identifier needs, alone, from as many threads as there are
     * processors. A floor for {@link #ulidGenerateClassMaxThreads()}: on some hosts the clock read
     * itself stops scaling once every core is busy.
     */
    @Benchmark
    @Threads(Threads.MAX)
    public long clockOnlyMaxThreads() {
        return System.currentTimeMillis();
    }

    /** A registered entity type, ULID value, from as many threads as there are processors. */
    @Benchmark
    @Threads(Threads.MAX)
    public String ulidGenerateClassMaxThreads() {
        return ulid.generate(Customer.class);
    }
}
