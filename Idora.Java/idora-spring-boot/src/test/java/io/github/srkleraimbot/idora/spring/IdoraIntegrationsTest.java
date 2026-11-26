package io.github.srkleraimbot.idora.spring;

import com.fasterxml.jackson.databind.ObjectMapper;
import io.github.srkleraimbot.idora.IdGenerationStrategy;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.hibernate.IdoraHibernateSettings;
import io.github.srkleraimbot.idora.jackson.IdoraModule;
import io.github.srkleraimbot.idora.spring.jpa.Account;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.autoconfigure.domain.EntityScan;
import org.springframework.boot.autoconfigure.http.HttpMessageConvertersAutoConfiguration;
import org.springframework.boot.autoconfigure.jackson.JacksonAutoConfiguration;
import org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration;
import org.springframework.boot.autoconfigure.orm.jpa.HibernateJpaAutoConfiguration;
import org.springframework.boot.autoconfigure.orm.jpa.HibernatePropertiesCustomizer;
import org.springframework.boot.autoconfigure.web.servlet.WebMvcAutoConfiguration;
import org.springframework.boot.test.context.FilteredClassLoader;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.boot.test.context.runner.WebApplicationContextRunner;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Import;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import java.util.HashMap;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;

/** The Jackson, Hibernate and Spring MVC integrations, each switched on by what is on the class path. */
class IdoraIntegrationsTest {

    private static final String USER = "usr_01k226e0g0008j4ct4ank7f24s";

    // ---------------------------------------------------------------------------------------------
    // Jackson
    // ---------------------------------------------------------------------------------------------

