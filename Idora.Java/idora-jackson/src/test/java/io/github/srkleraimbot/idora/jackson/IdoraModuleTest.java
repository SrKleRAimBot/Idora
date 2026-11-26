package io.github.srkleraimbot.idora.jackson;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.exc.InvalidDefinitionException;
import com.fasterxml.jackson.databind.exc.InvalidFormatException;
import com.fasterxml.jackson.databind.exc.MismatchedInputException;
import com.fasterxml.jackson.databind.json.JsonMapper;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.InvalidEntityPrefixException;
import io.github.srkleraimbot.idora.InvalidIdException;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Reading and writing {@link IdoraId} values with Jackson databind. */
class IdoraModuleTest {

    private static final String USER = "usr_01k226e0g0008j4ct4ank7f24s";
    private static final String ORDER = "ord_0198846702007011a233445566778899";

    private final ObjectMapper mapper = JsonMapper.builder().addModule(new IdoraModule()).build();

    // ---------------------------------------------------------------------------------------------
    // Writing
    // ---------------------------------------------------------------------------------------------

    @Test
    void write_aRootIdentifier_isAJsonString() throws Exception {
        // Act
        String json = mapper.writeValueAsString(IdoraId.parse(USER));

        // Assert
        assertEquals("\"" + USER + "\"", json);
    }

    @Test
    void write_aRecord_writesItsIdentifiersAsStrings() throws Exception {
        // Arrange
        Transfer transfer = new Transfer(IdoraId.parse(USER), IdoraId.parse(ORDER), null);

        // Act
        String json = mapper.writeValueAsString(transfer);

        // Assert
        assertEquals("{\"from\":\"" + USER + "\",\"to\":\"" + ORDER + "\",\"approvers\":null}", json);
    }

    @Test
    void write_aMapKeyedByIdentifier_usesTheTextAsTheKey() throws Exception {
        // Arrange
        Map<IdoraId, Integer> quantities = new LinkedHashMap<>();
        quantities.put(IdoraId.parse(ORDER), 3);

        // Act
        String json = mapper.writeValueAsString(quantities);

        // Assert
        assertEquals("{\"" + ORDER + "\":3}", json);
    }

    // ---------------------------------------------------------------------------------------------
    // Reading
    // ---------------------------------------------------------------------------------------------

    @Test
    void read_aRootIdentifier_parsesIt() throws Exception {
        // Act
        IdoraId id = mapper.readValue("\"" + USER + "\"", IdoraId.class);

        // Assert
        assertEquals(IdoraId.parse(USER), id);
    }

    @Test
    void read_roundTripsWhatAGeneratorMints() throws Exception {
        // Arrange
        List<IdoraId> ids = List.of(
                IdoraGenerator.create().generateId("usr"),
                IdoraGenerator.builder().useUuidV7().build().generateId("ord"));

        // Act
        List<IdoraId> copy = mapper.readValue(mapper.writeValueAsString(ids), new TypeReference<List<IdoraId>>() { });

        // Assert
        assertEquals(ids, copy);
    }

