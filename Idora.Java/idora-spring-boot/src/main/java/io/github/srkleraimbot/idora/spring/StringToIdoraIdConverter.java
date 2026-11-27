package io.github.srkleraimbot.idora.spring;

import io.github.srkleraimbot.idora.IdoraId;
import org.springframework.core.convert.converter.Converter;

/**
 * Converts request parameters, path variables and other text to {@link IdoraId}, strictly.
 *
 * <pre>{@code
 * @GetMapping("/users/{id}")
 * User get(@PathVariable IdoraId id) { ... }
 * }</pre>
 *
 * <p>{@link IdoraAutoConfiguration} declares it as a bean, which Spring Boot adds to the conversion
 * service of Spring MVC and WebFlux. A malformed identifier fails conversion with the
 * {@link io.github.srkleraimbot.idora.InvalidIdException} as its cause, which Spring MVC answers
 * with {@code 400 Bad Request}. An empty string converts to {@code null}, as Spring's own
 * converters do, so a blank optional parameter reads as absent.
 *
 * <p>Conversion accepts any prefix. To insist on the prefix of a particular entity, take the
 * parameter as a {@code String} and call
 * {@link io.github.srkleraimbot.idora.IdoraGenerator#parseId(Class, CharSequence)}, or check
 * {@link IdoraId#hasPrefix(String)}.
 */
public final class StringToIdoraIdConverter implements Converter<String, IdoraId> {

    /** Creates the converter. */
    public StringToIdoraIdConverter() {
    }

    /**
     * Parses the text.
     *
     * @param source the text
     * @return the identifier, or {@code null} for an empty string
     * @throws io.github.srkleraimbot.idora.InvalidIdException if the text is not a conforming
     *     identifier
     */
    @Override
    public IdoraId convert(String source) {
        return source.isEmpty() ? null : IdoraId.parse(source);
    }
}
