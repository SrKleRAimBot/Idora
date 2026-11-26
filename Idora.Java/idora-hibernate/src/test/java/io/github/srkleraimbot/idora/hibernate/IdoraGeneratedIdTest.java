package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.EntityPrefixNotRegisteredException;
import io.github.srkleraimbot.idora.IdGenerationStrategy;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidEntityPrefixException;
import io.github.srkleraimbot.idora.hibernate.entities.Animal;
import io.github.srkleraimbot.idora.hibernate.entities.Customer;
import io.github.srkleraimbot.idora.hibernate.entities.Dog;
import io.github.srkleraimbot.idora.hibernate.entities.Invoice;
import io.github.srkleraimbot.idora.hibernate.entities.Misconfigured;
import io.github.srkleraimbot.idora.hibernate.entities.Order;
import io.github.srkleraimbot.idora.hibernate.entities.Shipment;
import io.github.srkleraimbot.idora.hibernate.entities.Supplier;
import io.github.srkleraimbot.idora.hibernate.entities.User;
import io.github.srkleraimbot.idora.hibernate.support.Database;
import io.github.srkleraimbot.idora.hibernate.support.StaticProvider;
import org.hibernate.MappingException;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.net.URL;
import java.net.URLClassLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** {@link IdoraGeneratedId} against a real Hibernate session factory and an in-memory H2 database. */
class IdoraGeneratedIdTest {

    private static final IdoraGenerator IDS = IdoraGenerator.builder()
            .registerEntities(User.class, Order.class, Animal.class)
            .build();

    private static final Map<String, Object> WITH_IDS = Map.of(IdoraHibernateSettings.GENERATOR, IDS);

    // ---------------------------------------------------------------------------------------------
    // Generating
    // ---------------------------------------------------------------------------------------------

    @Test
    void persist_aStringKeyedEntity_assignsAnIdentifierWithItsRegisteredPrefix() {
        try (SessionFactory database = Database.open(WITH_IDS, User.class)) {
            // Act
            User user = new User("Ada");
            database.inTransaction(session -> session.persist(user));

            // Assert: assigned by persist, before the insert, and stored as written.
            IdoraId id = IdoraId.parse(user.getId());
            assertEquals("usr", id.prefix());
            assertEquals(IdGenerationStrategy.ULID, id.format());
            User found = database.fromTransaction(session -> session.find(User.class, user.getId()));
            assertEquals("Ada", found.getName());
        }
    }

    @Test
    void persist_anIdoraIdKeyedEntity_assignsAParsedIdentifier() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Arrange
            IdoraId customer = IDS.generateId(User.class);

            // Act
            Order order = new Order(customer);
            database.inTransaction(session -> session.persist(order));
            Order found = database.fromTransaction(session -> session.find(Order.class, order.getId()));

