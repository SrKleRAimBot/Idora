package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.internal.UuidV7;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.time.ZonedDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.TimeZone;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Pins the clock identifiers are stamped from to UTC.
 *
 * <p>Both ULID and UUIDv7 define their timestamp as milliseconds since the Unix epoch, an absolute
 * instant with no time zone, so servers in Sydney and Los Angeles that agree on the instant produce
 * identifiers that interleave in true creation order. Deriving the timestamp from local date-time
 * fields instead would shift every server's identifiers by its own UTC offset, silently.
 *
 * <p>That mistake is invisible on a host running in UTC. The build therefore runs the suite with
 * {@code -Duser.timezone=Australia/Sydney}, so that these assertions bite.
 */
class TimestampSourceTest {

    /** Generous enough for a slow host, far tighter than any real UTC offset. */
    private static final long TOLERANCE_MS = 30_000;

    @Test
    void theSuite_runsInANonUtcZone_soTheUtcAssertionsMeanSomething() {
        // Act
        int offset = TimeZone.getDefault().getOffset(System.currentTimeMillis());

        // Assert
        assertTrue(offset != 0, "run the tests with a non-UTC -Duser.timezone; the build sets Australia/Sydney");
    }

    @Test
    void generate_withUlid_encodesUtcRegardlessOfTheHostsTimeZone() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "usr").build();
        long utcNow = Instant.now().toEpochMilli();

        // Act
        String id = generator.generate(User.class);

        // Assert
        long encoded = UlidDecoder.decodeTimestamp(UlidDecoder.valueOf(id));
        assertNear(utcNow, encoded);
        assertNotLocalTime(utcNow, encoded);
    }

    @Test
    void generate_withUuidV7_encodesUtcRegardlessOfTheHostsTimeZone() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().useUuidV7().registerEntity(User.class, "usr").build();
        long utcNow = Instant.now().toEpochMilli();

        // Act
        String id = generator.generate(User.class);

        // Assert: the first 12 hex characters are the 48-bit millisecond timestamp.
        long encoded = UlidDecoder.decodeUuidTimestamp(UlidDecoder.valueOf(id));
        assertNear(utcNow, encoded);
        assertNotLocalTime(utcNow, encoded);
    }

    @Test
    void writeHex_treatsItsTimestampAsAnAbsoluteInstant() {
        // Arrange: one instant written down in three zones.
        ZonedDateTime utc = ZonedDateTime.of(2026, 8, 7, 12, 0, 0, 0, ZoneOffset.UTC);
        ZonedDateTime sydney = utc.withZoneSameInstant(ZoneId.of("Australia/Sydney"));
        ZonedDateTime losAngeles = utc.withZoneSameInstant(ZoneId.of("America/Los_Angeles"));

        // Act
        long fromUtc = uuidTimestampOf(utc);
        long fromSydney = uuidTimestampOf(sydney);
        long fromLosAngeles = uuidTimestampOf(losAngeles);

        // Assert: one instant, one timestamp, whatever offset it was written down with.
        assertEquals(fromUtc, fromSydney);
        assertEquals(fromUtc, fromLosAngeles);
    }

    @Test
    void identifiersMadeAMillisecondApartInDifferentZones_sortByInstant() {
        // Arrange: three servers, three zones, creating entities a millisecond apart.
        ZonedDateTime first = ZonedDateTime.of(2026, 8, 7, 12, 0, 0, 0, ZoneOffset.UTC);
        ZonedDateTime second = first.plusNanos(1_000_000).withZoneSameInstant(ZoneId.of("Australia/Sydney"));
        ZonedDateTime third = first.plusNanos(2_000_000).withZoneSameInstant(ZoneId.of("America/Los_Angeles"));

        // Act
        List<String> ids = new ArrayList<>(List.of(uuidOf(first), uuidOf(second), uuidOf(third)));

        // Assert: sorting the text restores true creation order, not zone order.
        List<String> sorted = new ArrayList<>(ids);
        sorted.sort(null);
        assertEquals(ids, sorted);
    }

    private static void assertNear(long expected, long actual) {
        assertTrue(Math.abs(actual - expected) <= TOLERANCE_MS, actual + " is not within " + TOLERANCE_MS + "ms of " + expected);
    }

    /** Asserts the value is not what an implementation reading local time would have produced. */
    private static void assertNotLocalTime(long utcNow, long actual) {
        long offset = TimeZone.getDefault().getOffset(utcNow);
        if (offset == 0) {
            return;
        }
        long asLocalTime = utcNow + offset;
        assertTrue(Math.abs(actual - asLocalTime) > TOLERANCE_MS,
                "the timestamp looks like local time in " + TimeZone.getDefault().getID() + " rather than UTC");
    }

    private static String uuidOf(ZonedDateTime time) {
        byte[] hex = new byte[UuidV7.HEX_LENGTH];
        UuidV7.writeHex(time.toInstant().toEpochMilli(), new byte[UuidV7.RANDOM_BYTES], 0, hex, 0);
        return new String(hex, StandardCharsets.US_ASCII);
    }

    private static long uuidTimestampOf(ZonedDateTime time) {
        return UlidDecoder.decodeUuidTimestamp(uuidOf(time));
    }
}
