package io.github.srkleraimbot.idora.spring;

import io.github.srkleraimbot.idora.DuplicateEntityPrefixException;
import io.github.srkleraimbot.idora.IdGenerationStrategy;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidEntityPrefixException;
import io.github.srkleraimbot.idora.InvalidIdException;
import io.github.srkleraimbot.idora.spring.domain.Catalog;
import io.github.srkleraimbot.idora.spring.domain.Customer;
import io.github.srkleraimbot.idora.spring.domain.DomainApplication;
import io.github.srkleraimbot.idora.spring.domain.Invoice;
import io.github.srkleraimbot.idora.spring.domain.PremiumCustomer;
import io.github.srkleraimbot.idora.spring.domain.sub.Supplier;
import io.github.srkleraimbot.idora.spring.vendor.Widget;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.convert.ApplicationConversionService;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.annotation.Order;
import org.springframework.core.convert.ConversionFailedException;
import org.springframework.format.support.FormattingConversionService;

import java.util.LinkedHashMap;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The generator bean and the converter, from the {@code idora.*} properties and the annotation scan. */
class IdoraAutoConfigurationTest {

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class));

    @Test
    void withNothingConfigured_providesAUlidGeneratorWithNoRegistrations() {
        runner.run(context -> {
            // Act
            IdoraGenerator generator = context.getBean(IdoraGenerator.class);

            // Assert
            assertEquals(IdGenerationStrategy.ULID, generator.strategy());
            assertTrue(generator.registry().registrations().isEmpty());
            assertTrue(generator.generate("tmp").startsWith("tmp_"));
        });
    }

    @Test
    void withTheApplicationsPackage_registersEveryAnnotatedTypeInItAndItsSubPackages() {
        runner.withUserConfiguration(DomainApplication.class).run(context -> {
            // Act
            Map<Class<?>, String> registrations = context.getBean(IdoraGenerator.class).registry().registrations();

            // Assert: classes, records, nested interfaces and sub-packages; never an unannotated subclass.
            Map<Class<?>, String> expected = new LinkedHashMap<>();
            expected.put(Catalog.Item.class, "itm");
            expected.put(Customer.class, "cus");
            expected.put(Invoice.class, "inv");
            expected.put(Supplier.class, "sup");
            assertEquals(expected, registrations);
            assertTrue(context.getBean(IdoraGenerator.class).registry().findPrefix(PremiumCustomer.class).isEmpty());
        });
    }

    @Test
    void registrations_areMadeInOrdinalOrderOfTheClassName() {
        runner.withUserConfiguration(DomainApplication.class).run(context -> {
            // Act
            Object[] order = context.getBean(IdoraGenerator.class).registry().registrations().keySet().toArray();

            // Assert
            assertEquals(Catalog.Item.class, order[0]);
            assertEquals(Supplier.class, order[3]);
        });
    }

    @Test
    void scanPackages_replaceTheApplicationsPackages() {
        runner.withUserConfiguration(DomainApplication.class)
                .withPropertyValues("idora.scan.packages=io.github.srkleraimbot.idora.spring.domain.sub")
                .run(context -> {
                    // Act
                    Map<Class<?>, String> registrations = context.getBean(IdoraGenerator.class).registry().registrations();

                    // Assert
                    assertEquals(Map.of(Supplier.class, "sup"), registrations);
                });
    }

    @Test
    void scanDisabled_registersNothingFromAnnotations() {
        runner.withUserConfiguration(DomainApplication.class)
                .withPropertyValues("idora.scan.enabled=false")
                .run(context -> assertTrue(context.getBean(IdoraGenerator.class).registry().registrations().isEmpty()));
    }

    @Test
    void strategy_isBoundLeniently() {
        for (String value : new String[] {"uuid-v7", "UUID_V7", "uuidv7"}) {
            runner.withPropertyValues("idora.strategy=" + value).run(context -> {
                // Act
                IdoraGenerator generator = context.getBean(IdoraGenerator.class);

                // Assert
                assertEquals(IdGenerationStrategy.UUID_V7, generator.strategy());
                assertEquals(IdGenerationStrategy.UUID_V7, IdoraId.parse(generator.generate("tmp")).format());
            });
        }
    }

    @Test
    void prefixes_registerTypesThatCannotBeAnnotated() {
        runner.withPropertyValues("idora.prefixes[" + Widget.class.getName() + "]=wdg").run(context -> {
            // Act
            IdoraGenerator generator = context.getBean(IdoraGenerator.class);

            // Assert
            assertEquals("wdg", generator.registry().getPrefix(Widget.class));
            assertTrue(generator.generate(Widget.class).startsWith("wdg_"));
        });
    }

    @Test
    void prefixes_namingAMissingClass_failsStartUpExplainingTheSyntax() {
        runner.withPropertyValues("idora.prefixes[com.example.Missing]=msg").run(context -> {
            // Act
            Throwable failure = context.getStartupFailure();

            // Assert
            IllegalStateException cause = causeOfType(failure, IllegalStateException.class);
            assertNotNull(cause, () -> "no IllegalStateException in " + failure);
            assertTrue(cause.getMessage().contains("com.example.Missing"), cause.getMessage());
            assertTrue(cause.getMessage().contains("idora.prefixes["), cause.getMessage());
        });
    }

    @Test
    void prefixes_withAnInvalidPrefix_failsStartUp() {
        runner.withPropertyValues("idora.prefixes[" + Widget.class.getName() + "]=w_g").run(context ->
                assertNotNull(causeOfType(context.getStartupFailure(), InvalidEntityPrefixException.class)));
    }

    @Test
    void prefixes_clashingWithAScannedType_failsStartUp() {
        runner.withUserConfiguration(DomainApplication.class)
                .withPropertyValues("idora.prefixes[" + Widget.class.getName() + "]=CUS")
                .run(context -> {
                    // Act
                    DuplicateEntityPrefixException cause =
                            causeOfType(context.getStartupFailure(), DuplicateEntityPrefixException.class);

                    // Assert
                    assertNotNull(cause);
                    assertSame(Widget.class, cause.getEntityType());
                    assertSame(Customer.class, cause.getConflictingEntityType());
                });
    }

    @Test
    void customizers_runInOrderAfterTheProperties() {
        runner.withUserConfiguration(Customizers.class)
                .withPropertyValues("idora.strategy=uuid-v7")
                .run(context -> {
                    // Act
                    IdoraGenerator generator = context.getBean(IdoraGenerator.class);

                    // Assert: the later customizer saw, and overrode, what the earlier one and the properties did.
                    assertEquals("wdg", generator.registry().getPrefix(Widget.class));
                    assertEquals(IdGenerationStrategy.ULID, generator.strategy());
                });
    }

    @Test
    void anApplicationGenerator_replacesTheAutoConfiguredOne() {
        runner.withUserConfiguration(OwnGenerator.class).run(context ->
                assertSame(OwnGenerator.IDS, context.getBean(IdoraGenerator.class)));
    }

    @Test
    void theConverter_parsesStrictlyAndMapsEmptyToNull() {
        runner.run(context -> {
            // Arrange: as Spring Boot assembles the MVC and WebFlux conversion services.
            FormattingConversionService conversion = new FormattingConversionService();
            ApplicationConversionService.addBeans(conversion, context.getBeanFactory());

            // Act & Assert
            assertEquals(IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s"),
                    conversion.convert("usr_01k226e0g0008j4ct4ank7f24s", IdoraId.class));
            assertNull(conversion.convert("", IdoraId.class));
            ConversionFailedException failure =
                    assertThrows(ConversionFailedException.class, () -> conversion.convert("usr_nope", IdoraId.class));
            assertInstanceOf(InvalidIdException.class, failure.getCause());
        });
    }

    static <T extends Throwable> T causeOfType(Throwable thrown, Class<T> type) {
        for (Throwable t = thrown; t != null; t = t.getCause()) {
            if (type.isInstance(t)) {
                return type.cast(t);
            }
        }
        return null;
    }

    /** Two customizers, applied in their declared order. */
    @Configuration(proxyBeanMethods = false)
    static class Customizers {

        @Bean
        @Order(2)
        IdoraGeneratorBuilderCustomizer second() {
            return builder -> builder.useUlid();
        }

        @Bean
        @Order(1)
        IdoraGeneratorBuilderCustomizer first() {
            return builder -> builder.registerEntity(Widget.class, "wdg");
        }
    }

    /** An application that builds its own generator. */
    @Configuration(proxyBeanMethods = false)
    static class OwnGenerator {

        static final IdoraGenerator IDS = IdoraGenerator.builder().registerEntity(Widget.class, "own").build();

        @Bean
        IdoraGenerator idoraGenerator() {
            return IDS;
        }
    }
}
