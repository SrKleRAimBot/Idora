using System.Buffers;
using Idora.Internal;

namespace Idora;

/// <summary>
/// The default <see cref="IIdoraGenerator"/>.
/// </summary>
/// <remarks>
/// <para>
/// You will normally let the container create this via <c>AddIdora</c>. The public
/// constructor exists for the cases where a container would be overkill &#8212; a console tool, a
/// migration script, a test that wants a generator and nothing else.
/// </para>
/// <para>
/// A generated identifier costs exactly one allocation: the string that is returned. The prefix and
/// the value are written straight into that string's buffer through
/// <see cref="string.Create{TState}(int, TState, SpanAction{char, TState})"/>, so there is no
/// intermediate buffer, no <see cref="System.Text.StringBuilder"/> and no concatenation.
/// </para>
/// <para>
/// Instances are immutable and safe to share across threads.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var generator = new IdoraGenerator(options => options.RegisterEntity&lt;User&gt;("usr"));
/// string id = generator.Generate&lt;User&gt;();
/// </code>
/// </example>
public sealed class IdoraGenerator : IIdoraGenerator
{
    /// <summary>
    /// Writes an identifier whose prefix already carries its separator, as stored in the registry.
    /// </summary>
    private static readonly SpanAction<char, IdWriteState> WriteRegisteredId = static (destination, state) =>
    {
        state.Prefix.CopyTo(destination);
        state.Writer.Write(destination[state.Prefix.Length..]);
    };

    /// <summary>
    /// Writes an identifier from a caller-supplied prefix, adding the separator as it goes.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="WriteRegisteredId"/> rather than merged behind a flag, so that
    /// neither path pays for the other's branch and no joined "prefix_" string has to be allocated
    /// for the ad-hoc case.
    /// </remarks>
    private static readonly SpanAction<char, IdWriteState> WriteAdHocId = static (destination, state) =>
    {
        state.Prefix.CopyTo(destination);
        destination[state.Prefix.Length] = EntityPrefixValidator.Separator;
        state.Writer.Write(destination[(state.Prefix.Length + 1)..]);
    };

    private readonly EntityPrefixRegistry _registry;
    private readonly IIdValueWriter _writer;

    /// <summary>
    /// Cached from the writer so that sizing the string does not go through an interface call.
    /// </summary>
    private readonly int _valueLength;

    /// <summary>
    /// Creates a generator from configuration, without a dependency injection container.
    /// </summary>
    /// <param name="configure">
    /// Receives the options to configure. Pass <see langword="null"/> to accept the defaults: ULIDs,
    /// and no registered entity types, meaning only <see cref="Generate(string)"/> is usable.
    /// </param>
    /// <exception cref="InvalidEntityPrefixException">A registered prefix is not valid.</exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// A prefix was registered for two entity types, or an entity type was registered twice.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="IdoraOptions.Strategy"/> was set to a value that is not defined.
    /// </exception>
    public IdoraGenerator(Action<IdoraOptions>? configure = null)
    {
        IdoraOptions options = new();
        configure?.Invoke(options);

        _registry = EntityPrefixRegistry.Create(options.Registrations);
        _writer = IdValueWriterFactory.Create(options.Strategy);
        _valueLength = _writer.ValueLength;
    }

    /// <summary>Creates a generator over an already-built registry and writer.</summary>
    /// <remarks>Used by the container registration, which builds both once during start-up.</remarks>
    internal IdoraGenerator(EntityPrefixRegistry registry, IIdValueWriter writer)
    {
        _registry = registry;
        _writer = writer;
        _valueLength = writer.ValueLength;
    }

    /// <inheritdoc />
    public string Generate<TEntity>()
    {
        string prefixWithSeparator = _registry.GetPrefixWithSeparator(typeof(TEntity));

        return string.Create(
            prefixWithSeparator.Length + _valueLength,
            new IdWriteState(prefixWithSeparator, _writer),
            WriteRegisteredId);
    }

    /// <inheritdoc />
    public string Generate(string entityPrefix)
    {
        EntityPrefixValidator.Validate(entityPrefix, nameof(entityPrefix));

        return string.Create(
            entityPrefix.Length + 1 + _valueLength,
            new IdWriteState(entityPrefix, _writer),
            WriteAdHocId);
    }

    /// <summary>
    /// Everything the write callbacks need, passed by value so that
    /// <see cref="string.Create{TState}(int, TState, SpanAction{char, TState})"/> does not have to
    /// close over anything.
    /// </summary>
    private readonly struct IdWriteState(string prefix, IIdValueWriter writer)
    {
        /// <summary>The prefix to copy, with or without its separator depending on the callback.</summary>
        internal string Prefix { get; } = prefix;

        /// <summary>The writer that fills in everything after the prefix.</summary>
        internal IIdValueWriter Writer { get; } = writer;
    }
}
