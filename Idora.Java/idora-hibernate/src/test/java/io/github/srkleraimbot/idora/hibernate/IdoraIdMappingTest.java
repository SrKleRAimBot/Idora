package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidIdException;
import io.github.srkleraimbot.idora.hibernate.entities.Invoice;
import io.github.srkleraimbot.idora.hibernate.entities.Order;
import io.github.srkleraimbot.idora.hibernate.entities.User;
import io.github.srkleraimbot.idora.hibernate.support.Database;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.Test;

import java.sql.DatabaseMetaData;
import java.sql.ResultSet;
import java.sql.Types;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** {@link IdoraId} attributes and query parameters, through the type contributor and the converter. */
class IdoraIdMappingTest {

    private static final IdoraGenerator IDS = IdoraGenerator.builder().registerEntities(User.class, Order.class).build();

    private static final Map<String, Object> WITH_IDS = Map.of(IdoraHibernateSettings.GENERATOR, IDS);

    @Test
    void anIdoraIdAttribute_isAVarcharColumnWideEnoughForAnyIdentifier() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Act
            List<int[]> columns = database.fromSession(session -> session.doReturningWork(connection -> {
                DatabaseMetaData metadata = connection.getMetaData();
                List<int[]> found = new ArrayList<>();
                for (String column : List.of("ID", "CUSTOMERID")) {
                    try (ResultSet result = metadata.getColumns(null, null, "ORDERS", column)) {
                        assertTrue(result.next(), column);
                        found.add(new int[] {result.getInt("DATA_TYPE"), result.getInt("COLUMN_SIZE")});
                    }
                }
                return found;
            }));

            // Assert: text, not a serialized blob.
            for (int[] column : columns) {
                assertEquals(Types.VARCHAR, column[0]);
                assertEquals(IdoraIdJavaType.DEFAULT_LENGTH, column[1]);
            }
        }
    }

    @Test
    void anIdoraIdAttribute_isStoredAsItsText() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Arrange
            IdoraId customer = IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s");
            Order order = new Order(customer);
            database.inTransaction(session -> session.persist(order));

            // Act
            Object stored = database.fromSession(session -> session
                    .createNativeQuery("select customerId from orders", Object.class)
                    .getSingleResult());

            // Assert
            assertEquals(customer.toString(), stored);
        }
    }

    @Test
    void anIdoraIdQueryParameter_matchesTheStoredText() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Arrange
            IdoraId customer = IDS.generateId(User.class);
            database.inTransaction(session -> {
                session.persist(new Order(customer));
                session.persist(new Order(IDS.generateId(User.class)));
            });

            // Act
            List<Order> orders = database.fromSession(session -> session
                    .createSelectionQuery("from Order where customerId = :customer", Order.class)
                    .setParameter("customer", customer)
                    .getResultList());

            // Assert
            assertEquals(1, orders.size());
            assertEquals(customer, orders.get(0).getCustomerId());
        }
    }

    @Test
    void rangeBounds_selectExactlyTheIdentifiersMintedInASpan_withoutATimestampColumn() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Arrange: identifiers for three distinct, known milliseconds.
            Instant early = Instant.parse("2025-08-07T12:00:00Z");
            Instant middle = early.plusSeconds(60);
            Instant late = middle.plusSeconds(60);
            List<String> rows = List.of(
                    IdoraId.lowerBound("ord", early, IDS.strategy()).toString(),
                    IdoraId.upperBound("ord", middle, IDS.strategy()).toString(),
                    IdoraId.lowerBound("ord", late, IDS.strategy()).toString());
            database.inTransaction(session -> {
                for (String row : rows) {
                    session.createNativeMutationQuery("insert into orders (id) values (:id)").setParameter("id", row)
                            .executeUpdate();
                }
            });

            // Act
            List<IdoraId> found = database.fromSession(session -> session
                    .createSelectionQuery("select o.id from Order o where o.id between :from and :to order by o.id",
                            IdoraId.class)
                    .setParameter("from", IDS.lowerBound(Order.class, middle))
                    .setParameter("to", IDS.upperBound(Order.class, middle))
                    .getResultList());

            // Assert
            assertEquals(List.of(IdoraId.parse(rows.get(1))), found);
        }
    }

    @Test
    void aNullIdoraIdAttribute_staysNull() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Act
            Order order = new Order(null);
            database.inTransaction(session -> session.persist(order));
            Order found = database.fromTransaction(session -> session.find(Order.class, order.getId()));

            // Assert
            assertNull(found.getCustomerId());
        }
    }

    @Test
    void aColumnHoldingSomethingElse_failsTheReadNamingTheText() {
        try (SessionFactory database = Database.open(WITH_IDS, Order.class)) {
            // Arrange
            Order order = new Order(null);
            database.inTransaction(session -> session.persist(order));
            database.inTransaction(session -> session
                    .createNativeMutationQuery("update orders set customerId = 'customer-42'").executeUpdate());

            // Act
            RuntimeException thrown = assertThrows(RuntimeException.class,
                    () -> database.fromTransaction(session -> session.find(Order.class, order.getId())));

            // Assert
            InvalidIdException cause = Database.causeOfType(thrown, InvalidIdException.class);
            assertNotNull(cause, () -> "no InvalidIdException in " + thrown);
            assertEquals("customer-42", cause.getText());
        }
    }

    @Test
    void theConverter_roundTripsAnAttributeItIsNamedOn() {
        try (SessionFactory database = Database.open(Map.of(), Invoice.class)) {
            // Arrange
            IdoraId orderId = IdoraId.parse("ord_0198846702007011a233445566778899");

            // Act
            Invoice invoice = new Invoice(orderId);
            database.inTransaction(session -> session.persist(invoice));
            Invoice found = database.fromTransaction(session -> session.find(Invoice.class, invoice.getId()));

            // Assert
            assertEquals(orderId, found.getOrderId());
        }
    }

    @Test
    void theConverter_convertsBothWaysAndPassesNullThrough() {
        // Arrange
        IdoraIdConverter converter = new IdoraIdConverter();
        IdoraId id = IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s");

        // Act & Assert
        assertEquals(id.toString(), converter.convertToDatabaseColumn(id));
        assertEquals(id, converter.convertToEntityAttribute(id.toString()));
        assertNull(converter.convertToDatabaseColumn(null));
        assertNull(converter.convertToEntityAttribute(null));
        assertThrows(InvalidIdException.class, () -> converter.convertToEntityAttribute("usr_"));
    }

    @Test
    void theJavaType_wrapsAndUnwrapsTextAndIdentifiers() {
        // Arrange
        IdoraIdJavaType type = IdoraIdJavaType.INSTANCE;
        IdoraId id = IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s");

        // Act & Assert
        assertEquals(id.toString(), type.unwrap(id, String.class, null));
        assertEquals(id.toString(), type.unwrap(id, CharSequence.class, null));
        assertEquals(id, type.unwrap(id, IdoraId.class, null));
        assertEquals(id, type.wrap(id.toString(), null));
        assertEquals(id, type.wrap(new StringBuilder(id.toString()), null));
        assertEquals(id, type.wrap(id, null));
        assertNull(type.wrap(null, null));
        assertNull(type.unwrap(null, String.class, null));
        assertEquals(id, type.fromString(type.toString(id)));
        assertThrows(RuntimeException.class, () -> type.wrap(42, null));
        assertThrows(RuntimeException.class, () -> type.unwrap(id, Integer.class, null));
    }
}
