package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidEntityPrefixException;
import org.hibernate.MappingException;
import org.hibernate.boot.registry.classloading.spi.ClassLoaderService;
import org.hibernate.engine.config.spi.ConfigurationService;
import org.hibernate.engine.spi.SharedSessionContractImplementor;
import org.hibernate.generator.AnnotationBasedGenerator;
import org.hibernate.generator.BeforeExecutionGenerator;
import org.hibernate.generator.EventType;
import org.hibernate.generator.EventTypeSets;
import org.hibernate.generator.GeneratorCreationContext;
import org.hibernate.mapping.PersistentClass;
import org.hibernate.service.ServiceRegistry;

import java.lang.reflect.Field;
import java.lang.reflect.Member;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Collection;
import java.util.EnumSet;
import java.util.List;

/**
 * The Hibernate generator behind {@link IdoraGeneratedId}. Hibernate creates one per entity
 * hierarchy while building its metadata; you do not use this class directly.
 *
 * <p>All the work of finding the generator and the prefix happens in
 * {@link #initialize(IdoraGeneratedId, Member, GeneratorCreationContext)}, while Hibernate builds its
 * metadata, so a misconfiguration fails start-up. Generating then costs what
 * {@link IdoraGenerator#generate(Class)} costs, plus wrapping the text in an {@link IdoraId} for an
 * {@code IdoraId}-typed attribute.
 */
public final class IdoraIdGenerator implements BeforeExecutionGenerator, AnnotationBasedGenerator<IdoraGeneratedId> {

    private static final long serialVersionUID = 1L;

    /**
     * Not serializable, and not needed to be: Hibernate serializes a generator only along with a
     * session factory, which is rebuilt from configuration rather than restored in practice.
     */
    @SuppressWarnings("serial")
    private IdoraGenerator generator;

    /** The registered type to generate for, or {@code null} when {@link #prefix} is used. */
    private Class<?> entityType;

    /** The explicit prefix, or {@code null} when {@link #entityType} is used. */
    private String prefix;

    /** Whether the attribute holds an {@link IdoraId} rather than a {@link String}. */
    private boolean producesIdoraId;

    /**
     * Called by Hibernate, which then calls
     * {@link #initialize(IdoraGeneratedId, Member, GeneratorCreationContext)} before any use.
     */
    public IdoraIdGenerator() {
    }

    /**
     * Called by Hibernate for each identifier annotated with {@link IdoraGeneratedId}, while it
     * builds its metadata.
     *
     * @param annotation the annotation on the identifier
     * @param member the annotated field or getter
     * @param context what Hibernate knows about the mapping being built
     * @throws MappingException if the attribute type is unsupported, the annotation is
     *     contradictory, or no generator is configured where one is needed
     * @throws InvalidEntityPrefixException if {@link IdoraGeneratedId#prefix()} breaks the rules
     * @throws io.github.srkleraimbot.idora.EntityPrefixNotRegisteredException if the entity whose
     *     prefix is needed is not registered with the generator
     */
    @Override
    public void initialize(IdoraGeneratedId annotation, Member member, GeneratorCreationContext context) {
        String where = member.getDeclaringClass().getName() + "." + member.getName();
        this.producesIdoraId = producesIdoraId(member, where);

        boolean explicitPrefix = !annotation.prefix().isEmpty();
        boolean explicitEntity = annotation.entity() != void.class;
        if (explicitPrefix && explicitEntity) {
            throw new MappingException("@IdoraGeneratedId on " + where + " names both a prefix ('" + annotation.prefix()
                    + "') and an entity (" + annotation.entity().getName() + "). Use one or the other: the prefix is used "
                    + "as written, the entity's is looked up in the registry.");
        }

        IdoraGenerator configured = configuredGenerator(context.getServiceRegistry(), where);
        if (explicitPrefix) {
            // Validated now, at start-up, rather than on the first insert.
            this.prefix = IdoraGenerator.validatePrefix(annotation.prefix());
            this.generator = configured != null ? configured : IdoraGenerator.create();
            this.entityType = null;
            return;
        }

        Class<?> type = explicitEntity ? annotation.entity() : owningEntity(context, where);
        if (configured == null) {
            throw new MappingException("@IdoraGeneratedId on " + where + " needs an IdoraGenerator to look up the prefix "
                    + "registered for " + type.getName() + ", and none is configured. Set the Hibernate property '"
                    + IdoraHibernateSettings.GENERATOR + "' to your IdoraGenerator (or to an IdoraGeneratorProvider, or "
                    + "the name of an IdoraGeneratorProvider class), register an IdoraGeneratorProvider in "
                    + "META-INF/services, or give the annotation an explicit prefix, for example "
                    + "@IdoraGeneratedId(prefix = \"abc\"). With idora-spring-boot this is done for you.");
        }
        // Fails now, naming the type, if it was never registered.
        configured.registry().getPrefix(type);
        this.generator = configured;
        this.entityType = type;
        this.prefix = null;
    }

    /**
     * Generated once, on insert.
     *
     * @return {@link EventTypeSets#INSERT_ONLY}
     */
    @Override
    public EnumSet<EventType> getEventTypes() {
        return EventTypeSets.INSERT_ONLY;
    }

