package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.IdoraGenerator;

/**
 * Supplies the {@link IdoraGenerator} that {@link IdoraGeneratedId} generates with, for
 * configurations that cannot hand Hibernate the generator itself.
 *
 * <pre>{@code
 * public final class Ids implements IdoraGeneratorProvider {
 *     public static final IdoraGenerator IDS = IdoraGenerator.builder()
 *         .registerEntitiesFromPackageOf(User.class)
 *         .build();
 *
 *     @Override
 *     public IdoraGenerator getIdoraGenerator() {
 *         return IDS;
 *     }
 * }
 * }</pre>
 *
 * <p>Name the implementation in the {@value IdoraHibernateSettings#GENERATOR} property, or register
 * it in {@code META-INF/services/io.github.srkleraimbot.idora.hibernate.IdoraGeneratorProvider}. It
 * needs a public no-argument constructor.
 *
 * <p>It is asked once for each entity that uses {@link IdoraGeneratedId}, while Hibernate starts.
 * Return one shared generator, as above, rather than building a new one per call, so that the whole
 * application works from the same registry.
 */
@FunctionalInterface
public interface IdoraGeneratorProvider {

    /**
     * Returns the generator to use.
     *
     * @return the generator; never {@code null}
     */
    IdoraGenerator getIdoraGenerator();
}
