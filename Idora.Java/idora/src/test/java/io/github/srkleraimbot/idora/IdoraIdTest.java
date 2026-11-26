package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.InvalidIdException.Reason;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;
import org.junit.jupiter.params.provider.ValueSource;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InvalidObjectException;
import java.io.ObjectInputStream;
import java.io.ObjectOutputStream;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.TreeSet;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Parsing, reading, comparing and bounding identifiers (specification section 6). */
class IdoraIdTest {

    private static final String ULID_ID = "usr_01k226e0g0008j4ct4ank7f24s";
    private static final long ULID_MILLIS = 1754568000000L;
    private static final String UUID_ID = "inv_0198846702007011a233445566778899";
    private static final long MAX_TIMESTAMP = (1L << 48) - 1;

    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    @Test
    void parse_aUlidIdentifier_exposesEveryPart() {
        // Act
        IdoraId id = IdoraId.parse(ULID_ID);

        // Assert
        assertEquals("usr", id.prefix());
        assertEquals("01k226e0g0008j4ct4ank7f24s", id.value());
        assertEquals(IdGenerationStrategy.ULID, id.format());
        assertEquals(ULID_MILLIS, id.timestampMillis());
        assertEquals(Instant.parse("2025-08-07T12:00:00Z"), id.timestamp());
        assertEquals(ULID_ID, id.toString());
    }

    @Test
    void parse_aUuidV7Identifier_exposesEveryPart() {
        // Act
        IdoraId id = IdoraId.parse(UUID_ID);

        // Assert
        assertEquals("inv", id.prefix());
        assertEquals("0198846702007011a233445566778899", id.value());
        assertEquals(IdGenerationStrategy.UUID_V7, id.format());
        assertEquals(ULID_MILLIS, id.timestampMillis());
    }

    @Test
    void parse_aCharSequenceOtherThanAString_parsesItsText() {
        // Act
        IdoraId id = IdoraId.parse(new StringBuilder(ULID_ID));

        // Assert
        assertEquals(IdoraId.parse(ULID_ID), id);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void parse_whatAGeneratorEmits_roundTripsWithTheCurrentTime(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).build();
        long before = System.currentTimeMillis();

        // Act
        String text = generator.generate("evt");
        long after = System.currentTimeMillis();
        IdoraId id = IdoraId.parse(text);

        // Assert
        assertEquals(text, id.toString());
        assertEquals(strategy, id.format());
        assertTrue(id.timestampMillis() >= before && id.timestampMillis() <= after, id.timestamp().toString());
    }

    @Test
    void parse_withANullText_throwsNullPointerExceptionNamingTheParameter() {
        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> IdoraId.parse(null));

