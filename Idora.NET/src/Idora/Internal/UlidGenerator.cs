using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Idora.Internal;

/// <summary>
/// The mutable state a single stream of monotonic ULIDs needs: the millisecond the last identifier
/// was stamped with, and the 80-bit random field that accompanied it.
/// </summary>
/// <remarks>
/// This is a mutable struct, deliberately. It is only ever used as a
/// <see cref="ThreadStaticAttribute">thread-static</see> field accessed by reference, which keeps
/// the whole monotonic counter in a handful of machine words with no allocation and no sharing
/// between threads.
/// </remarks>
internal struct MonotonicUlidState
{
    /// <summary>Milliseconds since the Unix epoch that the previous identifier was stamped with.</summary>
    public long LastTimestampMs;

    /// <summary>Random bits 79..64 of the previous identifier, held in the low 16 bits.</summary>
    public ulong RandomHigh;

    /// <summary>Random bits 63..0 of the previous identifier.</summary>
    public ulong RandomLow;
}

/// <summary>
/// Produces monotonically increasing ULIDs from a caller-supplied clock reading.
/// </summary>
/// <remarks>
/// <para>
/// The timestamp is a parameter rather than something this type reads for itself, which keeps the
/// generation logic a pure function of (state, time). That makes the awkward cases &#8212; a second
/// identifier inside the same millisecond, a clock that steps backwards, an exhausted counter
/// &#8212; directly and deterministically testable instead of dependent on timing luck.
/// </para>
/// </remarks>
internal static class UlidGenerator
{
    /// <summary>The largest value <see cref="MonotonicUlidState.RandomHigh"/> can hold (16 bits).</summary>
    private const ulong MaxRandomHigh = 0xFFFF;

    /// <summary><see cref="DateTime.UnixEpoch"/> expressed in ticks, as a compile-time constant.</summary>
    private const long UnixEpochTicks = 621_355_968_000_000_000L;

    /// <summary>
    /// Reads the current time as milliseconds since the Unix epoch.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime.UtcNow"/> followed by an integer division the JIT turns into a multiply
    /// is measurably cheaper than <see cref="DateTimeOffset.UtcNow"/> plus
    /// <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/>, and this sits on the hot path of every
    /// identifier generated.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long CurrentTimestampMs() =>
        (DateTime.UtcNow.Ticks - UnixEpochTicks) / TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// Advances <paramref name="state"/> and writes the resulting ULID into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="state">
    /// The caller's monotonic state, updated in place. Each thread owns its own copy.
    /// </param>
    /// <param name="timestampMs">
    /// The current time in milliseconds since the Unix epoch. Must not be negative.
    /// </param>
    /// <param name="destination">
    /// A span of at least <see cref="CrockfordBase32.UlidLength"/> characters.
    /// </param>
    /// <remarks>
    /// <para>
    /// Within a single millisecond the random field is incremented rather than redrawn, per the
    /// ULID specification's monotonic mode. That guarantees every identifier from this state sorts
    /// strictly after the one before it, even at rates far beyond what any real application
    /// produces.
    /// </para>
    /// <para>
    /// If the clock steps backwards &#8212; an NTP correction, a virtual machine resuming from a
    /// snapshot &#8212; the previous timestamp is reused rather than the earlier reading, so the
    /// sequence never regresses. The identifiers produced during that window carry a timestamp a
    /// few milliseconds ahead of the wall clock, which is the lesser of the two evils.
    /// </para>
    /// </remarks>
    internal static void Next(ref MonotonicUlidState state, long timestampMs, Span<char> destination)
    {
        Debug.Assert(timestampMs >= 0, "ULID timestamps are unsigned; the clock should never read before 1970.");

        if (timestampMs > state.LastTimestampMs)
        {
            Reseed(ref state, timestampMs);
        }
        else if (!TryIncrementRandomness(ref state))
        {
            // 2^80 identifiers stamped with the same millisecond. Unreachable in practice, but
            // borrowing the next millisecond keeps the sequence monotonic instead of stalling
            // or wrapping.
            Reseed(ref state, state.LastTimestampMs + 1);
        }

        CrockfordBase32.EncodeUlid(state.LastTimestampMs, state.RandomHigh, state.RandomLow, destination);
    }

    /// <summary>Stamps the state with a new millisecond and a fresh 80-bit random field.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Reseed(ref MonotonicUlidState state, long timestampMs)
    {
        state.LastTimestampMs = timestampMs;
        ThreadLocalCryptoRandom.NextUlidRandomness(out state.RandomHigh, out state.RandomLow);
    }

    /// <summary>
    /// Adds one to the 80-bit random field, carrying from the low word into the high word.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the field was incremented; <see langword="false"/> if it was
    /// already at its maximum and the millisecond is exhausted.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryIncrementRandomness(ref MonotonicUlidState state)
    {
        ulong low = state.RandomLow + 1;
        if (low != 0)
        {
            state.RandomLow = low;
            return true;
        }

        // The low 64 bits wrapped, so carry into the high 16.
        ulong high = state.RandomHigh + 1;
        if (high > MaxRandomHigh)
        {
            return false;
        }

        state.RandomHigh = high;
        state.RandomLow = 0;
        return true;
    }
}
