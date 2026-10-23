namespace Idora.Internal;

/// <summary>
/// Maps a configured <see cref="IdGenerationStrategy"/> onto the writer that implements it.
/// </summary>
internal static class IdValueWriterFactory
{
    /// <summary>Returns the writer for <paramref name="strategy"/>.</summary>
    /// <param name="strategy">The configured strategy.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="strategy"/> is not a defined <see cref="IdGenerationStrategy"/> value. This
    /// can only happen if the enum was cast from an arbitrary number.
    /// </exception>
    internal static IIdValueWriter Create(IdGenerationStrategy strategy) => strategy switch
    {
        IdGenerationStrategy.Ulid => UlidValueWriter.Instance,
        IdGenerationStrategy.UuidV7 => UuidV7ValueWriter.Instance,
        _ => throw new ArgumentOutOfRangeException(
            nameof(strategy),
            strategy,
            "The identifier generation strategy is not one of the defined values."),
    };
}
