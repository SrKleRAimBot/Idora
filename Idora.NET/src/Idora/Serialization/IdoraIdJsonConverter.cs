using System.Text.Json;
using System.Text.Json.Serialization;

namespace Idora;

/// <summary>
/// Reads and writes an <see cref="IdoraId"/> as a JSON string, such as
/// <c>"usr_01k2f9v8ta7m3x0qbrn4dzeh5c"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Applied to <see cref="IdoraId"/> with <see cref="JsonConverterAttribute"/>, so there is nothing
/// to register: reflection-based serialisation and source-generated contexts both pick it up. It also
/// handles <see cref="IdoraId"/> as a dictionary key.
/// </para>
/// <para>
/// Reading is as strict as <see cref="IdoraId.Parse(string)"/>. Anything that is not a valid
/// identifier fails with a <see cref="JsonException"/>, carrying the
/// <see cref="InvalidIdException"/> that explains why as its inner exception, so a bad identifier
/// in a request body is a deserialisation error rather than a value that blows up later.
/// </para>
/// <para>
/// <c>default(IdoraId)</c> is written as JSON <c>null</c>, and <c>null</c> reads back as
/// <c>default</c>, so an unassigned identifier round-trips. Declare the property as
/// <c>IdoraId?</c> if you need to tell "absent" apart from "empty"; <c>IdoraId?</c> is handled by
/// the serializer itself, using this converter for the non-null case.
/// </para>
/// </remarks>
public sealed class IdoraIdJsonConverter : JsonConverter<IdoraId>
{
    /// <summary>
    /// Asks to be called for JSON <c>null</c> too, which a converter for a value type is by default;
    /// stated explicitly so the round-trip of <c>default</c> does not hang on that default.
    /// </summary>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override IdoraId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => default,
            JsonTokenType.String => ParseOrThrow(reader.GetString()!),
            _ => throw new JsonException(
                "An identifier must be a JSON string, such as \"usr_01k2f9v8ta7m3x0qbrn4dzeh5c\", but the "
                + "token is " + reader.TokenType + "."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IdoraId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value.IsEmpty)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }

    /// <inheritdoc />
    public override IdoraId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        ParseOrThrow(reader.GetString()!);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, IdoraId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // A JSON object cannot have a null key, and an empty one would not read back.
        if (value.IsEmpty)
        {
            throw new JsonException("An empty IdoraId cannot be written as a JSON property name.");
        }

        writer.WritePropertyName(value.ToString());
    }

    private static IdoraId ParseOrThrow(string text)
    {
        try
        {
            return IdoraId.Parse(text);
        }
        catch (InvalidIdException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }
}
