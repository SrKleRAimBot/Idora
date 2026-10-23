package io.github.srkleraimbot.idora.internal;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.fixtures.FixtureUser;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.lang.module.ModuleReference;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.SortedSet;
import java.util.TreeMap;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.jar.JarOutputStream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotSame;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Each kind of location the scanner reads: a named module (how these tests run, patched into the
 * library's module), an exploded directory and a jar (both rebuilt here from the fixture class
 * files and loaded through a fresh class loader, the way a class-path application sees them).
 */
class PackageScannerTest {

    private static final String FIXTURES = "io.github.srkleraimbot.idora.fixtures";
    private static final byte[] NEEDLE = ("L" + EntityPrefix.class.getName().replace('.', '/') + ";").getBytes(StandardCharsets.US_ASCII);

    /** The fixture classes that carry, or mention, the annotation: what a scan should report. */
    private static final List<String> MENTIONING = List.of(
            FIXTURES + ".FixtureAuditable",
            FIXTURES + ".FixtureBase",
            FIXTURES + ".FixtureInternalEntity",
            FIXTURES + ".FixtureMentionsAnnotation",
            FIXTURES + ".FixtureOrder",
            FIXTURES + ".FixtureOuter$Line",
            FIXTURES + ".FixtureOuter$Secret",
            FIXTURES + ".FixturePayment",
            FIXTURES + ".FixtureStatus",
            FIXTURES + ".FixtureUser",
            FIXTURES + ".FixtureWithStaticInitialiser",
            FIXTURES + ".billing.FixtureInvoice");

    /** Every fixture class file, annotated or not, so the copies are a faithful package. */
    private static final List<String> ALL_FIXTURE_CLASSES = List.of(
            "FixtureAuditable", "FixtureBase", "FixtureDerived", "FixtureInternalEntity", "FixtureMentionsAnnotation",
            "FixtureOrder", "FixtureOuter", "FixtureOuter$Line", "FixtureOuter$Secret", "FixturePayment", "FixtureStatus",
            "FixtureUnannotated", "FixtureUser", "FixtureWithStaticInitialiser", "billing/FixtureInvoice");

    @Test
    void classesMentioning_inANamedModule_readsTheModule() {
        // Arrange: the suite runs patched into the library's module.
        assertTrue(FixtureUser.class.getModule().isNamed(), "expected the tests to run on the module path");

        // Act
        List<String> names = PackageScanner.classesMentioning(FixtureUser.class, new String(NEEDLE, StandardCharsets.US_ASCII));

        // Assert: sorted, filtered by content, sub-packages included, the sibling 'fixturesx' not.
        assertEquals(MENTIONING, names);
    }

    @Test
    void scanModule_findsTheSameClassesAsTheConvenienceMethod() throws IOException {
        // Arrange
        Module module = FixtureUser.class.getModule();
        ModuleReference reference = module.getLayer().configuration().findModule(module.getName()).orElseThrow().reference();

        // Act
        SortedSet<String> names = PackageScanner.scanModule(reference, FIXTURES, NEEDLE);

        // Assert
        assertEquals(MENTIONING, List.copyOf(names));
    }

    @Test
    void scanDirectory_findsClassesInAnExplodedDirectory(@TempDir Path root) throws IOException {
        // Arrange
        copyFixturesInto(root);

        // Act
        SortedSet<String> names = PackageScanner.scanDirectory(root, FIXTURES, NEEDLE);

        // Assert
        assertEquals(MENTIONING, List.copyOf(names));
    }

    @Test
    void scanDirectory_forAPackageThatIsNotThere_findsNothing(@TempDir Path root) throws IOException {
        // Act & Assert
        assertTrue(PackageScanner.scanDirectory(root, "no.such.pkg", NEEDLE).isEmpty());
    }

