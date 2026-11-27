using BenchmarkDotNet.Attributes;
using Idora;

namespace Idora.Benchmarks;

/// <summary>
/// Measures the cost of reading an identifier back: parsing, checking and comparing.
/// </summary>
/// <remarks>
/// The baseline is <c>Guid.TryParse</c>, the nearest thing an application parses today. Parsing a
/// string should allocate nothing at all, since <see cref="IdoraId"/> wraps the string it was given.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class IdParsingBenchmarks
{
    private readonly string _guid = Guid.NewGuid().ToString();
    private readonly string _ulid = new IdoraGenerator().Generate("cus");
    private readonly string _uuidV7 = new IdoraGenerator(options => options.UseUuidV7()).Generate("cus");
    private readonly IdoraId _first = IdoraId.Parse(new IdoraGenerator().Generate("cus"));
    private readonly IdoraId _second = IdoraId.Parse(new IdoraGenerator().Generate("cus"));

    /// <summary>What an application typically parses today, for comparison.</summary>
    [Benchmark(Baseline = true, Description = "Guid.TryParse")]
    public bool GuidBaseline() => Guid.TryParse(_guid, out _);

    /// <summary>Parsing a ULID identifier, prefix and timestamp included.</summary>
    [Benchmark(Description = "IdoraId.Parse - ULID")]
    public IdoraId ParseUlid() => IdoraId.Parse(_ulid);

    /// <summary>Parsing a UUIDv7 identifier.</summary>
    [Benchmark(Description = "IdoraId.Parse - UUIDv7")]
    public IdoraId ParseUuidV7() => IdoraId.Parse(_uuidV7);

    /// <summary>The check a route constraint or validation attribute makes.</summary>
    [Benchmark(Description = "IdoraId.IsValid(text, prefix)")]
    public bool IsValidWithPrefix() => IdoraId.IsValid(_ulid, "cus");

    /// <summary>One natural-order comparison, the unit of cost when sorting.</summary>
    [Benchmark(Description = "IdoraId.CompareTo")]
    public int Compare() => _first.CompareTo(_second);
}