            // Assert
            assertEquals("ord", order.getId().prefix());
            assertEquals(order.getId(), found.getId());
            assertEquals(customer, found.getCustomerId());
        }
    }

    @Test
    void persist_usesTheConfiguredGeneratorsStrategy() {
        // Arrange
        IdoraGenerator uuids = IdoraGenerator.builder().useUuidV7().registerEntities(User.class).build();

        try (SessionFactory database = Database.open(Map.of(IdoraHibernateSettings.GENERATOR, uuids), User.class)) {
            // Act
            User user = new User("Grace");
            database.inTransaction(session -> session.persist(user));

            // Assert
            assertEquals(IdGenerationStrategy.UUID_V7, IdoraId.parse(user.getId()).format());
        }
    }

    @Test
    void persist_withAnExplicitPrefixAndNoGeneratorConfigured_usesTheDefaults() {
        try (SessionFactory database = Database.open(Map.of(), Invoice.class)) {
            // Act
            Invoice invoice = new Invoice(null);
            database.inTransaction(session -> session.persist(invoice));

            // Assert
            IdoraId id = IdoraId.parse(invoice.getId());
            assertEquals("inv", id.prefix());
            assertEquals(IdGenerationStrategy.ULID, id.format());
        }
    }

    @Test
    void persist_aSubclass_usesThePrefixOfTheHierarchyRoot() {
        try (SessionFactory database = Database.open(WITH_IDS, Animal.class, Dog.class)) {
            // Act
            Dog dog = new Dog();
            database.inTransaction(session -> session.persist(dog));

            // Assert: one identifier column, one prefix, even though Dog is not registered.
            assertTrue(dog.getId().startsWith("ani_"), dog.getId());
            assertSame(Dog.class, database.fromTransaction(session -> session.find(Animal.class, dog.getId())).getClass());
        }
    }

    @Test
    void persist_entitiesSharingAMappedSuperclassIdentifier_eachUseTheirOwnPrefix() {
        // Arrange
        IdoraGenerator ids = IdoraGenerator.builder().registerEntities(Customer.class, Supplier.class).build();

        try (SessionFactory database =
                     Database.open(Map.of(IdoraHibernateSettings.GENERATOR, ids), Customer.class, Supplier.class)) {
            // Act
            Customer customer = new Customer();
            Supplier supplier = new Supplier();
            database.inTransaction(session -> {
                session.persist(customer);
                session.persist(supplier);
            });

            // Assert
            assertTrue(customer.getId().startsWith("cus_"), customer.getId());
            assertTrue(supplier.getId().startsWith("sup_"), supplier.getId());
        }
    }

    @Test
    void persist_withAnExplicitEntity_usesThatEntitysPrefix() {
        try (SessionFactory database = Database.open(WITH_IDS, Shipment.class)) {
            // Act
            Shipment shipment = new Shipment();
            database.inTransaction(session -> session.persist(shipment));

            // Assert
            assertEquals("ord", shipment.getId().prefix());
        }
    }

    @Test
    void persist_manyInOneTransaction_givesEachADistinctIncreasingIdentifier() {
        try (SessionFactory database = Database.open(WITH_IDS, User.class)) {
            // Act
            List<User> users = database.fromTransaction(session -> {
                List<User> created = new ArrayList<>();
                for (int i = 0; i < 500; i++) {
                    User user = new User("user " + i);
                    session.persist(user);
                    created.add(user);
                }
                return created;
            });

            // Assert: ULIDs from one thread are strictly increasing, so insertion order is key order.
            Set<String> distinct = new HashSet<>();
            for (int i = 0; i < users.size(); i++) {
                assertTrue(distinct.add(users.get(i).getId()));
                if (i > 0) {
                    assertTrue(users.get(i - 1).getId().compareTo(users.get(i).getId()) < 0);
                }
            }
            assertEquals(500L, (long) database.fromTransaction(session ->
                    session.createSelectionQuery("select count(u) from User u", Long.class).getSingleResult()));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Finding the generator
    // ---------------------------------------------------------------------------------------------

    @Test
    void generator_fromAProviderInstance_isUsed() {
        // Arrange
        IdoraGeneratorProvider provider = () -> IDS;

        try (SessionFactory database = Database.open(Map.of(IdoraHibernateSettings.GENERATOR, provider), User.class)) {
            // Act
            User user = new User("Ada");
            database.inTransaction(session -> session.persist(user));

            // Assert
            assertTrue(user.getId().startsWith("usr_"));
        }
    }

    @Test
    void generator_fromAProviderClassOrClassName_isInstantiatedAndUsed() {
        for (Object setting : List.of(StaticProvider.class, StaticProvider.class.getName(), " " + StaticProvider.class.getName())) {
            try (SessionFactory database = Database.open(Map.of(IdoraHibernateSettings.GENERATOR, setting), User.class)) {
                // Act
                User user = new User("Ada");
                database.inTransaction(session -> session.persist(user));

                // Assert: StaticProvider's generator is the UUIDv7 one.
                assertEquals(IdGenerationStrategy.UUID_V7, IdoraId.parse(user.getId()).format());
            }
        }
    }

    @Test
    void generator_fromTheServiceLoader_isUsedWhenNothingIsConfigured(@TempDir Path services) throws Exception {
        // Arrange: a class loader that sees one META-INF/services registration.
        Path file = services.resolve("META-INF/services/" + IdoraGeneratorProvider.class.getName());
        Files.createDirectories(file.getParent());
        Files.writeString(file, StaticProvider.class.getName() + "\n", StandardCharsets.UTF_8);

        try (URLClassLoader loader = new URLClassLoader(new URL[] {services.toUri().toURL()}, getClass().getClassLoader());
             SessionFactory database = Database.open(loader, Map.of(), User.class)) {
            // Act
            User user = new User("Ada");
            database.inTransaction(session -> session.persist(user));

            // Assert
            assertEquals(IdGenerationStrategy.UUID_V7, IdoraId.parse(user.getId()).format());
        }
    }

    @Test
    void generator_withSeveralServiceLoaderProviders_failsAsAmbiguous(@TempDir Path services) throws Exception {
        // Arrange
        Path file = services.resolve("META-INF/services/" + IdoraGeneratorProvider.class.getName());
        Files.createDirectories(file.getParent());
        Files.writeString(file, StaticProvider.class.getName() + "\n" + OtherProvider.class.getName() + "\n",
                StandardCharsets.UTF_8);

        try (URLClassLoader loader = new URLClassLoader(new URL[] {services.toUri().toURL()}, getClass().getClassLoader())) {
            // Act
            RuntimeException thrown = assertThrows(RuntimeException.class, () -> Database.open(loader, Map.of(), User.class));

            // Assert
            MappingException cause = Database.causeOfType(thrown, MappingException.class);
            assertNotNull(cause, () -> "no MappingException in " + thrown);
            assertTrue(cause.getMessage().contains("ambiguous"), cause.getMessage());
        }
    }

    @Test
    void generator_settingOfAnUnsupportedKind_failsStartUpSayingWhatIsAccepted() {
        // Act
        MappingException cause = startUpFailure(Map.of(IdoraHibernateSettings.GENERATOR, 42), User.class);

        // Assert
        assertTrue(cause.getMessage().contains("java.lang.Integer"), cause.getMessage());
        assertTrue(cause.getMessage().contains("IdoraGeneratorProvider"), cause.getMessage());
    }

    @Test
    void generator_namingAClassThatIsNotAProvider_failsStartUp() {
        // Act & Assert
        assertTrue(startUpFailure(Map.of(IdoraHibernateSettings.GENERATOR, String.class.getName()), User.class)
                .getMessage().contains("does not implement"));
        assertTrue(startUpFailure(Map.of(IdoraHibernateSettings.GENERATOR, "com.example.Missing"), User.class)
                .getMessage().contains("cannot be loaded"));
        assertTrue(startUpFailure(Map.of(IdoraHibernateSettings.GENERATOR, (IdoraGeneratorProvider) () -> null), User.class)
                .getMessage().contains("returned null"));
    }

    // ---------------------------------------------------------------------------------------------
    // Misconfiguration fails start-up, not the first insert
    // ---------------------------------------------------------------------------------------------

    @Test
    void startUp_withoutAGeneratorForARegistryLookup_failsExplainingTheOptions() {
        // Act
        MappingException cause = startUpFailure(Map.of(), User.class);

        // Assert
        assertTrue(cause.getMessage().contains(IdoraHibernateSettings.GENERATOR), cause.getMessage());
        assertTrue(cause.getMessage().contains(User.class.getName()), cause.getMessage());
    }

    @Test
    void startUp_forAnUnregisteredEntity_failsNamingIt() {
        // Act
        RuntimeException thrown =
                assertThrows(RuntimeException.class, () -> Database.open(WITH_IDS, Misconfigured.Unregistered.class));

        // Assert
        EntityPrefixNotRegisteredException cause = Database.causeOfType(thrown, EntityPrefixNotRegisteredException.class);
        assertNotNull(cause, () -> "no EntityPrefixNotRegisteredException in " + thrown);
        assertSame(Misconfigured.Unregistered.class, cause.getEntityType());
    }

    @Test
    void startUp_withAnInvalidExplicitPrefix_fails() {
        // Act
        RuntimeException thrown =
                assertThrows(RuntimeException.class, () -> Database.open(Map.of(), Misconfigured.BadPrefix.class));

        // Assert
        assertNotNull(Database.causeOfType(thrown, InvalidEntityPrefixException.class), () -> "in " + thrown);
    }

    @Test
    void startUp_withBothAPrefixAndAnEntity_fails() {
        // Act & Assert
        assertTrue(startUpFailure(WITH_IDS, Misconfigured.BothAttributes.class).getMessage().contains("both"));
    }

    @Test
    void startUp_onANumericIdentifier_failsNamingTheType() {
        // Act & Assert
        assertTrue(startUpFailure(WITH_IDS, Misconfigured.NumericId.class).getMessage().contains("java.lang.Long"));
    }

    private static MappingException startUpFailure(Map<String, Object> settings, Class<?>... entities) {
        RuntimeException thrown = assertThrows(RuntimeException.class, () -> Database.open(settings, entities));
        MappingException cause = Database.causeOfType(thrown, MappingException.class);
        assertNotNull(cause, () -> "no MappingException in " + thrown);
        return cause;
    }

    /** A second provider, to make the service loader ambiguous. */
    public static final class OtherProvider implements IdoraGeneratorProvider {
        @Override
        public IdoraGenerator getIdoraGenerator() {
            return IDS;
        }
    }
}
