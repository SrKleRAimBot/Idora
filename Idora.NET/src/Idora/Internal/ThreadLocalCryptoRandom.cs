using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Idora.Internal;

/// <summary>
/// Supplies cryptographically strong random bytes with the per-call cost of a buffer read.
/// </summary>
/// <remarks>
/// <para>
/// Calling <see cref="RandomNumberGenerator.Fill(Span{byte})"/> once per identifier dominates the
/// cost of generation. Instead each thread keeps a <see cref="BufferSizeInBytes"/> block of entropy
/// and hands out slices of it, refilling from the system CSPRNG only when the block is exhausted.
/// The entropy is still cryptographically strong &#8212; it comes from the same source &#8212; but the
/// cost of obtaining it is amortised over roughly a hundred identifiers.
/// </para>
/// <para>
/// The buffer is <see cref="ThreadStaticAttribute">thread-static</see>, so there is no shared
/// mutable state, no lock and no contention no matter how many threads generate concurrently.
/// </para>
/// </remarks>
internal static class ThreadLocalCryptoRandom
{
    /// <summary>
    /// Size of the per-thread entropy block. 1 KiB holds entropy for ~102 ULIDs or 64 UUIDs,
    /// which keeps the refill cost negligible without holding a meaningful amount of memory
    /// on threads that only ever generate a handful of identifiers.
    /// </summary>
    private const int BufferSizeInBytes = 1024;

    /// <summary>Number of random bytes consumed by a single ULID (80 bits of randomness).</summary>
    internal const int UlidRandomByteCount = 10;

    [ThreadStatic]
    private static byte[]? t_buffer;

    /// <summary>Index of the first byte of the buffer that has not yet been handed out.</summary>
    [ThreadStatic]
    private static int t_offset;

    /// <summary>
    /// Fills <paramref name="destination"/> with cryptographically strong random bytes.
    /// </summary>
    /// <param name="destination">The span to fill. May be of any length.</param>
    internal static void Fill(Span<byte> destination)
    {
        // Requests larger than the buffer itself would defeat the point of buffering; serve them
        // straight from the system CSPRNG.
        if (destination.Length > BufferSizeInBytes)
        {
            RandomNumberGenerator.Fill(destination);
            return;
        }

        int offset = Reserve(destination.Length, out byte[] buffer);
        buffer.AsSpan(offset, destination.Length).CopyTo(destination);
    }

    /// <summary>
    /// Produces the 80 bits of randomness a ULID requires, without going through an intermediate
    /// byte buffer.
    /// </summary>
    /// <param name="high">Receives random bits 79..64, in the low 16 bits of the value.</param>
    /// <param name="low">Receives random bits 63..0.</param>
    internal static void NextUlidRandomness(out ulong high, out ulong low)
    {
        int offset = Reserve(UlidRandomByteCount, out byte[] buffer);
        ReadOnlySpan<byte> bytes = buffer.AsSpan(offset, UlidRandomByteCount);

        // The bytes are uniformly random, so the byte order chosen here is arbitrary; little-endian
        // reads are simply the cheapest on the platforms .NET targets.
        low = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        high = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
    }

    /// <summary>
    /// Reserves <paramref name="byteCount"/> bytes from the calling thread's buffer, refilling it
    /// from the system CSPRNG first if too few bytes remain.
    /// </summary>
    /// <param name="byteCount">How many bytes to reserve. Must not exceed <see cref="BufferSizeInBytes"/>.</param>
    /// <param name="buffer">Receives the calling thread's buffer.</param>
    /// <returns>The offset at which the reserved bytes start.</returns>
    private static int Reserve(int byteCount, out byte[] buffer)
    {
        buffer = t_buffer ??= CreateBuffer();

        int offset = t_offset;
        if (offset + byteCount > BufferSizeInBytes)
        {
            RandomNumberGenerator.Fill(buffer);
            offset = 0;
        }

        t_offset = offset + byteCount;
        return offset;
    }

    /// <summary>Allocates and seeds this thread's entropy block.</summary>
    private static byte[] CreateBuffer()
    {
        byte[] buffer = new byte[BufferSizeInBytes];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }
}
