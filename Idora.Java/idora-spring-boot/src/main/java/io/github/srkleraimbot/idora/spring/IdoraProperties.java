package io.github.srkleraimbot.idora.spring;

import io.github.srkleraimbot.idora.IdGenerationStrategy;
import org.springframework.boot.context.properties.ConfigurationProperties;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The {@code idora.*} configuration properties, from which {@link IdoraAutoConfiguration} builds
 * the application's {@link io.github.srkleraimbot.idora.IdoraGenerator}.
 *
 * <pre>
 * idora.strategy=uuid-v7
 * idora.scan.packages=com.example.billing,com.example.accounts
 * idora.prefixes[com.example.vendor.Widget]=wdg
 * </pre>
 */
@ConfigurationProperties("idora")
public class IdoraProperties {

    /** The value format: ulid (the default) or uuid-v7. */
    private IdGenerationStrategy strategy = IdGenerationStrategy.ULID;

    /** Where @EntityPrefix types are collected from. */
    private final Scan scan = new Scan();

    /**
     * Explicit registrations, from fully qualified class name to prefix, for types that cannot carry
     * an @EntityPrefix annotation. Use the bracket notation, since class names contain dots.
     */
    private Map<String, String> prefixes = new LinkedHashMap<>();

    /** Creates the properties with their defaults; Spring binds them. */
    public IdoraProperties() {
    }

    /**
     * Returns the value format.
     *
     * @return the strategy; {@link IdGenerationStrategy#ULID} unless configured
     */
    public IdGenerationStrategy getStrategy() {
        return strategy;
    }

    /**
     * Sets the value format.
     *
     * @param strategy the strategy
     */
    public void setStrategy(IdGenerationStrategy strategy) {
        this.strategy = strategy;
    }

    /**
     * Returns the annotation scanning settings.
     *
     * @return the scanning settings; never {@code null}
     */
    public Scan getScan() {
        return scan;
    }

    /**
     * Returns the explicit registrations.
     *
     * @return class name to prefix, in the order configured
     */
    public Map<String, String> getPrefixes() {
        return prefixes;
    }

    /**
     * Sets the explicit registrations.
     *
     * @param prefixes class name to prefix
     */
    public void setPrefixes(Map<String, String> prefixes) {
        this.prefixes = prefixes;
    }

    /** Where {@link io.github.srkleraimbot.idora.EntityPrefix} types are collected from. */
    public static class Scan {

        /** Whether to register the @EntityPrefix types found in the scanned packages. */
        private boolean enabled = true;

        /**
         * Packages to scan, sub-packages included. When empty, the application's own packages are
         * scanned: those of its @SpringBootApplication (or @AutoConfigurationPackage) class, as
         * @EntityScan does.
         */
        private List<String> packages = new ArrayList<>();

        /** Creates the settings with their defaults. */
        public Scan() {
        }

        /**
         * Returns whether scanning is enabled.
         *
         * @return {@code true} unless disabled
         */
        public boolean isEnabled() {
            return enabled;
        }

        /**
         * Enables or disables scanning.
         *
         * @param enabled whether to scan
         */
        public void setEnabled(boolean enabled) {
            this.enabled = enabled;
        }

        /**
         * Returns the packages to scan.
         *
         * @return package names; empty to scan the application's own packages
         */
        public List<String> getPackages() {
            return packages;
        }

        /**
         * Sets the packages to scan.
         *
         * @param packages package names
         */
        public void setPackages(List<String> packages) {
            this.packages = packages;
        }
    }
}
