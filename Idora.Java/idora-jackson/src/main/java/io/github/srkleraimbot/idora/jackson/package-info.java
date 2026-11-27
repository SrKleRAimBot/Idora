/**
 * Jackson 2 databind support for {@link io.github.srkleraimbot.idora.IdoraId}.
 *
 * <pre>{@code
 * ObjectMapper mapper = JsonMapper.builder().addModule(new IdoraModule()).build();
 * }</pre>
 *
 * <p>Register {@link io.github.srkleraimbot.idora.jackson.IdoraModule}, by hand or through
 * {@code findAndRegisterModules()}, and {@code IdoraId} values are written as strings and read back
 * strictly. {@link io.github.srkleraimbot.idora.jackson.IdoraIdPrefix} requires a property's
 * identifiers to carry a particular prefix.
 */
package io.github.srkleraimbot.idora.jackson;