    @Test
    void jackson_theModuleIsRegisteredWithSpringBootsObjectMapper() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class, JacksonAutoConfiguration.class))
                .run(context -> {
                    // Arrange
                    ObjectMapper mapper = context.getBean(ObjectMapper.class);

                    // Act
                    IdoraId id = mapper.readValue("\"" + USER + "\"", IdoraId.class);

                    // Assert
                    assertEquals(1, context.getBeansOfType(IdoraModule.class).size());
                    assertEquals(IdoraId.parse(USER), id);
                    assertEquals("\"" + USER + "\"", mapper.writeValueAsString(id));
                });
    }

    @Test
    void jackson_withoutIdoraJackson_noModuleIsDeclared() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class, JacksonAutoConfiguration.class))
                .withClassLoader(new FilteredClassLoader(IdoraModule.class))
                .run(context -> {
                    assertTrue(context.getStartupFailure() == null, () -> String.valueOf(context.getStartupFailure()));
                    assertTrue(context.getBeansOfType(com.fasterxml.jackson.databind.Module.class).values().stream()
                            .noneMatch(module -> module.getClass().getName().equals(IdoraModule.class.getName())));
                    assertEquals(1, context.getBeansOfType(IdoraGenerator.class).size());
                });
    }

    // ---------------------------------------------------------------------------------------------
    // Hibernate
    // ---------------------------------------------------------------------------------------------

    @Test
    void hibernate_theCustomizerHandsTheGeneratorToHibernate() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class))
                .run(context -> {
                    // Act
                    Map<String, Object> properties = new HashMap<>();
                    context.getBeansOfType(HibernatePropertiesCustomizer.class).values()
                            .forEach(customizer -> customizer.customize(properties));

                    // Assert
                    assertSame(context.getBean(IdoraGenerator.class), properties.get(IdoraHibernateSettings.GENERATOR));
                });
    }

    @Test
    void hibernate_theCustomizerLeavesAnExplicitSettingAlone() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class))
                .run(context -> {
                    // Arrange
                    Map<String, Object> properties = new HashMap<>();
                    properties.put(IdoraHibernateSettings.GENERATOR, "com.example.MyProvider");

                    // Act
                    context.getBeansOfType(HibernatePropertiesCustomizer.class).values()
                            .forEach(customizer -> customizer.customize(properties));

                    // Assert
                    assertEquals("com.example.MyProvider", properties.get(IdoraHibernateSettings.GENERATOR));
                });
    }

    @Test
    void hibernate_withoutIdoraHibernate_noCustomizerIsDeclared() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(IdoraAutoConfiguration.class))
                .withClassLoader(new FilteredClassLoader(IdoraHibernateSettings.class))
                .run(context -> assertTrue(context.getBeansOfType(HibernatePropertiesCustomizer.class).isEmpty()));
    }

    @Test
    void hibernate_endToEnd_persistsAnEntityWithAnIdentifierFromTheApplicationsGenerator() {
        new ApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(
                        IdoraAutoConfiguration.class, DataSourceAutoConfiguration.class, HibernateJpaAutoConfiguration.class))
                .withUserConfiguration(JpaApplication.class)
                .withPropertyValues(
                        "spring.datasource.generate-unique-name=true",
                        "spring.jpa.hibernate.ddl-auto=create-drop",
                        "idora.strategy=uuid-v7",
                        "idora.scan.packages=io.github.srkleraimbot.idora.spring.jpa")
                .run(context -> {
                    // Arrange
                    assertTrue(context.getStartupFailure() == null, () -> String.valueOf(context.getStartupFailure()));
                    EntityManagerFactory factory = context.getBean(EntityManagerFactory.class);
                    IdoraId owner = IdoraId.parse(USER);

                    // Act
                    Account account = new Account(owner);
                    EntityManager entityManager = factory.createEntityManager();
                    try {
                        entityManager.getTransaction().begin();
                        entityManager.persist(account);
                        entityManager.getTransaction().commit();
                        entityManager.clear();

                        // Assert: the prefix from the scan, the strategy from the properties.
                        assertEquals("acc", account.getId().prefix());
                        assertEquals(IdGenerationStrategy.UUID_V7, account.getId().format());
                        assertEquals(owner, entityManager.find(Account.class, account.getId()).getOwnerId());
                    } finally {
                        entityManager.close();
                    }
                });
    }

    // ---------------------------------------------------------------------------------------------
    // Spring MVC
    // ---------------------------------------------------------------------------------------------

    @Test
    void mvc_bindsPathVariablesAndRequestParametersAndRejectsMalformedOnesWith400() {
        new WebApplicationContextRunner()
                .withConfiguration(AutoConfigurations.of(
                        IdoraAutoConfiguration.class,
                        JacksonAutoConfiguration.class,
                        HttpMessageConvertersAutoConfiguration.class,
                        WebMvcAutoConfiguration.class))
                .withUserConfiguration(WebApplication.class)
                .run(context -> {
                    // Arrange
                    MockMvc mvc = MockMvcBuilders.webAppContextSetup(context).build();

                    // Act
                    MvcResult path = mvc.perform(get("/users/" + USER)).andReturn();
                    MvcResult param = mvc.perform(get("/users").param("id", USER)).andReturn();
                    MvcResult body = mvc.perform(get("/echo/" + USER)).andReturn();
                    MvcResult malformed = mvc.perform(get("/users/usr_nope")).andReturn();
                    MvcResult blank = mvc.perform(get("/users").param("id", "")).andReturn();

                    // Assert
                    assertEquals(200, path.getResponse().getStatus());
                    assertEquals("usr 2025-08-07T12:00:00Z", path.getResponse().getContentAsString());
                    assertEquals("usr 2025-08-07T12:00:00Z", param.getResponse().getContentAsString());
                    assertEquals("{\"id\":\"" + USER + "\"}", body.getResponse().getContentAsString());
                    assertEquals(400, malformed.getResponse().getStatus());
                    assertEquals(400, blank.getResponse().getStatus());
                });
    }

    /** A JPA application whose entities live in the jpa package. */
    @Configuration(proxyBeanMethods = false)
    @EntityScan(basePackageClasses = Account.class)
    static class JpaApplication {
    }

    /** A web application with one controller. */
    @Configuration(proxyBeanMethods = false)
    @Import(UserController.class)
    static class WebApplication {
    }

    /** Takes identifiers from the path, the query string and returns one in a body. */
    @RestController
    static class UserController {

        @GetMapping("/users/{id}")
        String byPath(@PathVariable("id") IdoraId id) {
            return id.prefix() + " " + id.timestamp();
        }

        @GetMapping("/users")
        String byParameter(@RequestParam("id") IdoraId id) {
            return id.prefix() + " " + id.timestamp();
        }

        @GetMapping("/echo/{id}")
        Map<String, IdoraId> echo(@PathVariable("id") IdoraId id) {
            return Map.of("id", id);
        }
    }
}
