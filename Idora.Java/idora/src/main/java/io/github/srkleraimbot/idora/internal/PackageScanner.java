package io.github.srkleraimbot.idora.internal;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.lang.module.ModuleReader;
import java.lang.module.ModuleReference;
import java.lang.module.ResolvedModule;
import java.net.JarURLConnection;
import java.net.URISyntaxException;
import java.net.URL;
import java.net.URLConnection;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.security.CodeSource;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.Optional;
import java.util.SortedSet;
import java.util.TreeSet;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.stream.Stream;

/**
 * Lists the classes in one package and its sub-packages, within the directory, jar or named module
 * a marker class was loaded from, without walking anything else and without loading anything.
 *
 * <p>The listing is pre-filtered: a class is only reported if its class file contains a given
 * byte sequence, which callers set to the descriptor of an annotation type. Any use of a
 * runtime-visible annotation puts that descriptor in the class file's constant pool, so the filter
 * never misses an annotated class. It can report a class that merely mentions the annotation type,
 * which is why the caller still checks the annotation reflectively. The point is that the hundreds
 * of other classes in a large package are read as bytes but never loaded, so they cost no
 * metaspace and cannot fail to link.
 *
 * <p>Results are sorted by binary class name in ordinal order (specification section 4.2).
 */
public final class PackageScanner {

    private static final String CLASS_SUFFIX = ".class";

    private PackageScanner() {
    }

    /**
     * Lists the classes in the package of {@code marker}, and its sub-packages, whose class files
     * contain {@code needle}.
     *
     * @param marker a class in the package to scan
     * @param needle the text to look for, ASCII; normally an annotation's type descriptor
     * @return binary class names, in ordinal order
     * @throws IllegalArgumentException if the marker is a primitive or array type, is in the unnamed
     *     package, or was loaded from somewhere that cannot be listed
     * @throws UncheckedIOException if reading the location fails
     */
    public static List<String> classesMentioning(Class<?> marker, String needle) {
        if (marker.isPrimitive() || marker.isArray()) {
            throw new IllegalArgumentException(
                    "'" + marker.getTypeName() + "' cannot be used as a package marker. Pass a class, record, "
                            + "interface or enum from the package to scan, for example one of your entities.");
        }
        String packageName = marker.getPackageName();
        if (packageName.isEmpty()) {
            throw new IllegalArgumentException(
                    "'" + marker.getName() + "' is in the unnamed package. Scanning it would mean scanning every "
                            + "class at the root of its location, so it is not supported. Move the entities into a "
                            + "named package, or register them with registerEntity or registerEntities.");
        }

        byte[] pattern = needle.getBytes(StandardCharsets.UTF_8);
        SortedSet<String> names;
        try {
            Module module = marker.getModule();
            names = module.isNamed()
                    ? scanModule(moduleReference(marker, module), packageName, pattern)
                    : scanLocation(marker, packageName, pattern);
        } catch (IOException e) {
            throw new UncheckedIOException(
                    "Could not read the classes of package '" + packageName + "' to find @EntityPrefix types.", e);
        }
        return new ArrayList<>(names);
    }

    /**
     * Loads a class found by a scan, without initialising it.
     *
     * @param className the binary name
     * @param loader the loader of the marker the scan started from
     * @return the class
     */
    public static Class<?> load(String className, ClassLoader loader) {
        try {
            return Class.forName(className, false, loader);
        } catch (ClassNotFoundException e) {
            throw new IllegalStateException(
                    "Found the class file for '" + className + "' while scanning for @EntityPrefix types, but "
                            + "its class loader could not load it.", e);
        }
    }

