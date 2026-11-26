package io.github.srkleraimbot.idora.jackson;

import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.databind.SerializerProvider;
import com.fasterxml.jackson.databind.ser.std.StdScalarSerializer;
import io.github.srkleraimbot.idora.IdoraId;

import java.io.IOException;

/**
 * Writes an {@link IdoraId} as a JSON string holding its text. Registered by {@link IdoraModule};
 * public so that it can also be named in {@code @JsonSerialize(using = ...)}.
 */
public final class IdoraIdSerializer extends StdScalarSerializer<IdoraId> {

    private static final long serialVersionUID = 1L;

    /** The shared instance; the serializer is stateless. */
    public static final IdoraIdSerializer INSTANCE = new IdoraIdSerializer();

    /** Creates a serializer. Prefer {@link #INSTANCE}. */
    public IdoraIdSerializer() {
        super(IdoraId.class);
    }

    /**
     * Writes the identifier's text.
     *
     * @param value the identifier; never {@code null}, which Jackson writes itself
     * @param generator the generator to write to
     * @param provider the serializer provider
     * @throws IOException if writing fails
     */
    @Override
    public void serialize(IdoraId value, JsonGenerator generator, SerializerProvider provider) throws IOException {
        generator.writeString(value.toString());
    }
}
