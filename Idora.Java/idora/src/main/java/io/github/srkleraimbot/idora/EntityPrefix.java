package io.github.srkleraimbot.idora;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Declares, on the entity type itself, the prefix its identifiers carry.
 *
 * <pre>{@code
 * @EntityPrefix("usr")
 * public final class User { ... }
 *
 * IdoraGenerator ids = IdoraGenerator.builder()
 *     .registerEntitiesFromPackageOf(User.class)
 *     .build();
 *
 * ids.generate(User.class);   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
 * }</pre>
 *
 * <p>This is the alternative to {@link IdoraGenerator.Builder#registerEntity(Class, String)}. It
 * keeps the prefix next to the type it describes rather than in a wall of registrations, which
 * reads better once a domain has more than a handful of entities.
 *
 * <p>The annotation on its own does nothing. It is collected while the generator is being built,
 * by {@link IdoraGenerator.Builder#registerEntities(Class[])} for types you list, or by
 * {@link IdoraGenerator.Builder#registerEntitiesFromPackageOf(Class)} for a package you name.
 * Either way it lands in the same immutable lookup as an explicit registration, so nothing is
 * reflected over once the generator exists, and the two styles police each other: a prefix claimed
 * twice by any combination of them fails the build.
 *
 * <p>The annotation may be placed on a class, record, interface or enum, public or not, top-level
 * or nested. It is deliberately <strong>not</strong> {@link java.lang.annotation.Inherited}: a
 * subclass never takes its base type's prefix, which would otherwise collide with the base the
 * moment both were registered.
 *
 * <p>The prefix is validated when it is collected, not here; an invalid one fails with an
 * {@link InvalidEntityPrefixException} whose message names the annotated type.
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface EntityPrefix {

    /**
     * The prefix this entity type's identifiers carry: 1 to 16 ASCII letters or digits. Lowercase
     * reads best, but the prefix is used exactly as written.
     *
     * @return the declared prefix
     */
    String value();
}