        // Assert
        assertEquals("text", exception.getMessage());
    }

    @Test
    void parse_withANullExpectedPrefix_throwsNullPointerExceptionNamingTheParameter() {
        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> IdoraId.parse(ULID_ID, null));

        // Assert
        assertEquals("expectedPrefix", exception.getMessage());
    }

    @Test
    void parse_withAnInvalidExpectedPrefix_rejectsTheArgumentRatherThanTheIdentifier() {
        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> IdoraId.parse(ULID_ID, "us_r"));

        // Assert
        assertEquals(InvalidEntityPrefixException.Reason.SEPARATOR, exception.getReason());
    }

    @Test
    void parse_splitsOnTheFirstSeparator_soASecondOneLandsInTheValue() {
        // Act
        InvalidIdException exception =
                assertThrows(InvalidIdException.class, () -> IdoraId.parse("usr_01k226e0g0008j4ct4a_k7f24s"));

        // Assert
        assertEquals(Reason.VALUE, exception.getReason());
    }

    @Test
    void parse_withABadPrefix_attachesThePrefixFailureAsTheCause() {
        // Act
        InvalidIdException exception =
                assertThrows(InvalidIdException.class, () -> IdoraId.parse("us!r_01k226e0g0008j4ct4ank7f24s"));

        // Assert
        assertEquals(Reason.PREFIX, exception.getReason());
        InvalidEntityPrefixException cause = (InvalidEntityPrefixException) exception.getCause();
        assertEquals(InvalidEntityPrefixException.Reason.CHARACTER, cause.getReason());
        assertEquals("us!r", cause.getPrefix());
        assertEquals(2, cause.getIndex().getAsInt());
        assertTrue(exception.getMessage().contains("index 2"), exception.getMessage());
    }

    @Test
    void parse_whenTheTextIsMalformedAndTheWrongPrefix_reportsTheMalformation() {
        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> IdoraId.parse("ord_nope", "usr"));

        // Assert: the identifier is checked fully before the prefix is compared.
        assertEquals(Reason.VALUE, exception.getReason());
        assertEquals(Optional.of("usr"), exception.getExpectedPrefix());
    }

    @Test
    void parse_withAPrefixDifferingOnlyInCase_isAMismatchAndSaysSo() {
        // Act
        InvalidIdException exception =
                assertThrows(InvalidIdException.class, () -> IdoraId.parse("USR_01k226e0g0008j4ct4ank7f24s", "usr"));

        // Assert
        assertEquals(Reason.PREFIX_MISMATCH, exception.getReason());
        assertTrue(exception.getMessage().contains("case included"), exception.getMessage());
    }

    @Test
    void parse_withTheExpectedPrefix_accepts() {
        // Act
        IdoraId id = IdoraId.parse(ULID_ID, "usr");

        // Assert
        assertEquals("usr", id.prefix());
    }

    @Test
    void parse_aTextWithNoPrefixMismatch_hasNoExpectedPrefixOnItsException() {
        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> IdoraId.parse("nope"));

        // Assert
        assertEquals(Reason.NO_SEPARATOR, exception.getReason());
        assertEquals(Optional.empty(), exception.getExpectedPrefix());
        assertNull(exception.getCause());
    }

    @ParameterizedTest
    @ValueSource(strings = {
        "usr_01K226E0G0008J4CT4ANK7F24S",
        "usr_81k226e0g0008j4ct4ank7f24s",
        "inv_00000000000140008000000000000001",
        "inv_0000000000017000c000000000000001",
        "inv_0198846702007011A233445566778899",
        "inv_01988467-0200-7011-a233-445566778899",
        "usr_01k226e0g0008j4ct4ank7f24"})
    void parse_aBadValue_explainsWhatIsWrongInTheMessage(String text) {
        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> IdoraId.parse(text));

        // Assert: every message names the text and the rule.
        assertEquals(Reason.VALUE, exception.getReason());
        assertTrue(exception.getMessage().contains(text), exception.getMessage());
        assertTrue(exception.getMessage().contains("ULID") && exception.getMessage().contains("UUIDv7"),
                exception.getMessage());
    }

    @Test
    void parse_anEnormousText_keepsTheMessageShortButTheTextWhole() {
        // Arrange
        String text = "x".repeat(100_000);

        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> IdoraId.parse(text));

        // Assert
        assertTrue(exception.getMessage().length() < 400, exception.getMessage());
        assertTrue(exception.getMessage().contains("100000 characters"), exception.getMessage());
        assertSame(text, exception.getText());
    }

    @Test
    void parse_aTextWithControlCharacters_escapesThemInTheMessage() {
        // Act
        InvalidIdException exception =
                assertThrows(InvalidIdException.class, () -> IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24\n"));

        // Assert: a crafted identifier cannot forge a second log line.
        assertFalse(exception.getMessage().contains("\n"), exception.getMessage());
        assertTrue(exception.getMessage().contains("\\u000a"), exception.getMessage());
    }

    @Test
    void tryParse_aValidIdentifier_returnsIt() {
        // Act
        Optional<IdoraId> id = IdoraId.tryParse(ULID_ID);

        // Assert
        assertEquals(Optional.of(IdoraId.parse(ULID_ID)), id);
    }

    @Test
    void tryParse_aMalformedIdentifier_returnsEmpty() {
        // Act & Assert
        assertEquals(Optional.empty(), IdoraId.tryParse("usr_"));
        assertEquals(Optional.empty(), IdoraId.tryParse(ULID_ID, "ord"));
    }

    @Test
    void tryParse_withANullText_stillThrows() {
        // Act & Assert: a null is a programming error, not a malformed identifier.
        assertThrows(NullPointerException.class, () -> IdoraId.tryParse(null));
        assertThrows(NullPointerException.class, () -> IdoraId.tryParse(null, "usr"));
        assertThrows(NullPointerException.class, () -> IdoraId.tryParse(ULID_ID, null));
    }

    @Test
    void isValid_reportsTheSameVerdictAsParse() {
        // Act & Assert
        assertTrue(IdoraId.isValid(ULID_ID));
        assertTrue(IdoraId.isValid(UUID_ID, "inv"));
        assertFalse(IdoraId.isValid(UUID_ID, "Inv"));
        assertFalse(IdoraId.isValid(" " + ULID_ID));
        assertThrows(NullPointerException.class, () -> IdoraId.isValid(null));
        assertThrows(InvalidEntityPrefixException.class, () -> IdoraId.isValid(ULID_ID, ""));
    }

    // ---------------------------------------------------------------------------------------------
    // Members
    // ---------------------------------------------------------------------------------------------

    @Test
    void hasPrefix_isExactAndOrdinal() {
        // Arrange
        IdoraId id = IdoraId.parse(ULID_ID);

        // Act & Assert
        assertTrue(id.hasPrefix("usr"));
        assertFalse(id.hasPrefix("USR"));
        assertFalse(id.hasPrefix("us"));
        assertFalse(id.hasPrefix("usr_"));
        assertFalse(id.hasPrefix(""));
        assertThrows(NullPointerException.class, () -> id.hasPrefix(null));
    }

    @Test
    void timestamp_isUtc_whateverTheDefaultTimeZone() {
        // Act: surefire runs the suite under Australia/Sydney.
        Instant timestamp = IdoraId.parse("evt_ffffffffffff7fffbfffffffffffffff").timestamp();

        // Assert
        assertEquals(Instant.ofEpochMilli(MAX_TIMESTAMP), timestamp);
        assertEquals("Australia/Sydney", java.util.TimeZone.getDefault().getID());
    }

    // ---------------------------------------------------------------------------------------------
    // Equality and order
    // ---------------------------------------------------------------------------------------------

    @Test
    void equals_comparesTheText() {
        // Arrange
        IdoraId id = IdoraId.parse(ULID_ID);

        // Act & Assert
        assertEquals(id, IdoraId.parse(new String(ULID_ID.toCharArray())));
        assertEquals(id.hashCode(), IdoraId.parse(ULID_ID).hashCode());
        assertEquals(ULID_ID.hashCode(), id.hashCode());
        assertNotEquals(id, IdoraId.parse("Usr_01k226e0g0008j4ct4ank7f24s"));
        assertNotEquals(id, ULID_ID);
        assertNotEquals(null, id);
    }

    @Test
    void compareTo_ordersByTimestampBeforeTextAcrossPrefixesAndFormats() {
        // Arrange: by text, "ord" sorts before "usr"; by time it is the other way round.
        IdoraId user = IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s");
        IdoraId order = IdoraId.parse("ord_0198846702017fee9dccbbaa99887766");

        // Act & Assert
        assertTrue(user.compareTo(order) < 0);
        assertTrue(order.compareTo(user) > 0);
        assertTrue(user.isBefore(order));
        assertTrue(order.isAfter(user));
        assertFalse(user.isAfter(order));
        assertFalse(user.isBefore(user));
        assertEquals(0, user.compareTo(IdoraId.parse(user.toString())));
    }

    @Test
    void compareTo_withinOneMillisecond_ordersByValueThenPrefix() {
        // Arrange
        IdoraId lowValue = IdoraId.parse("zzz_01k226e0g00000000000000000");
        IdoraId highValueUpper = IdoraId.parse("Ord_01k226e0g0zzzzzzzzzzzzzzzz");
        IdoraId highValueLower = IdoraId.parse("ord_01k226e0g0zzzzzzzzzzzzzzzz");

        // Act
        List<IdoraId> sorted = new ArrayList<>(List.of(highValueLower, lowValue, highValueUpper));
        sorted.sort(null);

        // Assert
        assertEquals(List.of(lowValue, highValueUpper, highValueLower), sorted);
    }

    @Test
    void compareTo_isConsistentWithEquals_soATreeSetKeepsEveryDistinctIdentifier() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();
        TreeSet<IdoraId> set = new TreeSet<>();

        // Act
        for (int i = 0; i < 1000; i++) {
            set.add(generator.generateId("a"));
            set.add(generator.generateId("b"));
        }

        // Assert
        assertEquals(2000, set.size());
    }

    @Test
    void compareTo_forUlidsFromOneThread_isGenerationOrder() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();
        List<IdoraId> generated = new ArrayList<>();
        for (int i = 0; i < 10_000; i++) {
            generated.add(generator.generateId("evt"));
        }

        // Act
        List<IdoraId> sorted = new ArrayList<>(generated);
        sorted.sort(null);

        // Assert
        assertEquals(generated, sorted);
    }

    @Test
    void compareTimestamp_tiesIdentifiersFromTheSameMillisecond() {
        // Arrange
        IdoraId ulid = IdoraId.parse("usr_01k226e0g0008j4ct4ank7f24s");
        IdoraId uuid = IdoraId.parse("inv_0198846702007011a233445566778899");
        IdoraId later = IdoraId.parse("doc_0198846702017fee9dccbbaa99887766");

        // Act & Assert
        assertEquals(0, ulid.compareTimestamp(uuid));
        assertTrue(ulid.isSameMillisecond(uuid));
        assertFalse(ulid.isSameMillisecond(later));
        assertTrue(ulid.compareTimestamp(later) < 0);
        assertTrue(later.compareTimestamp(ulid) > 0);
        assertEquals(0, IdoraId.TIMESTAMP_ORDER.compare(ulid, uuid));
        assertTrue(IdoraId.TIMESTAMP_ORDER.compare(later, ulid) > 0);
        assertNotEquals(0, ulid.compareTo(uuid));
    }

    @Test
    void comparisons_withANull_throwNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraId id = IdoraId.parse(ULID_ID);

        // Act & Assert
        assertEquals("other", assertThrows(NullPointerException.class, () -> id.isBefore(null)).getMessage());
        assertEquals("other", assertThrows(NullPointerException.class, () -> id.isAfter(null)).getMessage());
        assertEquals("other", assertThrows(NullPointerException.class, () -> id.compareTimestamp(null)).getMessage());
        assertEquals("other", assertThrows(NullPointerException.class, () -> id.isSameMillisecond(null)).getMessage());
        assertEquals("instant", assertThrows(NullPointerException.class, () -> id.isCreatedBefore(null)).getMessage());
        assertEquals("instant", assertThrows(NullPointerException.class, () -> id.isCreatedAfter(null)).getMessage());
        assertThrows(NullPointerException.class, () -> id.compareTo(null));
    }

    @Test
    void isCreatedBeforeAndAfter_areStrictAtMillisecondPrecision() {
        // Arrange: minted at 12:00:00.000.
        IdoraId id = IdoraId.parse(ULID_ID);
        Instant sameMillisecond = Instant.parse("2025-08-07T12:00:00.000999999Z");
        Instant nextMillisecond = Instant.parse("2025-08-07T12:00:00.001Z");
        Instant previousMillisecond = Instant.parse("2025-08-07T11:59:59.999999Z");

        // Act & Assert: the sub-millisecond part is truncated, so the same millisecond is neither.
        assertFalse(id.isCreatedBefore(sameMillisecond));
        assertFalse(id.isCreatedAfter(sameMillisecond));
        assertTrue(id.isCreatedBefore(nextMillisecond));
        assertFalse(id.isCreatedAfter(nextMillisecond));
        assertTrue(id.isCreatedAfter(previousMillisecond));
        assertFalse(id.isCreatedBefore(previousMillisecond));
    }

    @Test
    void isCreatedBeforeAndAfter_handleInstantsFarOutsideTheTimestampRange() {
        // Arrange
        IdoraId id = IdoraId.parse(ULID_ID);

        // Act & Assert: no overflow at the extremes of Instant.
        assertTrue(id.isCreatedBefore(Instant.MAX));
        assertTrue(id.isCreatedAfter(Instant.MIN));
        assertTrue(id.isCreatedAfter(Instant.EPOCH.minusMillis(1)));
        assertFalse(IdoraId.parse("ord_00000000000000000000000000").isCreatedAfter(Instant.EPOCH));
    }

    // ---------------------------------------------------------------------------------------------
    // Range bounds
    // ---------------------------------------------------------------------------------------------

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void bounds_encloseEveryIdentifierGeneratedInTheirSpan(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).build();
        Instant from = Instant.ofEpochMilli(System.currentTimeMillis());
        List<String> generated = new ArrayList<>();
        for (int i = 0; i < 1000; i++) {
            generated.add(generator.generate("usr"));
        }
        Instant to = Instant.ofEpochMilli(System.currentTimeMillis());

        // Act
        String lower = IdoraId.lowerBound("usr", from, strategy).toString();
        String upper = IdoraId.upperBound("usr", to, strategy).toString();

        // Assert: as plain strings, as a database column would compare them.
        for (String id : generated) {
            assertTrue(id.compareTo(lower) >= 0 && id.compareTo(upper) <= 0, id + " outside " + lower + ".." + upper);
        }
        assertTrue(IdoraId.upperBound("usr", from.minusMillis(1), strategy).toString().compareTo(generated.get(0)) < 0);
        assertTrue(IdoraId.lowerBound("usr", to.plusMillis(1), strategy).toString()
                .compareTo(generated.get(generated.size() - 1)) > 0);
    }

    @Test
    void bounds_ignoreTheSubMillisecondPartOfAnInstant() {
        // Arrange
        Instant instant = Instant.parse("2025-08-07T12:00:00.000999Z");

        // Act & Assert
        assertEquals("01k226e0g00000000000000000", IdoraId.lowerBoundValue(instant, IdGenerationStrategy.ULID));
        assertEquals("01k226e0g0zzzzzzzzzzzzzzzz", IdoraId.upperBoundValue(instant, IdGenerationStrategy.ULID));
    }

    @Test
    void bounds_areParsedIdentifiers() {
        // Act
        IdoraId lower = IdoraId.lowerBound("Inv", ULID_MILLIS, IdGenerationStrategy.UUID_V7);

        // Assert
        assertEquals("Inv", lower.prefix());
        assertEquals(IdGenerationStrategy.UUID_V7, lower.format());
        assertEquals(ULID_MILLIS, lower.timestampMillis());
        assertEquals(IdoraId.parse(lower.toString()), lower);
    }

    @ParameterizedTest
    @ValueSource(longs = {-1, 1L << 48, Long.MAX_VALUE, Long.MIN_VALUE})
    void bounds_withATimestampOutOfRange_throwIllegalArgumentException(long timestamp) {
        // Act & Assert
        for (IdGenerationStrategy strategy : IdGenerationStrategy.values()) {
            assertThrows(IllegalArgumentException.class, () -> IdoraId.lowerBoundValue(timestamp, strategy));
            assertThrows(IllegalArgumentException.class, () -> IdoraId.upperBoundValue(timestamp, strategy));
            assertThrows(IllegalArgumentException.class, () -> IdoraId.lowerBound("usr", timestamp, strategy));
            assertThrows(IllegalArgumentException.class, () -> IdoraId.upperBound("usr", timestamp, strategy));
        }
    }

    @Test
    void bounds_withAnInstantOutOfRange_throwIllegalArgumentException() {
        // Arrange
        List<Instant> outOfRange = List.of(
                Instant.EPOCH.minusNanos(1), Instant.ofEpochMilli(MAX_TIMESTAMP + 1), Instant.MAX, Instant.MIN);

        // Act & Assert
        for (Instant instant : outOfRange) {
            IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
                    () -> IdoraId.lowerBound("usr", instant, IdGenerationStrategy.ULID));
            assertTrue(exception.getMessage().contains(instant.toString()), exception.getMessage());
            assertThrows(IllegalArgumentException.class, () -> IdoraId.upperBoundValue(instant, IdGenerationStrategy.UUID_V7));
        }
    }

    @Test
    void bounds_atTheEdgesOfTheRange_areAccepted() {
        // Act & Assert
        assertEquals("0".repeat(26), IdoraId.lowerBoundValue(Instant.EPOCH, IdGenerationStrategy.ULID));
        assertEquals("7" + "z".repeat(25),
                IdoraId.upperBoundValue(Instant.ofEpochMilli(MAX_TIMESTAMP).plusNanos(999_999), IdGenerationStrategy.ULID));
    }

    @Test
    void bounds_validateThePrefix() {
        // Act & Assert
        assertThrows(InvalidEntityPrefixException.class, () -> IdoraId.lowerBound("us_r", 0, IdGenerationStrategy.ULID));
        assertThrows(InvalidEntityPrefixException.class,
                () -> IdoraId.upperBound("", Instant.EPOCH, IdGenerationStrategy.ULID));
        assertEquals("prefix", assertThrows(NullPointerException.class,
                () -> IdoraId.lowerBound(null, 0, IdGenerationStrategy.ULID)).getMessage());
        assertEquals("format", assertThrows(NullPointerException.class,
                () -> IdoraId.lowerBound("usr", 0, null)).getMessage());
        assertEquals("instant", assertThrows(NullPointerException.class,
                () -> IdoraId.upperBoundValue((Instant) null, IdGenerationStrategy.ULID)).getMessage());
    }

    // ---------------------------------------------------------------------------------------------
    // Serialization
    // ---------------------------------------------------------------------------------------------

    @Test
    void serialization_roundTripsTheIdentifier() throws Exception {
        // Arrange
        IdoraId id = IdoraId.parse(UUID_ID);

        // Act
        Object copy = deserialize(serialize(id));

        // Assert
        assertEquals(id, copy);
        assertEquals(id.format(), ((IdoraId) copy).format());
        assertEquals(id.timestampMillis(), ((IdoraId) copy).timestampMillis());
    }

    @Test
    void serialization_ofATamperedStream_parsesTheTextAgainAndRejectsIt() throws Exception {
        // Arrange: same length, so the stream stays well formed, but the value is now uppercase.
        byte[] bytes = serialize(IdoraId.parse(ULID_ID));
        replace(bytes, ULID_ID, ULID_ID.toUpperCase(java.util.Locale.ROOT).replace("USR", "usr"));

        // Act
        InvalidObjectException exception = assertThrows(InvalidObjectException.class, () -> deserialize(bytes));

        // Assert
        assertEquals(Reason.VALUE, ((InvalidIdException) exception.getCause()).getReason());
    }

    private static byte[] serialize(Object value) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        try (ObjectOutputStream out = new ObjectOutputStream(bytes)) {
            out.writeObject(value);
        }
        return bytes.toByteArray();
    }

    private static Object deserialize(byte[] bytes) throws IOException, ClassNotFoundException {
        try (ObjectInputStream in = new ObjectInputStream(new ByteArrayInputStream(bytes))) {
            return in.readObject();
        }
    }

    private static void replace(byte[] haystack, String needle, String replacement) {
        byte[] from = needle.getBytes(StandardCharsets.US_ASCII);
        byte[] to = replacement.getBytes(StandardCharsets.US_ASCII);
        for (int i = 0; i <= haystack.length - from.length; i++) {
            boolean match = true;
            for (int j = 0; j < from.length && match; j++) {
                match = haystack[i + j] == from[j];
            }
            if (match) {
                System.arraycopy(to, 0, haystack, i, to.length);
                return;
            }
        }
        throw new AssertionError("not found in the stream: " + needle);
    }
}
