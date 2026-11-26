package io.github.srkleraimbot.idora.jackson;

import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.core.JsonToken;
import com.fasterxml.jackson.databind.BeanProperty;
import com.fasterxml.jackson.databind.DeserializationContext;
import com.fasterxml.jackson.databind.JsonDeserializer;
import com.fasterxml.jackson.databind.JsonMappingException;
import com.fasterxml.jackson.databind.deser.ContextualDeserializer;
import com.fasterxml.jackson.databind.deser.std.StdScalarDeserializer;
import com.fasterxml.jackson.databind.exc.InvalidDefinitionException;
import com.fasterxml.jackson.databind.exc.InvalidFormatException;
import com.fasterxml.jackson.databind.type.LogicalType;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidEntityPrefixException;
import io.github.srkleraimbot.idora.InvalidIdException;

import java.io.IOException;

/**
 * Reads an {@link IdoraId} from a JSON string, strictly, and checks its prefix when the property
 * carries {@link IdoraIdPrefix}. Registered by {@link IdoraModule}.
 *
 * <p>Only a JSON string is accepted: a number, a boolean, an object or an array fails with a
 * {@link com.fasterxml.jackson.databind.exc.MismatchedInputException}, and a string that is not a
 * conforming identifier (including the empty string) with an
 * {@link com.fasterxml.jackson.databind.exc.InvalidFormatException} carrying the
 * {@link InvalidIdException} as its cause. JSON {@code null} is handled by Jackson and becomes
 * {@code null}.
 */
public final class IdoraIdDeserializer extends StdScalarDeserializer<IdoraId> implements ContextualDeserializer {

    private static final long serialVersionUID = 1L;

    /** Accepts any conforming prefix; the deserializer for properties without {@link IdoraIdPrefix}. */
    static final IdoraIdDeserializer ANY_PREFIX = new IdoraIdDeserializer(null);

    /** The prefix every identifier must carry, or {@code null} for any. */
    private final String expectedPrefix;

    /** Creates a deserializer that accepts any conforming prefix. */
    public IdoraIdDeserializer() {
        this(null);
    }

    private IdoraIdDeserializer(String expectedPrefix) {
        super(IdoraId.class);
        this.expectedPrefix = expectedPrefix;
    }

    /**
     * Returns a deserializer that requires the prefix named by the property's
     * {@link IdoraIdPrefix}, or this one if it has none.
     *
     * @param context the deserialization context
     * @param property the property being deserialized, or {@code null} for a root value
     * @return the deserializer to use for that property
     * @throws JsonMappingException if the annotation names an invalid prefix
     */
    @Override
    public JsonDeserializer<?> createContextual(DeserializationContext context, BeanProperty property)
            throws JsonMappingException {
        String prefix = Prefixes.expected(context, property);
        if (prefix == null) {
            return ANY_PREFIX;
        }
        return prefix.equals(expectedPrefix) ? this : new IdoraIdDeserializer(prefix);
    }

    /**
     * Reads one identifier.
     *
     * @param parser the parser, positioned on the value
     * @param context the deserialization context
     * @return the identifier
     * @throws IOException if the value is not a string, or not a conforming identifier with the
     *     expected prefix
     */
    @Override
    public IdoraId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
        if (!parser.hasToken(JsonToken.VALUE_STRING)) {
            return (IdoraId) context.handleUnexpectedToken(IdoraId.class, parser);
        }
        String text = parser.getText();
        try {
            return expectedPrefix == null ? IdoraId.parse(text) : IdoraId.parse(text, expectedPrefix);
        } catch (InvalidIdException e) {
            // Built here rather than with context.weirdStringException, whose message quotes the
            // text again, up to Jackson's own limit: Idora's message already quotes it, shortened.
            // The full text stays on getValue().
            InvalidFormatException failure = InvalidFormatException.from(
                    parser, "Cannot deserialize value of type `" + IdoraId.class.getName() + "`: " + e.getMessage(),
                    text, IdoraId.class);
            failure.initCause(e);
            throw failure;
        }
    }

    /**
     * Identifiers are text.
     *
     * @return {@link LogicalType#Textual}
     */
    @Override
    public LogicalType logicalType() {
        return LogicalType.Textual;
    }

    /** Reads {@link IdoraIdPrefix} from a property and validates it once, at deserializer construction. */
    static final class Prefixes {

        private Prefixes() {
        }

        /**
         * Returns the prefix the property requires, or {@code null} if it does not carry
         * {@link IdoraIdPrefix}.
         *
         * @throws InvalidDefinitionException if the annotation names an invalid prefix
         */
        static String expected(DeserializationContext context, BeanProperty property) throws InvalidDefinitionException {
            if (property == null) {
                return null;
            }
            IdoraIdPrefix annotation = property.getAnnotation(IdoraIdPrefix.class);
            if (annotation == null) {
                return null;
            }
            String prefix = annotation.value();
            try {
                // Checked once, here, rather than on every value. An invalid prefix is a mistake in
                // the code, not in the input, and is reported as one.
                IdoraGenerator.validatePrefix(prefix);
            } catch (InvalidEntityPrefixException e) {
                InvalidDefinitionException failure = InvalidDefinitionException.from(
                        context.getParser(),
                        "The @IdoraIdPrefix annotation on property '" + property.getName() + "' of "
                                + property.getMember().getDeclaringClass().getName()
                                + " does not declare a valid prefix. " + e.getMessage(),
                        context.constructType(IdoraId.class));
                failure.initCause(e);
                throw failure;
            }
            return prefix;
        }
    }
}
