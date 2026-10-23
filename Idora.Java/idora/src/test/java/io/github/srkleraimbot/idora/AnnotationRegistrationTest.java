package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.InvalidEntityPrefixException.Reason;
import io.github.srkleraimbot.idora.fixtures.FixtureAuditable;
import io.github.srkleraimbot.idora.fixtures.FixtureBase;
import io.github.srkleraimbot.idora.fixtures.FixtureDerived;
import io.github.srkleraimbot.idora.fixtures.FixtureMentionsAnnotation;
import io.github.srkleraimbot.idora.fixtures.FixtureOrder;
import io.github.srkleraimbot.idora.fixtures.FixtureOuter;
import io.github.srkleraimbot.idora.fixtures.FixturePayment;
import io.github.srkleraimbot.idora.fixtures.FixtureStatus;
import io.github.srkleraimbot.idora.fixtures.FixtureUnannotated;
import io.github.srkleraimbot.idora.fixtures.FixtureUser;
import io.github.srkleraimbot.idora.fixtures.billing.FixtureInvoice;
import io.github.srkleraimbot.idora.fixturesx.FixtureImpostor;
import io.github.srkleraimbot.idora.support.InitialisationLog;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import io.github.srkleraimbot.idora.unannotated.Plain;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.AnnotatedAccount;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.AnnotatedAccrual;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.AnnotatedCustomer;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.AnnotatedShipment;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.DerivedCustomer;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.Nothing;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.WithBadCharacter;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.WithEmptyPrefix;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.WithOverlongPrefix;
import io.github.srkleraimbot.idora.unscanned.UnscannedEntities.WithSeparator;
import io.github.srkleraimbot.idora.unscanned.clash.ClashFirst;
import io.github.srkleraimbot.idora.unscanned.clash.ClashSecond;
import io.github.srkleraimbot.idora.unscanned.invalid.InvalidAnnotated;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Inherited;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;
import java.util.stream.Collectors;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Registering prefixes with {@link EntityPrefix}: what a scan finds and leaves alone, that the two
 * registration styles coexist without either claiming a prefix twice, and that a failure describes
 * itself the same way every run.
 *
 * <p>Whole-package behaviour is exercised against {@code fixtures}, which holds only well-formed
 * annotated types. Clashes and malformed prefixes use the types in {@code unscanned}, listed
 * explicitly, or its single-purpose {@code invalid} and {@code clash} sub-packages.
 */
class AnnotationRegistrationTest {