    @Test
    void scanJar_findsClassesInAJar(@TempDir Path directory) throws IOException {
        // Arrange
        Path jar = buildJar(directory.resolve("fixtures.jar"), "");

        // Act
        SortedSet<String> names;
        try (JarFile file = new JarFile(jar.toFile())) {
            names = PackageScanner.scanJar(file, "", FIXTURES, NEEDLE);
        }

        // Assert
        assertEquals(MENTIONING, List.copyOf(names));
    }

    @Test
    void scanJar_withAnEntryPrefix_findsClassesInARepackagedJar(@TempDir Path directory) throws IOException {
        // Arrange: the layout of a Spring Boot style fat jar.
        Path jar = buildJar(directory.resolve("app.jar"), "BOOT-INF/classes/");

        // Act
        SortedSet<String> names;
        try (JarFile file = new JarFile(jar.toFile())) {
            names = PackageScanner.scanJar(file, "BOOT-INF/classes/", FIXTURES, NEEDLE);
        }

        // Assert
        assertEquals(MENTIONING, List.copyOf(names));
    }

    @Test
    void registerEntitiesFromPackageOf_onTheClassPathFromAJar_registersTheJarsTypes(@TempDir Path directory)
            throws Exception {
        // Arrange: load the fixtures from a jar through their own loader, as a class-path
        // application would, so the marker is in an unnamed module and the scan takes the jar path.
        Path jar = buildJar(directory.resolve("fixtures.jar"), "");

        try (ChildFirstLoader loader = new ChildFirstLoader(jar.toUri().toURL())) {
            Class<?> marker = loader.loadClass(FIXTURES + ".FixtureUser");
            assertFalse(marker.getModule().isNamed());
            assertNotSame(FixtureUser.class, marker);

            // Act
            Map<Class<?>, String> registrations = IdoraGenerator.builder()
                    .registerEntitiesFromPackageOf(marker).build().registry().registrations();

            // Assert
            assertEquals(11, registrations.size());
            assertEquals("fxusr", registrations.get(marker));
            registrations.keySet().forEach(type -> assertSame(loader, type.getClassLoader()));
        }
    }

    @Test
    void registerEntitiesFromPackageOf_onTheClassPathFromADirectory_registersTheDirectorysTypes(@TempDir Path root)
            throws Exception {
        // Arrange
        copyFixturesInto(root);

        try (ChildFirstLoader loader = new ChildFirstLoader(root.toUri().toURL())) {
            Class<?> marker = loader.loadClass(FIXTURES + ".FixtureUser");

            // Act
            Map<Class<?>, String> registrations = IdoraGenerator.builder()
                    .registerEntitiesFromPackageOf(marker).build().registry().registrations();

            // Assert
            Map<String, String> byName = new TreeMap<>();
            registrations.forEach((type, prefix) -> byName.put(type.getName(), prefix));
            assertEquals(11, byName.size());
            assertEquals("fxinv", byName.get(FIXTURES + ".billing.FixtureInvoice"));
            assertFalse(byName.containsKey(FIXTURES + ".FixtureMentionsAnnotation"));
        }
    }

    @Test
    void classesMentioning_withAMarkerInTheUnnamedPackage_refusesToScanTheRoot(@TempDir Path root) throws Exception {
        // Arrange: Java source cannot reference a class in the unnamed package, so assemble one.
        Files.write(root.resolve("Unpackaged.class"), minimalClassFile("Unpackaged"));

        try (URLClassLoader loader = new URLClassLoader(new URL[] {root.toUri().toURL()}, null)) {
            Class<?> marker = loader.loadClass("Unpackaged");

            // Act
            IllegalArgumentException exception = assertThrows(IllegalArgumentException.class,
                    () -> PackageScanner.classesMentioning(marker, "x"));

            // Assert: scanning the root would mean scanning everything at that location.
            assertTrue(exception.getMessage().contains("unnamed package"), exception.getMessage());
        }
    }