    /**
     * Generates a new identifier for the entity being inserted.
     *
     * @param session the session
     * @param owner the entity being inserted
     * @param currentValue the identifier's current value; ignored
     * @param eventType always {@link EventType#INSERT}
     * @return a {@link String} or an {@link IdoraId}, matching the attribute's type
     */
    @Override
    public Object generate(SharedSessionContractImplementor session, Object owner, Object currentValue, EventType eventType) {
        if (producesIdoraId) {
            return entityType != null ? generator.generateId(entityType) : generator.generateId(prefix);
        }
        return entityType != null ? generator.generate(entityType) : generator.generate(prefix);
    }

    /** Reads the attribute type off the annotated member, accepting only what can hold an identifier. */
    private static boolean producesIdoraId(Member member, String where) {
        Class<?> type;
        if (member instanceof Field) {
            type = ((Field) member).getType();
        } else if (member instanceof Method) {
            type = ((Method) member).getReturnType();
        } else {
            throw new MappingException("@IdoraGeneratedId on " + where + " must annotate a field or a getter.");
        }
        if (type == IdoraId.class) {
            return true;
        }
        if (type == String.class) {
            return false;
        }
        throw new MappingException("@IdoraGeneratedId on " + where + " annotates an attribute of type " + type.getName()
                + ", which cannot hold an Idora identifier. Declare it as String or IdoraId.");
    }

    /** The entity that owns the identifier: the root of its hierarchy, which owns the column. */
    private static Class<?> owningEntity(GeneratorCreationContext context, String where) {
        PersistentClass entity = context.getPersistentClass();
        Class<?> type = entity == null ? null : entity.getRootClass().getMappedClass();
        if (type == null) {
            throw new MappingException("@IdoraGeneratedId on " + where + " is not on an entity identifier, so there is "
                    + "no entity whose prefix to use. Give it an explicit prefix or entity, for example "
                    + "@IdoraGeneratedId(prefix = \"abc\").");
        }
        return type;
    }

    /**
     * Finds the configured generator: the {@value IdoraHibernateSettings#GENERATOR} setting, or else
     * a single {@link IdoraGeneratorProvider} service. Returns {@code null} if there is neither.
     */
    private static IdoraGenerator configuredGenerator(ServiceRegistry services, String where) {
        Object setting = services.requireService(ConfigurationService.class).getSettings().get(IdoraHibernateSettings.GENERATOR);
        ClassLoaderService classLoader = services.requireService(ClassLoaderService.class);

        if (setting != null) {
            return fromSetting(setting, classLoader, where);
        }

        Collection<IdoraGeneratorProvider> providers = classLoader.loadJavaServices(IdoraGeneratorProvider.class);
        if (providers.isEmpty()) {
            return null;
        }
        if (providers.size() > 1) {
            List<String> names = new ArrayList<>();
            providers.forEach(provider -> names.add(provider.getClass().getName()));
            throw new MappingException("Several IdoraGeneratorProvider services are registered " + names + ", so the one "
                    + "for @IdoraGeneratedId on " + where + " is ambiguous. Remove all but one, or choose one with the "
                    + "Hibernate property '" + IdoraHibernateSettings.GENERATOR + "'.");
        }
        return fromProvider(providers.iterator().next(), where);
    }

    private static IdoraGenerator fromSetting(Object setting, ClassLoaderService classLoader, String where) {
        if (setting instanceof IdoraGenerator) {
            return (IdoraGenerator) setting;
        }
        if (setting instanceof IdoraGeneratorProvider) {
            return fromProvider((IdoraGeneratorProvider) setting, where);
        }
        Class<?> type;
        if (setting instanceof Class) {
            type = (Class<?>) setting;
        } else if (setting instanceof String) {
            String name = ((String) setting).trim();
            try {
                type = classLoader.classForName(name);
            } catch (RuntimeException e) {
                throw new MappingException("The Hibernate property '" + IdoraHibernateSettings.GENERATOR + "' names the "
                        + "class '" + name + "', which cannot be loaded. Name an IdoraGeneratorProvider implementation "
                        + "by its fully qualified class name.", e);
            }
        } else {
            throw new MappingException("The Hibernate property '" + IdoraHibernateSettings.GENERATOR + "' holds a "
                    + setting.getClass().getName() + ". It must be an IdoraGenerator, an IdoraGeneratorProvider, or an "
                    + "IdoraGeneratorProvider class or class name.");
        }
        if (!IdoraGeneratorProvider.class.isAssignableFrom(type)) {
            throw new MappingException("The Hibernate property '" + IdoraHibernateSettings.GENERATOR + "' names "
                    + type.getName() + ", which does not implement " + IdoraGeneratorProvider.class.getName() + ".");
        }
        IdoraGeneratorProvider provider;
        try {
            provider = (IdoraGeneratorProvider) type.getConstructor().newInstance();
        } catch (ReflectiveOperationException e) {
            throw new MappingException("Cannot instantiate the IdoraGeneratorProvider " + type.getName() + " named by the "
                    + "Hibernate property '" + IdoraHibernateSettings.GENERATOR + "'. It needs a public no-argument "
                    + "constructor.", e);
        }
        return fromProvider(provider, where);
    }

    private static IdoraGenerator fromProvider(IdoraGeneratorProvider provider, String where) {
        IdoraGenerator generator = provider.getIdoraGenerator();
        if (generator == null) {
            throw new MappingException("The IdoraGeneratorProvider " + provider.getClass().getName() + " returned null "
                    + "for @IdoraGeneratedId on " + where + ". It must return the application's IdoraGenerator.");
        }
        return generator;
    }
}
