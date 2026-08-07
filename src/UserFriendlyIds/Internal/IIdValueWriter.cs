namespace UserFriendlyIds.Internal;

/// <summary>
/// Writes the value half of an identifier &#8212; everything after the underscore &#8212; directly
/// into a caller-supplied buffer.
/// </summary>
/// <remarks>
/// Writers deliberately do not return strings. The generator allocates the finished identifier
/// exactly once, as a single <see cref="string"/> of the final length, and hands the writer the
/// slice it should fill. Nothing else is allocated along the way.
/// </remarks>
internal interface IIdValueWriter
{
    /// <summary>
    /// The exact number of characters <see cref="Write"/> produces. Constant for a given writer,
    /// which is what lets the generator size the string before any work is done.
    /// </summary>
    int ValueLength { get; }

    /// <summary>
    /// Writes exactly <see cref="ValueLength"/> characters into <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">
    /// A buffer of at least <see cref="ValueLength"/> characters.
    /// </param>
    void Write(Span<char> destination);
}
