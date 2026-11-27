package io.github.srkleraimbot.idora.hibernate;

import org.hibernate.annotations.IdGeneratorType;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Generates the entity's identifier with Idora when Hibernate first persists it.
 *
 * <pre>{@code
 * @Entity
 * @EntityPrefix("usr")
 * public class User {
 *     @Id
 *     @IdoraGeneratedId
 *     private String id;   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c", assigned by persist()
 *     ...
 * }
 * }</pre>
 *
 * <p>Place it next to {@link jakarta.persistence.Id @Id}, on a field or getter of type
 * {@link String} or {@link io.github.srkleraimbot.idora.IdoraId}, instead of
 * {@link jakarta.persistence.GeneratedValue @GeneratedValue}. The identifier is generated in Java,
 * before the {@code INSERT}, so Hibernate can batch inserts and the entity has its identifier as soon
 * as {@code persist} returns.
 *
 * <p>Where the prefix comes from:
 *
 * <ul>
 *   <li><strong>By default</strong>, from the {@link io.github.srkleraimbot.idora.IdoraGenerator}'s
 *       registry, for the entity that owns the identifier: the root of its inheritance hierarchy,
 *       since every entity in a hierarchy shares one identifier column. The entity must be
 *       registered with that generator.
 *   <li>With {@link #entity()}, from the registry, for the named type instead.
 *   <li>With {@link #prefix()}, exactly that prefix, with no registry involved.
 * </ul>
 *
 * <p>The generator is found through the Hibernate setting {@value IdoraHibernateSettings#GENERATOR},
 * or else an {@link IdoraGeneratorProvider} registered with {@link java.util.ServiceLoader}; see
 * {@link IdoraHibernateSettings#GENERATOR}. With an explicit {@link #prefix()} and no generator
 * configured, a default one ({@link io.github.srkleraimbot.idora.IdoraGenerator#create()}: ULID
 * values) is used.
 *
 * <p>Everything is resolved while Hibernate builds its metadata: an unregistered entity, an invalid
 * prefix, a missing generator or an unsupported attribute type fails start-up, not the first
 * insert.
 */
@Documented
@IdGeneratorType(IdoraIdGenerator.class)
@Retention(RetentionPolicy.RUNTIME)
@Target({ElementType.FIELD, ElementType.METHOD})
public @interface IdoraGeneratedId {

    /**
     * A prefix to use as written, instead of looking one up in the registry: 1 to 16 ASCII letters
     * or digits. Empty, the default, means "look it up".
     *
     * @return the explicit prefix, or empty
     */
    String prefix() default "";

    /**
     * The registered type whose prefix to use, instead of the entity that owns the identifier.
     * {@code void.class}, the default, means "the owning entity". Cannot be combined with
     * {@link #prefix()}.
     *
     * @return the type to look up, or {@code void.class}
     */
    Class<?> entity() default void.class;
}