    @Test
    void read_null_staysNull() throws Exception {
        // Act
        Transfer transfer = mapper.readValue("{\"from\":null,\"to\":\"" + ORDER + "\"}", Transfer.class);

        // Assert
        assertNull(transfer.from());
        assertEquals(IdoraId.parse(ORDER), transfer.to());
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "usr", "usr_", " usr_01k226e0g0008j4ct4ank7f24s", "usr_01K226E0G0008J4CT4ANK7F24S"})
    void read_aMalformedIdentifier_failsWithInvalidFormatCarryingTheReason(String text) {
        // Act
        InvalidFormatException exception = assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"from\":\"" + text + "\"}", Transfer.class));

        // Assert
        assertEquals(text, exception.getValue());
        assertSame(IdoraId.class, exception.getTargetType());
        assertInstanceOf(InvalidIdException.class, exception.getCause());
        assertEquals("from", exception.getPath().get(0).getFieldName());
        assertTrue(exception.getMessage().contains("from"), exception.getMessage());
    }

    @Test
    void read_aLongMalformedIdentifier_doesNotRepeatItInTheMessage_asAValueOrAMapKey() {
        // Arrange: input from a request, rejected. Idora quotes at most 64 characters of it, and
        // the Jackson exception must not quote it again at length; a marker past character 100
        // shows whether it did.
        String text = "usr_" + "x".repeat(100) + "MARKER" + "x".repeat(1000);

        // Act
        InvalidFormatException asValue = assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"from\":\"" + text + "\"}", Transfer.class));
        InvalidFormatException asKey = assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"" + text + "\":3}", new TypeReference<Map<IdoraId, Integer>>() { }));

        // Assert
        for (InvalidFormatException exception : List.of(asValue, asKey)) {
            assertFalse(exception.getMessage().contains("MARKER"), exception.getMessage());
            assertEquals(text, exception.getValue());
            assertInstanceOf(InvalidIdException.class, exception.getCause());
        }
    }

    @ParameterizedTest
    @ValueSource(strings = {"42", "true", "{}", "[]"})
    void read_somethingOtherThanAString_failsWithMismatchedInput(String json) {
        // Act & Assert
        MismatchedInputException exception =
                assertThrows(MismatchedInputException.class, () -> mapper.readValue(json, IdoraId.class));
        assertSame(IdoraId.class, exception.getTargetType());
    }

    @Test
    void read_aMapKeyedByIdentifier_parsesTheKeys() throws Exception {
        // Act
        Map<IdoraId, Integer> quantities =
                mapper.readValue("{\"" + ORDER + "\":3}", new TypeReference<Map<IdoraId, Integer>>() { });

        // Assert
        assertEquals(Map.of(IdoraId.parse(ORDER), 3), quantities);
    }

    @Test
    void read_aMalformedMapKey_failsWithInvalidFormat() {
        // Act
        InvalidFormatException exception = assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"ord-1\":3}", new TypeReference<Map<IdoraId, Integer>>() { }));

        // Assert
        assertInstanceOf(InvalidIdException.class, exception.getCause());
        assertEquals("ord-1", exception.getValue());
    }

    // ---------------------------------------------------------------------------------------------
    // @IdoraIdPrefix
    // ---------------------------------------------------------------------------------------------

    @Test
    void prefix_onARecordComponent_acceptsTheExpectedPrefix() throws Exception {
        // Act
        Payment payment = mapper.readValue("{\"payer\":\"" + USER + "\"}", Payment.class);

        // Assert
        assertEquals(IdoraId.parse(USER), payment.payer());
    }

    @Test
    void prefix_onARecordComponent_rejectsAnotherPrefixNamingTheProperty() {
        // Act
        InvalidFormatException exception = assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"payer\":\"" + ORDER + "\"}", Payment.class));

        // Assert
        InvalidIdException cause = (InvalidIdException) exception.getCause();
        assertEquals(InvalidIdException.Reason.PREFIX_MISMATCH, cause.getReason());
        assertEquals("usr", cause.getExpectedPrefix().orElseThrow());
        assertEquals("payer", exception.getPath().get(0).getFieldName());
    }

    @Test
    void prefix_comparesWithCaseIncluded() {
        // Act & Assert
        assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"payer\":\"USR_01k226e0g0008j4ct4ank7f24s\"}", Payment.class));
    }

    @Test
    void prefix_onAListProperty_appliesToEveryElement() throws Exception {
        // Arrange
        String good = "{\"approvers\":[\"" + USER + "\",\"" + USER + "\"]}";
        String bad = "{\"approvers\":[\"" + USER + "\",\"" + ORDER + "\"]}";

        // Act
        Transfer transfer = mapper.readValue(good, Transfer.class);
        InvalidFormatException exception = assertThrows(InvalidFormatException.class, () -> mapper.readValue(bad, Transfer.class));

        // Assert
        assertEquals(2, transfer.approvers().size());
        assertEquals(1, exception.getPath().get(1).getIndex());
    }

    @Test
    void prefix_onAnArrayProperty_appliesToEveryElement() throws Exception {
        // Act
        Ledger ledger = mapper.readValue("{\"auditors\":[\"" + USER + "\"]}", Ledger.class);

        // Assert
        assertEquals(IdoraId.parse(USER), ledger.auditors[0]);
        assertThrows(InvalidFormatException.class,
                () -> mapper.readValue("{\"auditors\":[\"" + ORDER + "\"]}", Ledger.class));
    }

    @Test
    void prefix_onAMapProperty_appliesToKeysAndValues() throws Exception {
        // Arrange
        String good = "{\"owners\":{\"" + USER + "\":\"" + USER + "\"}}";
        String badKey = "{\"owners\":{\"" + ORDER + "\":\"" + USER + "\"}}";
        String badValue = "{\"owners\":{\"" + USER + "\":\"" + ORDER + "\"}}";

        // Act
        Ledger ledger = mapper.readValue(good, Ledger.class);

        // Assert
        assertEquals(1, ledger.owners.size());
        assertThrows(InvalidFormatException.class, () -> mapper.readValue(badKey, Ledger.class));
        assertThrows(InvalidFormatException.class, () -> mapper.readValue(badValue, Ledger.class));
    }

    @Test
    void prefix_onASetterAndOnACreatorParameter_isHonoured() throws Exception {
        // Act & Assert: a setter.
        assertEquals(IdoraId.parse(ORDER), mapper.readValue("{\"order\":\"" + ORDER + "\"}", Ledger.class).order);
        assertThrows(InvalidFormatException.class, () -> mapper.readValue("{\"order\":\"" + USER + "\"}", Ledger.class));

        // Act & Assert: an explicit creator.
        assertEquals(IdoraId.parse(ORDER), mapper.readValue("{\"order\":\"" + ORDER + "\"}", Shipment.class).order);
        assertThrows(InvalidFormatException.class, () -> mapper.readValue("{\"order\":\"" + USER + "\"}", Shipment.class));
    }

    @Test
    void prefix_thatIsItselfInvalid_failsAsABadDefinitionNamingTheProperty() {
        // Act
        InvalidDefinitionException exception = assertThrows(InvalidDefinitionException.class,
                () -> mapper.readValue("{\"id\":\"" + USER + "\"}", Misannotated.class));

        // Assert
        assertInstanceOf(InvalidEntityPrefixException.class, exception.getCause());
        assertTrue(exception.getMessage().contains("'id'"), exception.getMessage());
        assertTrue(exception.getMessage().contains(Misannotated.class.getName()), exception.getMessage());
    }

    @Test
    void prefix_hasNoEffectOnSerialization() throws Exception {
        // Act: a payer with the "wrong" prefix still writes; the annotation guards input only.
        String json = mapper.writeValueAsString(new Payment(IdoraId.parse(ORDER)));

        // Assert
        assertEquals("{\"payer\":\"" + ORDER + "\"}", json);
    }

    // ---------------------------------------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------------------------------------

    @Test
    void findAndRegisterModules_discoversTheModuleThroughTheServiceLoader() throws Exception {
        // Arrange
        ObjectMapper discovered = new ObjectMapper().findAndRegisterModules();

        // Act
        IdoraId id = discovered.readValue("\"" + USER + "\"", IdoraId.class);

        // Assert
        assertEquals(IdoraId.parse(USER), id);
        assertTrue(discovered.getRegisteredModuleIds().contains(IdoraModule.class.getName()));
    }

    @Test
    void registeringTwice_isHarmless() throws Exception {
        // Arrange
        ObjectMapper twice = JsonMapper.builder().addModule(new IdoraModule()).addModule(new IdoraModule()).build();

        // Act & Assert
        assertEquals(1, twice.getRegisteredModuleIds().size());
        assertEquals("\"" + USER + "\"", twice.writeValueAsString(IdoraId.parse(USER)));
    }

    @Test
    void withoutTheModule_anIdentifierIsNotReadAsAString() {
        // Act & Assert: the module is what makes a plain string acceptable.
        assertThrows(InvalidDefinitionException.class, () -> new ObjectMapper().readValue("\"" + USER + "\"", IdoraId.class));
    }

    @Test
    void module_reportsItsName() {
        // Act
        IdoraModule module = new IdoraModule();

        // Assert
        assertEquals("IdoraModule", module.getModuleName());
        // From target/classes there is no jar manifest to read a version from.
        assertTrue(module.version().isUnknownVersion() || "idora-jackson".equals(module.version().getArtifactId()));
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    /** A record with plain identifiers and an annotated list. */
    record Transfer(IdoraId from, IdoraId to, @IdoraIdPrefix("usr") List<IdoraId> approvers) {
    }

    /** A record with an annotated component. */
    record Payment(@IdoraIdPrefix("usr") IdoraId payer) {
    }

    /** A bean with an annotated map field and an annotated setter. */
    static final class Ledger {
        @IdoraIdPrefix("usr")
        public Map<IdoraId, IdoraId> owners;

        @IdoraIdPrefix("usr")
        public IdoraId[] auditors;

        IdoraId order;

        @IdoraIdPrefix("ord")
        public void setOrder(IdoraId order) {
            this.order = order;
        }
    }

    /** A class with an explicit creator. */
    static final class Shipment {
        final IdoraId order;

        @JsonCreator
        Shipment(@JsonProperty("order") @IdoraIdPrefix("ord") IdoraId order) {
            this.order = order;
        }
    }

    /** An annotation naming a prefix no identifier could carry. */
    static final class Misannotated {
        @IdoraIdPrefix("us_r")
        public IdoraId id;
    }
}
