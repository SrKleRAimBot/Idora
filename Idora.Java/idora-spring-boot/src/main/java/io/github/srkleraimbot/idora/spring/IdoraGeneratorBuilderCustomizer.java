package io.github.srkleraimbot.idora.spring;

import io.github.srkleraimbot.idora.IdoraGenerator;

/**
 * Adjusts the {@link IdoraGenerator.Builder} that {@link IdoraAutoConfiguration} uses, after the
 * {@code idora.*} properties and the annotation scan have been applied. Declare one as a bean to
 * register types in code:
 *
 * <pre>{@code
 * @Bean
 * IdoraGeneratorBuilderCustomizer vendorTypes() {
 *     return builder -> builder.registerEntity(Widget.class, "wdg");
 * }
 * }</pre>
 *
 * <p>Customizers run in {@link org.springframework.core.Ordered} order. A registration that clashes
 * with one already made fails start-up with a
 * {@link io.github.srkleraimbot.idora.DuplicateEntityPrefixException}, as it would anywhere else.
 */
@FunctionalInterface
public interface IdoraGeneratorBuilderCustomizer {

    /**
     * Customizes the builder.
     *
     * @param builder the builder; build is called after every customizer has run
     */
    void customize(IdoraGenerator.Builder builder);
}
