using System.Diagnostics;

namespace Idora.Internal;

/// <summary>
/// Writes a version 7 UUID as 32 lowercase hexadecimal characters, with no hyphens.
/// </summary>
/// <remarks>
/// The hyphens are dropped because the identifier already has structure of its own &#8212; the
/// prefix and its separator &#8212; and a second, differently shaped set of delimiters inside the
/// value only makes it harder to read and longer to store. The value still round-trips through
/// <see cref="Guid.Parse(string)"/>, which accepts the compact form.
/// </remarks>
internal sealed class UuidV7ValueWriter : IIdValueWriter
{
    /// <summary>Number of characters in a hyphen-free UUID.</summary>
    internal const int UuidLength = 32;

    /// <summary>The single instance needed; the type holds no per-instance state.</summary>
    internal static readonly UuidV7ValueWriter Instance = new();

    private UuidV7ValueWriter()
    {
    }

    /// <inheritdoc />
    public int ValueLength => UuidLength;

    /// <inheritdoc />
    public void Write(Span<char> destination)
    {
        // "N" is the 32-character hyphen-free form, and Guid formats hexadecimal in lowercase.
        bool formatted = UuidV7.Create().TryFormat(destination, out _, "N");

        Debug.Assert(formatted, "The destination is always sized by ValueLength, so formatting cannot fail.");
    }
}