    /**
     * Scans a package within a named module, through the module's own reader.
     *
     * @param reference the module
     * @param packageName the package to scan, together with its sub-packages
     * @param needle the bytes a class file must contain to be reported
     * @return binary class names, in ordinal order
     * @throws IOException if the module cannot be read
     */
    public static SortedSet<String> scanModule(ModuleReference reference, String packageName, byte[] needle)
            throws IOException {
        String directory = packageName.replace('.', '/') + '/';
        SortedSet<String> names = new TreeSet<>();
        try (ModuleReader reader = reference.open(); Stream<String> resources = reader.list()) {
            for (String resource : (Iterable<String>) resources::iterator) {
                if (!isClassIn(resource, directory)) {
                    continue;
                }
                Optional<InputStream> content = reader.open(resource);
                if (content.isEmpty()) {
                    continue;
                }
                try (InputStream in = content.get()) {
                    if (contains(in.readAllBytes(), needle)) {
                        names.add(binaryName(resource));
                    }
                }
            }
        }
        return names;
    }

    /**
     * Scans a package within an exploded class directory.
     *
     * @param root the directory at the root of the class hierarchy (the one containing
     *     {@code com/}, say)
     * @param packageName the package to scan, together with its sub-packages
     * @param needle the bytes a class file must contain to be reported
     * @return binary class names, in ordinal order
     * @throws IOException if the directory cannot be read
     */
    public static SortedSet<String> scanDirectory(Path root, String packageName, byte[] needle) throws IOException {
        SortedSet<String> names = new TreeSet<>();
        Path directory = root;
        for (String segment : packageName.split("\\.")) {
            directory = directory.resolve(segment);
        }
        if (!Files.isDirectory(directory)) {
            return names;
        }
        try (Stream<Path> files = Files.walk(directory)) {
            for (Path file : (Iterable<Path>) files::iterator) {
                String relative = toResourceName(root.relativize(file));
                if (isClassIn(relative, "") && Files.isRegularFile(file) && contains(Files.readAllBytes(file), needle)) {
                    names.add(binaryName(relative));
                }
            }
        }
        return names;
    }

    /**
     * Scans a package within a jar.
     *
     * @param jar the jar
     * @param entryPrefix the path within the jar at which the class hierarchy starts; empty for an
     *     ordinary jar, something like {@code BOOT-INF/classes/} for a repackaged one
     * @param packageName the package to scan, together with its sub-packages
     * @param needle the bytes a class file must contain to be reported
     * @return binary class names, in ordinal order
     * @throws IOException if the jar cannot be read
     */
    public static SortedSet<String> scanJar(JarFile jar, String entryPrefix, String packageName, byte[] needle)
            throws IOException {
        String directory = entryPrefix + packageName.replace('.', '/') + '/';
        SortedSet<String> names = new TreeSet<>();
        Enumeration<JarEntry> entries = jar.entries();
        while (entries.hasMoreElements()) {
            JarEntry entry = entries.nextElement();
            String name = entry.getName();
            if (entry.isDirectory() || !isClassIn(name, directory)) {
                continue;
            }
            try (InputStream in = jar.getInputStream(entry)) {
                if (contains(in.readAllBytes(), needle)) {
                    names.add(binaryName(name.substring(entryPrefix.length())));
                }
            }
        }
        return names;
    }

    /**
     * Returns whether {@code haystack} contains {@code needle}. A plain search: class files are
     * small and the needle's first byte ({@code L}) rules most positions out at once.
     *
     * @param haystack the bytes to search
     * @param needle the bytes to find
     * @return {@code true} if found
     */
    public static boolean contains(byte[] haystack, byte[] needle) {
        int last = haystack.length - needle.length;
        if (needle.length == 0) {
            return true;
        }
        byte first = needle[0];
        outer:
        for (int i = 0; i <= last; i++) {
            if (haystack[i] != first) {
                continue;
            }
            for (int j = 1; j < needle.length; j++) {
                if (haystack[i + j] != needle[j]) {
                    continue outer;
                }
            }
            return true;
        }
        return false;
    }

