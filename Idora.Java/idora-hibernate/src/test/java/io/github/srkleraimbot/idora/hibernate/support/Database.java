package io.github.srkleraimbot.idora.hibernate.support;

import org.hibernate.SessionFactory;
import org.hibernate.boot.MetadataSources;
import org.hibernate.boot.registry.BootstrapServiceRegistry;
import org.hibernate.boot.registry.BootstrapServiceRegistryBuilder;
import org.hibernate.boot.registry.StandardServiceRegistry;
import org.hibernate.boot.registry.StandardServiceRegistryBuilder;
import org.hibernate.cfg.AvailableSettings;

import java.util.Map;
import java.util.UUID;

/** Builds a Hibernate session factory over a fresh in-memory H2 database. */
public final class Database {

    private Database() {
    }

    /** A session factory for the given entities, with the given extra settings. */
    public static SessionFactory open(Map<String, Object> settings, Class<?>... entities) {
        return open(null, settings, entities);
    }

    /** As {@link #open(Map, Class[])}, with an extra class loader for Hibernate's service discovery. */
    public static SessionFactory open(ClassLoader serviceLoader, Map<String, Object> settings, Class<?>... entities) {
        BootstrapServiceRegistryBuilder bootstrap = new BootstrapServiceRegistryBuilder();
        if (serviceLoader != null) {
            bootstrap.applyClassLoader(serviceLoader);
        }
        BootstrapServiceRegistry bootstrapRegistry = bootstrap.build();
        StandardServiceRegistry registry = new StandardServiceRegistryBuilder(bootstrapRegistry)
                .applySetting(AvailableSettings.JAKARTA_JDBC_URL, "jdbc:h2:mem:" + UUID.randomUUID() + ";DB_CLOSE_DELAY=-1")
                .applySetting(AvailableSettings.JAKARTA_JDBC_USER, "sa")
                .applySetting(AvailableSettings.JAKARTA_HBM2DDL_DATABASE_ACTION, "create-drop")
                .applySetting(AvailableSettings.SHOW_SQL, false)
                .applySettings(settings)
                .build();
        try {
            MetadataSources sources = new MetadataSources(registry);
            for (Class<?> entity : entities) {
                sources.addAnnotatedClass(entity);
            }
            return sources.buildMetadata().buildSessionFactory();
        } catch (RuntimeException e) {
            StandardServiceRegistryBuilder.destroy(registry);
            throw e;
        }
    }

    /** The first throwable of the given type in the cause chain, or {@code null}. */
    public static <T extends Throwable> T causeOfType(Throwable thrown, Class<T> type) {
        for (Throwable t = thrown; t != null; t = t.getCause()) {
            if (type.isInstance(t)) {
                return type.cast(t);
            }
        }
        return null;
    }
}
