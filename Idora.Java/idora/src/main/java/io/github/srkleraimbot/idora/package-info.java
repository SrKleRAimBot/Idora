/**
 * Prefixed, time-ordered entity identifiers: {@code <prefix>_<value>}, where the value is a
 * lowercase ULID (the default) or a lowercase, hyphen-free UUIDv7.
 *
 * <pre>{@code
 * IdoraGenerator ids = IdoraGenerator.builder()
 *     .registerEntity(User.class, "usr")
 *     .build();
 *
 * ids.generate(User.class);   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
 * }</pre>
 *
 * <p>The entry point is {@link io.github.srkleraimbot.idora.IdoraGenerator}. Prefixes are
 * registered once, while the generator is being built, either explicitly or by declaring them on
 * the entity type with {@link io.github.srkleraimbot.idora.EntityPrefix}. Misconfiguration fails
 * at that point with an {@link io.github.srkleraimbot.idora.IdoraException}, never later.
 *
 * <p>Identifiers that come back, in a route, a message or a foreign key, are read with
 * {@link io.github.srkleraimbot.idora.IdoraId}: a strict parse, the prefix, value, format and
 * timestamp, a chronological natural order, and range bounds for querying by creation time.
 *
 * <p>This package implements the Idora specification, shared with Idora.NET, Idora.JS, Idora.Go
 * and Idora.Rust: an identifier produced by any of them is indistinguishable from one produced by
 * any other.
 */
package io.github.srkleraimbot.idora;