    private static ModuleReference moduleReference(Class<?> marker, Module module) {
        ModuleLayer layer = module.getLayer();
        Optional<ModuleReference> reference = layer == null
                ? Optional.empty()
                : layer.configuration().findModule(module.getName()).map(ResolvedModule::reference);
        return reference.orElseThrow(() -> new IllegalArgumentException(
                "Cannot list the classes of module '" + module.getName() + "', which contains '" + marker.getName()
                        + "': it was not defined through a module layer. Register its entities with "
                        + "registerEntity or registerEntities instead."));
    }

    /**
     * Scans the class-path location that defined {@code marker}. The code source is preferred
     * because it names exactly where the class was defined from, regardless of delegation; the
     * class's resource URL is the fallback for loaders that report no code source, or a non-file
     * one, such as nested jars.
     */
    private static SortedSet<String> scanLocation(Class<?> marker, String packageName, byte[] needle)
            throws IOException {
        CodeSource codeSource = marker.getProtectionDomain().getCodeSource();
        URL location = codeSource == null ? null : codeSource.getLocation();
        if (location != null && "file".equals(location.getProtocol())) {
            Path path = toPath(location);
            if (Files.isDirectory(path)) {
                return scanDirectory(path, packageName, needle);
            }
            if (Files.isRegularFile(path)) {
                try (JarFile jar = new JarFile(path.toFile())) {
                    return scanJar(jar, "", packageName, needle);
                }
            }
        }

        String classFile = marker.getName().replace('.', '/') + CLASS_SUFFIX;
        ClassLoader loader = marker.getClassLoader();
        URL resource = loader == null ? null : loader.getResource(classFile);
        if (resource != null) {
            if ("file".equals(resource.getProtocol())) {
                Path file = toPath(resource);
                Path root = file;
                for (int i = 0, segments = classFile.split("/").length; i < segments; i++) {
                    root = root.getParent();
                }
                return scanDirectory(root, packageName, needle);
            }
            URLConnection connection = resource.openConnection();
            if (connection instanceof JarURLConnection) {
                JarURLConnection jarConnection = (JarURLConnection) connection;
                jarConnection.setUseCaches(false);
                String entryName = jarConnection.getEntryName();
                String entryPrefix = entryName.substring(0, entryName.length() - classFile.length());
                try (JarFile jar = jarConnection.getJarFile()) {
                    return scanJar(jar, entryPrefix, packageName, needle);
                }
            }
        }

        throw new IllegalArgumentException(
                "Cannot list the classes of package '" + packageName + "': '" + marker.getName() + "' was loaded from "
                        + (resource != null ? "'" + resource + "'" : location != null ? "'" + location + "'" : "an unknown location")
                        + ", which is not a directory, a jar or a named module. Register the entities with "
                        + "registerEntity or registerEntities instead.");
    }

    private static Path toPath(URL url) {
        try {
            return Paths.get(url.toURI());
        } catch (URISyntaxException e) {
            throw new IllegalArgumentException("Cannot interpret the class location '" + url + "' as a path.", e);
        }
    }

    /** Whether a resource name is a class file under {@code directory}, other than module or package info. */
    private static boolean isClassIn(String resource, String directory) {
        return resource.startsWith(directory)
                && resource.endsWith(CLASS_SUFFIX)
                && !resource.endsWith("module-info.class")
                && !resource.endsWith("package-info.class");
    }

    /** {@code com/example/Outer$Inner.class} to {@code com.example.Outer$Inner}. */
    private static String binaryName(String resource) {
        return resource.substring(0, resource.length() - CLASS_SUFFIX.length()).replace('/', '.');
    }

    /** A relative path as a {@code /}-separated resource name, whatever the platform's separator. */
    private static String toResourceName(Path relative) {
        StringBuilder name = new StringBuilder();
        for (Path segment : relative) {
            if (name.length() > 0) {
                name.append('/');
            }
            name.append(segment);
        }
        return name.toString();
    }
}
