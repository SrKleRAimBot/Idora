package io.github.srkleraimbot.idora.hibernate;

/**
 * The Hibernate configuration properties {@code idora-hibernate} reads.
 */
public final class IdoraHibernateSettings {

    /**
     * The {@link io.github.srkleraimbot.idora.IdoraGenerator} that {@link IdoraGeneratedId}
     * generates with, and whose registry supplies the prefixes.
     *
     * <p>The value may be:
     *
     * <ul>
     *   <li>the {@code IdoraGenerator} itself, which is the usual choice when the configuration is
     *       built in code: {@code StandardServiceRegistryBuilder.applySetting(GENERATOR, ids)}, the
     *       map passed to {@code Persistence.createEntityManagerFactory}, or a Spring
     *       {@code HibernatePropertiesCustomizer} (which {@code idora-spring-boot} registers for
     *       you);
     *   <li>an {@link IdoraGeneratorProvider} instance;
     *   <li>an {@code IdoraGeneratorProvider} class, or its fully qualified name, which is what a
     *       {@code persistence.xml} or {@code hibernate.properties} can express. It is instantiated
     *       through its public no-argument constructor.
     * </ul>
     *
     * <p>When the property is not set, exactly one {@link IdoraGeneratorProvider} registered in
     * {@code META-INF/services} is used, if there is one. Several are an error, because Hibernate
     * would have to guess which one is meant.
     */
    public static final String GENERATOR = "idora.generator";

    private IdoraHibernateSettings() {
    }
}