    @Test
    void contains_findsANeedleAnywhereAndOnlyWhenPresent() {
        // Arrange
        byte[] haystack = "xxLabc;yyLab".getBytes(StandardCharsets.US_ASCII);

        // Act & Assert
        assertTrue(PackageScanner.contains(haystack, "Labc;".getBytes(StandardCharsets.US_ASCII)));
        assertTrue(PackageScanner.contains(haystack, "yyLab".getBytes(StandardCharsets.US_ASCII)));
        assertFalse(PackageScanner.contains(haystack, "Labc;x".getBytes(StandardCharsets.US_ASCII)));
        assertFalse(PackageScanner.contains(haystack, "yyLabc".getBytes(StandardCharsets.US_ASCII)));
        assertTrue(PackageScanner.contains(haystack, new byte[0]));
    }

    /** The smallest valid class file: {@code public class <name> extends Object}, with no members. */
    private static byte[] minimalClassFile(String name) throws IOException {
        java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream();
        try (java.io.DataOutputStream out = new java.io.DataOutputStream(bytes)) {
            out.writeInt(0xCAFEBABE);
            out.writeShort(0);          // minor version
            out.writeShort(52);         // major version: Java 8
            out.writeShort(5);          // constant pool count (entries 1 to 4)
            out.writeByte(1);           // #1 Utf8 this class name
            out.writeUTF(name);
            out.writeByte(7);           // #2 Class #1
            out.writeShort(1);
            out.writeByte(1);           // #3 Utf8 superclass name
            out.writeUTF("java/lang/Object");
            out.writeByte(7);           // #4 Class #3
            out.writeShort(3);
            out.writeShort(0x0021);     // ACC_PUBLIC | ACC_SUPER
            out.writeShort(2);          // this class
            out.writeShort(4);          // superclass
            out.writeShort(0);          // interfaces
            out.writeShort(0);          // fields
            out.writeShort(0);          // methods
            out.writeShort(0);          // attributes
        }
        return bytes.toByteArray();
    }

    private static void copyFixturesInto(Path root) throws IOException {
        for (String name : ALL_FIXTURE_CLASSES) {
            Path target = root.resolve(FIXTURES.replace('.', '/')).resolve(name + ".class");
            Files.createDirectories(target.getParent());
            Files.write(target, classBytes(name));
        }
    }

    private static Path buildJar(Path jar, String entryPrefix) throws IOException {
        try (OutputStream file = Files.newOutputStream(jar); JarOutputStream out = new JarOutputStream(file)) {
            // No directory entries, as many build tools produce: the scan must not rely on them.
            for (String name : ALL_FIXTURE_CLASSES) {
                out.putNextEntry(new JarEntry(entryPrefix + FIXTURES.replace('.', '/') + "/" + name + ".class"));
                out.write(classBytes(name));
                out.closeEntry();
            }
        }
        return jar;
    }

    private static byte[] classBytes(String name) throws IOException {
        String resource = "/" + FIXTURES.replace('.', '/') + "/" + name + ".class";
        try (InputStream in = FixtureUser.class.getResourceAsStream(resource)) {
            if (in == null) {
                throw new IOException("fixture class file not found: " + resource);
            }
            return in.readAllBytes();
        }
    }

    /**
     * Loads the fixture package from its own URLs and delegates everything else, so the copies get
     * their own Class objects while still sharing the real {@link EntityPrefix} type.
     */
    private static final class ChildFirstLoader extends URLClassLoader {

        ChildFirstLoader(URL url) {
            super(new URL[] {url}, PackageScannerTest.class.getClassLoader());
        }

        @Override
        protected Class<?> loadClass(String name, boolean resolve) throws ClassNotFoundException {
            if (!name.startsWith(FIXTURES + ".")) {
                return super.loadClass(name, resolve);
            }
            synchronized (getClassLoadingLock(name)) {
                Class<?> loaded = findLoadedClass(name);
                if (loaded == null) {
                    loaded = findClass(name);
                }
                if (resolve) {
                    resolveClass(loaded);
                }
                return loaded;
            }
        }
    }
}
