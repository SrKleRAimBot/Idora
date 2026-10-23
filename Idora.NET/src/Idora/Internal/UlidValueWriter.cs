namespace Idora.Internal;

/// <summary>
/// Writes a 26-character lowercase ULID.
/// </summary>
/// <remarks>
/// <para>
/// The monotonic state is <see cref="ThreadStaticAttribute">thread-static</see> rather than an
/// instance field. Two consequences follow, both intentional: generation never contends on a lock
/// however many threads are producing identifiers, and every generator on a given thread shares one
/// monotonic sequence, so identifiers stay ordered even in a process that has configured more than
/// one container.
/// </para>
/// </remarks>
internal sealed class UlidValueWriter : IIdValueWriter
{
    /// <summary>The single instance needed; the type holds no per-instance state.</summary>
    internal static readonly UlidValueWriter Instance = new();

    [ThreadStatic]
    private static MonotonicUlidState t_state;

    private UlidValueWriter()
    {
    }

    /// <inheritdoc />
    public int ValueLength => CrockfordBase32.UlidLength;

    /// <inheritdoc />
    public void Write(Span<char> destination) =>
        UlidGenerator.Next(ref t_state, UlidGenerator.CurrentTimestampMs(), destination);
}
