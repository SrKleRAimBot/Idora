package io.github.srkleraimbot.idora.jackson;

import com.fasterxml.jackson.databind.BeanProperty;
import com.fasterxml.jackson.databind.DeserializationContext;
import com.fasterxml.jackson.databind.JsonMappingException;
import com.fasterxml.jackson.databind.KeyDeserializer;
import com.fasterxml.jackson.databind.deser.ContextualKeyDeserializer;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidIdException;

import java.io.IOException;

/**
 * Reads the keys of a {@code Map<IdoraId, ...>}, strictly, honouring {@link IdoraIdPrefix} on the
 * map property. Registered by {@link IdoraModule}. A rejected key fails with an
 * {@link com.fasterxml.jackson.databind.exc.InvalidFormatException} carrying the
 * {@link InvalidIdException} as its cause.
 */
public final class IdoraIdKeyDeserializer extends KeyDeserializer implements ContextualKeyDeserializer {

    /** Accepts any conforming prefix. */
    static final IdoraIdKeyDeserializer ANY_PREFIX = new IdoraIdKeyDeserializer(null);

    /** The prefix every key must carry, or {@code null} for any. */
    private final String expectedPrefix;

    /** Creates a key deserializer that accepts any conforming prefix. */
    public IdoraIdKeyDeserializer() {
        this(null);
    }

    private IdoraIdKeyDeserializer(String expectedPrefix) {
        this.expectedPrefix = expectedPrefix;
    }

    /**
     * Returns a key deserializer that requires the prefix named by the property's
     * {@link IdoraIdPrefix}, or this one if it has none.
     *
     * @param context the deserialization context
     * @param property the map property, or {@code null} for a root value
     * @return the key deserializer to use for that property
     * @throws JsonMappingException if the annotation names an invalid prefix
     */
    @Override
    public KeyDeserializer createContextual(DeserializationContext context, BeanProperty property)
            throws JsonMappingException {
        String prefix = IdoraIdDeserializer.Prefixes.expected(context, property);
        if (prefix == null) {
            return ANY_PREFIX;
        }
        return prefix.equals(expectedPrefix) ? this : new IdoraIdKeyDeserializer(prefix);
    }

    /**
     * Reads one key.
     *
     * @param key the key's text
     * @param context the deserialization context
     * @return the identifier
     * @throws IOException if the key is not a conforming identifier with the expected prefix
     */
    @Override
    public Object deserializeKey(String key, DeserializationContext context) throws IOException {
        try {
            return expectedPrefix == null ? IdoraId.parse(key) : IdoraId.parse(key, expectedPrefix);
        } catch (InvalidIdException e) {
            JsonMappingException failure = context.weirdKeyException(IdoraId.class, key, e.getMessage());
            failure.initCause(e);
            throw failure;
        }
    }
}
