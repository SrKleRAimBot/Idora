/**
 * Spring Boot 3 auto-configuration for Idora.
 *
 * <p>Add {@code idora-spring-boot} and the application gets an
 * {@link io.github.srkleraimbot.idora.IdoraGenerator} bean registered with every
 * {@link io.github.srkleraimbot.idora.EntityPrefix} type in its own packages, configured by the
 * {@code idora.*} properties ({@link io.github.srkleraimbot.idora.spring.IdoraProperties}), plus
 * {@link io.github.srkleraimbot.idora.IdoraId} binding for web requests and, when they are present,
 * the Jackson module and the Hibernate generator configuration. See
 * {@link io.github.srkleraimbot.idora.spring.IdoraAutoConfiguration}.
 */
package io.github.srkleraimbot.idora.spring;
