using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Idora;

namespace Idora.Benchmarks;

/// <summary>
/// Measures the cost of generating one identifier against the obvious alternatives.
/// </summary>
/// <remarks>
/// The baseline is <c>Guid.NewGuid().ToString()</c>, which is what most applications reach for when
/// they need an identifier. The target for every method here is to come in under that, at exactly
/// one allocation.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class IdGenerationBenchmarks
{
    private readonly IIdoraGenerator _ulidGenerator =
        new IdoraGenerator(options => options.UseUlid().RegisterEntity<Customer>("cus"));

    private readonly IIdoraGenerator _uuidGenerator =
        new IdoraGenerator(options => options.UseUuidV7().RegisterEntity<Customer>("cus"));

    /// <summary>What an application typically does today, for comparison.</summary>
    [Benchmark(Baseline = true, Description = "Guid.NewGuid().ToString()")]
    public string GuidBaseline() => Guid.NewGuid().ToString();

    /// <summary>A prefixed UUIDv7 built by hand, showing what the naive version costs.</summary>
    [Benchmark(Description = "Naive string concat + UUIDv7")]
    public string NaiveConcatenation() => "cus_" + Guid.CreateVersion7().ToString("N");

    /// <summary>The framework's UUIDv7, which reaches for the system CSPRNG on every call.</summary>
    [Benchmark(Description = "Guid.CreateVersion7()")]
    public Guid FrameworkUuidV7() => Guid.CreateVersion7();

    /// <summary>This package's UUIDv7, drawing from the buffered per-thread entropy pool.</summary>
    [Benchmark(Description = "UuidV7.CreateAt() - buffered entropy")]
    public Guid BufferedUuidV7() => Idora.Internal.UuidV7.CreateAt(1_700_000_000_000);

    /// <summary>The registered-entity path: a frozen dictionary lookup plus one string.</summary>
    [Benchmark(Description = "Generate<T>() - ULID")]
    public string GenerateUlidForRegisteredEntity() => _ulidGenerator.Generate<Customer>();

    /// <summary>The ad-hoc path: prefix validation plus one string.</summary>
    [Benchmark(Description = "Generate(prefix) - ULID")]
    public string GenerateUlidForAdHocPrefix() => _ulidGenerator.Generate("cus");

    /// <summary>The registered-entity path with UUIDv7 values.</summary>
    [Benchmark(Description = "Generate<T>() - UUIDv7")]
    public string GenerateUuidForRegisteredEntity() => _uuidGenerator.Generate<Customer>();

    /// <summary>The ad-hoc path with UUIDv7 values.</summary>
    [Benchmark(Description = "Generate(prefix) - UUIDv7")]
    public string GenerateUuidForAdHocPrefix() => _uuidGenerator.Generate("cus");

    /// <summary>A stand-in entity type.</summary>
    private sealed class Customer;
}

/// <summary>Entry point for the benchmark run.</summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