    /** Every annotated type under {@code fixtures}, by binary name, with its prefix. */
    private static final Map<String, String> FIXTURE_PREFIXES = new TreeMap<>(Map.ofEntries(
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureAuditable", "fxaud"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureBase", "fxbase"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureInternalEntity", "fxint"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureOrder", "fxord"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureOuter$Line", "fxline"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureOuter$Secret", "fxsec"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixturePayment", "fxpay"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureStatus", "fxsts"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureUser", "fxusr"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.FixtureWithStaticInitialiser", "fxinit"),
            Map.entry("io.github.srkleraimbot.idora.fixtures.billing.FixtureInvoice", "fxinv")));

    @Test
    void registerEntitiesFromPackageOf_registersEveryAnnotatedTypeAndNothingElse() {
        // Act
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert: classes, a record, an interface, an enum, nested, private and package-private
        // types, and a sub-package; nothing unannotated, derived, or merely mentioning the annotation.
        assertEquals(FIXTURE_PREFIXES, byName(registrations));
        assertFalse(registrations.containsKey(FixtureUnannotated.class));
        assertFalse(registrations.containsKey(FixtureMentionsAnnotation.class));
    }

    @Test
    void registerEntitiesFromPackageOf_findsRecordsInterfacesEnumsAndNestedTypes() {
        // Act
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert
        assertEquals("fxpay", registrations.get(FixturePayment.class));
        assertEquals("fxaud", registrations.get(FixtureAuditable.class));
        assertEquals("fxsts", registrations.get(FixtureStatus.class));
        assertEquals("fxline", registrations.get(FixtureOuter.Line.class));
    }

    @Test
    void registerEntitiesFromPackageOf_descendsIntoSubPackages() {
        // Act
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert
        assertEquals("fxinv", registrations.get(FixtureInvoice.class));
    }

    @Test
    void registerEntitiesFromPackageOf_doesNotTreatAPackageSharingTheNamePrefixAsASubPackage() {
        // Act: 'fixturesx' starts with 'fixtures'; had it been scanned, its 'fxusr' would collide.
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert
        assertFalse(registrations.containsKey(FixtureImpostor.class));
    }

    @Test
    void registerEntitiesFromPackageOf_doesNotGiveADerivedTypeItsBasesPrefix() {
        // Act
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert: an inherited annotation would make every subclass collide with its base.
        assertEquals("fxbase", registrations.get(FixtureBase.class));
        assertFalse(registrations.containsKey(FixtureDerived.class));
    }

    @Test
    void registerEntitiesFromPackageOf_registersInOrdinalOrderOfBinaryName() {
        // Act
        Map<Class<?>, String> registrations = scanFixtures().registrations();

        // Assert
        List<String> names = registrations.keySet().stream().map(Class::getName).collect(Collectors.toList());
        assertEquals(new ArrayList<>(FIXTURE_PREFIXES.keySet()), names);
    }

    @Test
    void registerEntitiesFromPackageOf_withAMarkerInASubPackage_scansOnlyThatSubPackage() {
        // Act
        Map<Class<?>, String> registrations = IdoraGenerator.builder()
                .registerEntitiesFromPackageOf(FixtureInvoice.class).build().registry().registrations();

        // Assert
        assertEquals(Map.of(FixtureInvoice.class, "fxinv"), registrations);
    }

    @Test
    void registerEntitiesFromPackageOf_withANestedMarker_scansTheEnclosingPackage() {
        // Act
        Map<Class<?>, String> registrations = IdoraGenerator.builder()
                .registerEntitiesFromPackageOf(FixtureOuter.Line.class).build().registry().registrations();

        // Assert
        assertEquals(FIXTURE_PREFIXES, byName(registrations));
    }

    @Test
    void registerEntitiesFromPackageOf_withAPackageThatHasNoAnnotatedTypes_registersNothing() {
        // Act
        Map<Class<?>, String> registrations = IdoraGenerator.builder()
                .registerEntitiesFromPackageOf(Plain.class).build().registry().registrations();

        // Assert
        assertTrue(registrations.isEmpty());
    }

    @Test
    void registerEntitiesFromPackageOf_loadsClassesWithoutInitialisingThem() {
        // Act
        Map<String, String> registrations = byName(scanFixtures().registrations());

        // Assert: found and registered, yet its static initialiser never ran.
        String name = "io.github.srkleraimbot.idora.fixtures.FixtureWithStaticInitialiser";
        assertEquals("fxinit", registrations.get(name));
        assertFalse(InitialisationLog.wasInitialised(name), "the scan must not run static initialisers");
    }

    @Test
    void generate_forATypeRegisteredByAnnotation_usesTheDeclaredPrefix() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class).build();

        // Act
        String id = generator.generate(FixtureUser.class);

        // Assert: no reflection was needed to get here.
        assertTrue(id.startsWith("fxusr_"), id);
        assertEquals("fxusr_".length() + 26, id.length());
    }

    @Test
    void scanThenRegisterEntity_keepsBoth() {
        // Act
        IdoraGenerator generator = IdoraGenerator.builder()
                .registerEntitiesFromPackageOf(FixtureUser.class)
                .registerEntity(User.class, "usr")
                .build();

        // Assert
        assertEquals("fxusr", generator.registry().getPrefix(FixtureUser.class));
        assertEquals("usr", generator.registry().getPrefix(User.class));
    }

    @Test
    void registerEntityThenScan_keepsBoth() {
        // Act: the styles mix in either order.
        IdoraGenerator generator = IdoraGenerator.builder()
                .registerEntity(User.class, "usr")
                .registerEntitiesFromPackageOf(FixtureUser.class)
                .build();

        // Assert
        assertEquals("usr", generator.registry().getPrefix(User.class));
        assertEquals("fxusr", generator.registry().getPrefix(FixtureUser.class));
    }

    @Test
    void scan_withAPrefixAlreadyTakenExplicitly_isADuplicate() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "FXUSR");

        // Act
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> builder.registerEntitiesFromPackageOf(FixtureUser.class));

        // Assert
        assertEquals("fxusr", exception.getPrefix());
        assertSame(FixtureUser.class, exception.getEntityType());
        assertSame(User.class, exception.getConflictingEntityType());
    }

    @Test
    void registerEntity_withAPrefixAlreadyTakenByAnAnnotation_isADuplicate() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class);

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(User.class, "fxusr"));

        // Assert
        assertSame(FixtureUser.class, exception.getConflictingEntityType());
    }

    @Test
    void registerEntity_forATypeAlreadyRegisteredByItsAnnotation_throwsEvenWithTheSamePrefix() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class);

        // Act: agreeing on the prefix does not make two sources of truth acceptable.
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(FixtureUser.class, "fxusr"));

        // Assert
        assertSame(FixtureUser.class, exception.getEntityType());
        assertSame(FixtureUser.class, exception.getConflictingEntityType());
    }

    @Test
    void scan_forATypeAlreadyRegisteredExplicitly_throwsEvenWithTheSamePrefix() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(FixtureUser.class, "fxusr");

        // Act: and the same holds the other way round.
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> builder.registerEntitiesFromPackageOf(FixtureUser.class));

        // Assert
        assertSame(FixtureUser.class, exception.getEntityType());
    }

    @Test
    void scan_ofTheSamePackageTwice_isADuplicateAndSaysSo() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class);

        // Act
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> builder.registerEntitiesFromPackageOf(FixtureOrder.class));

        // Assert: neither "remove the registerEntity call" nor "remove the annotation" is the fix here.
        assertTrue(exception.getMessage().contains("scanned twice"), exception.getMessage());
        assertFalse(exception.getMessage().contains("cannot also be registered with"), exception.getMessage());
    }

    @Test
    void registerEntity_forAnAnnotatedTypeWithADifferentPrefix_saysWhichPrefixesDisagree() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class);

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(FixtureUser.class, "other"));

        // Assert: declaring two different prefixes is a different mistake from declaring one twice.
        assertTrue(exception.getMessage().contains("fxusr"), exception.getMessage());
        assertTrue(exception.getMessage().contains("other"), exception.getMessage());
        assertTrue(exception.getMessage().contains("only one prefix"), exception.getMessage());
    }

    @Test
    void scan_ofAPackageWithTwoTypesClaimingOnePrefix_reportsTheLaterNameAsTheOffender() {
        // Act
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntitiesFromPackageOf(ClashSecond.class));

        // Assert: "ClashFirst" sorts before "ClashSecond", whatever order the file system lists them.
        assertSame(ClashSecond.class, exception.getEntityType());
        assertSame(ClashFirst.class, exception.getConflictingEntityType());
        assertEquals("DUP", exception.getPrefix());
    }

    @Test
    void scan_ofAPackageWithAnInvalidPrefix_namesTheAnnotatedType() {
        // Act
        InvalidEntityPrefixException exception = assertThrows(InvalidEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntitiesFromPackageOf(InvalidAnnotated.class));

        // Assert: an annotation leaves no stack trace pointing at the line that declared it.
        assertTrue(exception.getMessage().contains(InvalidAnnotated.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains("@EntityPrefix"), exception.getMessage());
        assertEquals(Optional.of(InvalidAnnotated.class), exception.getEntityType());
        assertEquals(Reason.CHARACTER, exception.getReason());
    }

    @Test
    void registerEntities_withTwoTypesClaimingOnePrefix_isADuplicate() {
        // Act: "acc" and "ACC" differ only by case, which is a collision to a human reader.
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(AnnotatedAccount.class, AnnotatedAccrual.class));

        // Assert
        assertSame(AnnotatedAccrual.class, exception.getEntityType());
        assertSame(AnnotatedAccount.class, exception.getConflictingEntityType());
    }

    @Test
    void registerEntities_withAClash_reportsItTheSameWayWhateverOrderTheTypesArriveIn() {
        // Act
        DuplicateEntityPrefixException forwards = assertThrows(DuplicateEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(AnnotatedAccount.class, AnnotatedAccrual.class));
        DuplicateEntityPrefixException backwards = assertThrows(DuplicateEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(List.of(AnnotatedAccrual.class, AnnotatedAccount.class)));

        // Assert: a start-up failure that describes itself differently each run is a bad one.
        assertSame(forwards.getEntityType(), backwards.getEntityType());
        assertSame(forwards.getConflictingEntityType(), backwards.getConflictingEntityType());
        assertEquals(forwards.getMessage(), backwards.getMessage());
    }

    @Test
    void registerEntities_registersEachListedType() {
        // Act
        Map<Class<?>, String> registrations = IdoraGenerator.builder()
                .registerEntities(AnnotatedShipment.class, AnnotatedCustomer.class)
                .build().registry().registrations();

        // Assert: and in name order, not argument order.
        assertEquals(Map.of(AnnotatedCustomer.class, "cus", AnnotatedShipment.class, "shp"), registrations);
        assertEquals(List.of(AnnotatedCustomer.class, AnnotatedShipment.class), List.copyOf(registrations.keySet()));
    }

    @Test
    void registerEntities_withAnUnannotatedType_throwsIllegalArgumentExceptionNamingIt() {
        // Act
        IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
                () -> IdoraGenerator.builder().registerEntities(Nothing.class));

        // Assert: a type you named explicitly but forgot to annotate is a mistake, not a no-op.
        assertTrue(exception.getMessage().contains(Nothing.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains("registerEntity"), exception.getMessage());
    }

    @Test
    void registerEntities_withASubclassOfAnAnnotatedType_doesNotInheritTheAnnotation() {
        // Act & Assert
        assertThrows(IllegalArgumentException.class,
                () -> IdoraGenerator.builder().registerEntities(DerivedCustomer.class));
    }

    @Test
    void registerEntities_withASeparatorInThePrefix_namesTheOffendingType() {
        // Act
        InvalidEntityPrefixException exception = assertThrows(InvalidEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(WithSeparator.class));

        // Assert
        assertTrue(exception.getMessage().contains(WithSeparator.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains("@EntityPrefix"), exception.getMessage());
        assertTrue(exception.getMessage().contains("separator"), exception.getMessage());
        assertEquals("bad_prefix", exception.getPrefix());
        assertEquals(Reason.SEPARATOR, exception.getReason());
    }

    @ParameterizedTest
    @ValueSource(classes = {WithEmptyPrefix.class, WithOverlongPrefix.class, WithBadCharacter.class})
    void registerEntities_withAnInvalidPrefix_throwsAndNamesTheOffendingType(Class<?> entityType) {
        // Act
        InvalidEntityPrefixException exception = assertThrows(InvalidEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(entityType));

        // Assert
        assertTrue(exception.getMessage().contains(entityType.getName()), exception.getMessage());
        assertEquals(Optional.of(entityType), exception.getEntityType());
    }

    @Test
    void registerEntities_whenOneTypeIsRejected_leavesEarlierRegistrationsIntact() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        assertThrows(InvalidEntityPrefixException.class,
                () -> builder.registerEntities(WithSeparator.class, AnnotatedCustomer.class));

        // Assert: "AnnotatedCustomer" sorts first, so it was already in when the list failed.
        assertEquals(Map.of(AnnotatedCustomer.class, "cus"), builder.build().registry().registrations());
    }

    @Test
    void registerEntities_withTheSameTypeTwice_isADuplicate() {
        // Act
        DuplicateEntityPrefixException exception = assertThrows(DuplicateEntityPrefixException.class,
                () -> IdoraGenerator.builder().registerEntities(AnnotatedCustomer.class, AnnotatedCustomer.class));

        // Assert
        assertTrue(exception.getMessage().contains("registered a second time"), exception.getMessage());
    }

    @Test
    void registerEntities_withNulls_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        NullPointerException array = assertThrows(NullPointerException.class, () -> builder.registerEntities((Class<?>[]) null));
        NullPointerException element = assertThrows(NullPointerException.class,
                () -> builder.registerEntities(AnnotatedCustomer.class, null));
        NullPointerException collection = assertThrows(NullPointerException.class,
                () -> builder.registerEntities((List<Class<?>>) null));

        // Assert
        assertEquals("entityTypes", array.getMessage());
        assertEquals("entityTypes", element.getMessage());
        assertEquals("entityTypes", collection.getMessage());
    }

    @Test
    void registerEntitiesFromPackageOf_withANullMarker_throwsNullPointerExceptionNamingTheParameter() {
        // Act
        NullPointerException exception = assertThrows(NullPointerException.class,
                () -> IdoraGenerator.builder().registerEntitiesFromPackageOf(null));

        // Assert
        assertEquals("marker", exception.getMessage());
    }

    @Test
    void registerEntitiesFromPackageOf_withAPrimitiveOrArrayMarker_throwsIllegalArgumentException() {
        // Act & Assert
        assertThrows(IllegalArgumentException.class, () -> IdoraGenerator.builder().registerEntitiesFromPackageOf(int.class));
        assertThrows(IllegalArgumentException.class,
                () -> IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser[].class));
    }

    @Test
    void entityPrefix_keepsThePrefixItWasGiven() {
        // Act
        EntityPrefix annotation = FixtureUser.class.getDeclaredAnnotation(EntityPrefix.class);

        // Assert
        assertNotNull(annotation);
        assertEquals("fxusr", annotation.value());
    }

    @Test
    void entityPrefix_isRuntimeRetainedDocumentedTypeOnlyAndNotInherited() {
        // Act
        Retention retention = EntityPrefix.class.getAnnotation(Retention.class);
        Target target = EntityPrefix.class.getAnnotation(Target.class);

        // Assert
        assertEquals(RetentionPolicy.RUNTIME, retention.value());
        assertArrayEquals(new ElementType[] {ElementType.TYPE}, target.value());
        assertNotNull(EntityPrefix.class.getAnnotation(Documented.class));
        assertNull(EntityPrefix.class.getAnnotation(Inherited.class));
        assertNull(FixtureDerived.class.getAnnotation(EntityPrefix.class));
    }

    private static EntityPrefixRegistry scanFixtures() {
        return IdoraGenerator.builder().registerEntitiesFromPackageOf(FixtureUser.class).build().registry();
    }

    private static Map<String, String> byName(Map<Class<?>, String> registrations) {
        Map<String, String> byName = new TreeMap<>();
        registrations.forEach((type, prefix) -> byName.put(type.getName(), prefix));
        return byName;
    }
}
