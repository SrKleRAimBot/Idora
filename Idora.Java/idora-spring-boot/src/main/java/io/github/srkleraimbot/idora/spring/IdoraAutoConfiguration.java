package io.github.srkleraimbot.idora.spring;

import com.fasterxml.jackson.databind.ObjectMapper;
import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.hibernate.IdoraHibernateSettings;
import io.github.srkleraimbot.idora.jackson.IdoraModule;
import org.springframework.beans.factory.BeanFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.AnnotatedBeanDefinition;
import org.springframework.beans.factory.config.BeanDefinition;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.autoconfigure.AutoConfigurationPackages;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.boot.autoconfigure.orm.jpa.HibernatePropertiesCustomizer;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.ClassPathScanningCandidateComponentProvider;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ResourceLoader;
import org.springframework.core.type.filter.AnnotationTypeFilter;
import org.springframework.util.ClassUtils;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Auto-configures Idora for a Spring Boot application.
 *
 * <ul>
 *   <li>An {@link IdoraGenerator} bean, unless the application declares its own, built from the
 *       {@code idora.*} properties ({@link IdoraProperties}): the strategy; every
 *       {@link EntityPrefix} type in the scanned packages, which default to the application's own,
 *       as {@code @EntityScan} does; the explicit {@code idora.prefixes}; and finally any
 *       {@link IdoraGeneratorBuilderCustomizer} beans.
 *   <li>A {@link StringToIdoraIdConverter}, which Spring Boot adds to the MVC and WebFlux
 *       conversion services, so {@code @PathVariable IdoraId id} and
 *       {@code @RequestParam IdoraId id} work.
 *   <li>When {@code idora-jackson} is present, its {@link IdoraModule}, which Spring Boot registers
 *       with the application's {@link ObjectMapper}.
 *   <li>When {@code idora-hibernate} is present, a {@link HibernatePropertiesCustomizer} handing
 *       the generator to Hibernate, so {@code @IdoraGeneratedId} identifiers use the same registry
 *       and strategy as the rest of the application.
 * </ul>
 *
 * <p>Annotated types are found by reading class files, without initialising any class, through
 * Spring's own class path scanning, which understands Spring Boot's nested jars.
 */
@AutoConfiguration
@ConditionalOnClass(IdoraGenerator.class)
@EnableConfigurationProperties(IdoraProperties.class)
public class IdoraAutoConfiguration {

    /** Created by Spring. */
    public IdoraAutoConfiguration() {
    }

    /**
     * The application's generator.
     *
     * @param properties the {@code idora.*} properties
     * @param customizers the builder customizers, applied in order after the properties
     * @param beanFactory the bean factory, for the application's auto-configuration packages
     * @param environment the environment, for class path scanning
     * @param resourceLoader the resource loader, for class path scanning and class loading
     * @return the generator
     * @throws IllegalStateException if {@code idora.prefixes} names a class that cannot be loaded
     * @throws io.github.srkleraimbot.idora.IdoraException if a prefix is invalid or claimed twice
     */
    @Bean
    @ConditionalOnMissingBean
    public IdoraGenerator idoraGenerator(
            IdoraProperties properties,
            ObjectProvider<IdoraGeneratorBuilderCustomizer> customizers,
            BeanFactory beanFactory,
            Environment environment,
            ResourceLoader resourceLoader) {
        IdoraGenerator.Builder builder = IdoraGenerator.builder().strategy(properties.getStrategy());
        ClassLoader classLoader = resourceLoader.getClassLoader();

        if (properties.getScan().isEnabled()) {
            List<String> packages = properties.getScan().getPackages().isEmpty() && AutoConfigurationPackages.has(beanFactory)
                    ? AutoConfigurationPackages.get(beanFactory)
                    : properties.getScan().getPackages();
            builder.registerEntities(annotatedTypes(packages, environment, resourceLoader, classLoader));
        }

        for (Map.Entry<String, String> registration : properties.getPrefixes().entrySet()) {
            builder.registerEntity(load(registration.getKey(), classLoader), registration.getValue());
        }

        customizers.orderedStream().forEach(customizer -> customizer.customize(builder));
        return builder.build();
    }

    /**
     * Converts text to {@link io.github.srkleraimbot.idora.IdoraId} for web request binding.
     *
     * @return the converter
     */
    @Bean
    @ConditionalOnMissingBean
    public StringToIdoraIdConverter stringToIdoraIdConverter() {
        return new StringToIdoraIdConverter();
    }

    /** Every {@link EntityPrefix} type in the packages, found from class files without initialising them. */
    private static Set<Class<?>> annotatedTypes(
            List<String> packages, Environment environment, ResourceLoader resourceLoader, ClassLoader classLoader) {
        ClassPathScanningCandidateComponentProvider scanner = new ClassPathScanningCandidateComponentProvider(false, environment) {
            @Override
            protected boolean isCandidateComponent(AnnotatedBeanDefinition definition) {
                // Interfaces, abstract classes, records, enums and nested types may all carry a prefix.
                return true;
            }
        };
        scanner.setResourceLoader(resourceLoader);
        // Declared on the type itself: the annotation is not inherited, and neither is the prefix.
        scanner.addIncludeFilter(new AnnotationTypeFilter(EntityPrefix.class, false, false));

        Set<Class<?>> types = new LinkedHashSet<>();
        for (String basePackage : packages) {
            for (BeanDefinition candidate : scanner.findCandidateComponents(basePackage)) {
                types.add(load(candidate.getBeanClassName(), classLoader));
            }
        }
        return types;
    }

    private static Class<?> load(String className, ClassLoader classLoader) {
        try {
            // Loaded but not initialised: no static initialiser runs.
            return ClassUtils.forName(className, classLoader);
        } catch (ClassNotFoundException | LinkageError e) {
            throw new IllegalStateException("Idora cannot load the class '" + className + "'. Check the class names in "
                    + "idora.prefixes: each key must be a fully qualified class name, written in brackets, for example "
                    + "idora.prefixes[com.example.Widget]=wdg.", e);
        }
    }

    /** Registers {@link IdoraModule} when {@code idora-jackson} and Jackson are on the class path. */
    @Configuration(proxyBeanMethods = false)
    @ConditionalOnClass({ObjectMapper.class, IdoraModule.class})
    public static class JacksonConfiguration {

        /** Created by Spring. */
        public JacksonConfiguration() {
        }

        /**
         * The Jackson module, which Spring Boot registers with its {@link ObjectMapper}.
         *
         * @return the module
         */
        @Bean
        @ConditionalOnMissingBean
        public IdoraModule idoraJacksonModule() {
            return new IdoraModule();
        }
    }

    /** Hands the generator to Hibernate when {@code idora-hibernate} and Spring Boot's JPA support are present. */
    @Configuration(proxyBeanMethods = false)
    @ConditionalOnClass(name = {
        "org.springframework.boot.autoconfigure.orm.jpa.HibernatePropertiesCustomizer",
        "io.github.srkleraimbot.idora.hibernate.IdoraHibernateSettings"})
    public static class HibernateConfiguration {

        /** Created by Spring. */
        public HibernateConfiguration() {
        }

        /**
         * Sets {@value IdoraHibernateSettings#GENERATOR} to the application's generator, unless
         * {@code spring.jpa.properties} already sets it.
         *
         * @param generator the application's generator
         * @return the customizer
         */
        @Bean
        public HibernatePropertiesCustomizer idoraHibernatePropertiesCustomizer(IdoraGenerator generator) {
            return hibernateProperties -> hibernateProperties.putIfAbsent(IdoraHibernateSettings.GENERATOR, generator);
        }
    }
}
