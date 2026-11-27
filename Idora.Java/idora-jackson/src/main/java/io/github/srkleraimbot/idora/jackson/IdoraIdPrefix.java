package io.github.srkleraimbot.idora.jackson;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Requires the {@link io.github.srkleraimbot.idora.IdoraId} read into a property to carry exactly
 * this prefix, case included.
 *
 * <pre>{@code
 * record TransferRequest(
 *         @IdoraIdPrefix("acc") IdoraId from,
 *         @IdoraIdPrefix("acc") IdoraId to,
 *         @IdoraIdPrefix("usr") List<IdoraId> approvers) {}
 *
 * mapper.readValue("{\"from\":\"usr_01k2f9v8ta7m3x0qbrn4dzeh5c\", ...}", TransferRequest.class);
 * // InvalidFormatException: ... has the prefix 'usr', but 'acc' was expected ...
 * //   (through reference chain: TransferRequest["from"])
 * }</pre>
 *
 * <p>Honoured by {@link IdoraModule} when deserializing an {@code IdoraId} property, and the
 * elements, values and keys of a collection, array or map property. It may sit on a field, a
 * setter, a creator parameter or a record component. It has no effect on serialization, and none on
 * a property of any other type.
 *
 * <p>The prefix is validated when Jackson first builds the deserializer for the property: an
 * invalid one fails with an {@link com.fasterxml.jackson.databind.exc.InvalidDefinitionException}
 * naming it, the first time the type is read.
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target({ElementType.FIELD, ElementType.METHOD, ElementType.PARAMETER, ElementType.ANNOTATION_TYPE})
public @interface IdoraIdPrefix {

    /**
     * The prefix the identifier must carry, without the separator: 1 to 16 ASCII letters or digits.
     *
     * @return the required prefix
     */
    String value();
}
