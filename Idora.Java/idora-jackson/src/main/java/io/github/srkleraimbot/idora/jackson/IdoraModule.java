package io.github.srkleraimbot.idora.jackson;

import com.fasterxml.jackson.core.Version;
import com.fasterxml.jackson.core.util.VersionUtil;
import com.fasterxml.jackson.databind.Module;
import com.fasterxml.jackson.databind.module.SimpleDeserializers;
import com.fasterxml.jackson.databind.module.SimpleKeyDeserializers;
import com.fasterxml.jackson.databind.module.SimpleSerializers;
import io.github.srkleraimbot.idora.IdoraId;

/**
 * Teaches Jackson to read and write {@link IdoraId} values as JSON strings.
 *
 * <pre>{@code
 * ObjectMapper mapper = JsonMapper.builder()
 *     .addModule(new IdoraModule())
 *     .build();
 *
 * record User(IdoraId id, String name) {}
 *
 * mapper.writeValueAsString(new User(IdoraId.parse("usr_01k2f9v8ta7m3x0qbrn4dzeh5c"), "Ada"));
 * // {"id":"usr_01k2f9v8ta7m3x0qbrn4dzeh5c","name":"Ada"}
 * }</pre>
 *
 * <p>Identifiers are written exactly as their text, and read back with the same strict parse as
 * {@link IdoraId#parse(CharSequence)}: a malformed identifier fails deserialization with an
 * {@link com.fasterxml.jackson.databind.exc.InvalidFormatException} whose message says which rule
 * it broke and whose path names the offending property. Annotate a property with
 * {@link IdoraIdPrefix} to also require a particular prefix. Map keys of type {@code IdoraId} are
 * supported both ways, and JSON {@code null} stays {@code null}.
 *
 * <p>The module is also registered for {@link java.util.ServiceLoader}, so
 * {@code ObjectMapper.findAndRegisterModules()} picks it up. Spring Boot applications using
 * {@code idora-spring-boot} get it registered automatically. Registering it twice is harmless.
 *
 * <p>Instances are stateless and immutable; one may be shared by any number of mappers.
 */
public final class IdoraModule extends Module {

    /** Reported by {@link #version()}; the jar's own version, or unknown outside a jar. */
    private static final Version VERSION = VersionUtil.parseVersion(
            IdoraModule.class.getPackage().getImplementationVersion(), "io.github.srkleraimbot", "idora-jackson");

    /** Creates the module. It has no options; per-property behaviour comes from {@link IdoraIdPrefix}. */
    public IdoraModule() {
    }

    /**
     * Returns the module's name, {@code "IdoraModule"}.
     *
     * @return the name
     */
    @Override
    public String getModuleName() {
        return "IdoraModule";
    }

    /**
     * Returns the version of {@code idora-jackson} on the class path, read from its jar manifest.
     *
     * @return the version, or {@link Version#unknownVersion()} when not running from the jar
     */
    @Override
    public Version version() {
        return VERSION;
    }

    /**
     * Registers the {@link IdoraId} serializer, deserializer and key deserializer.
     *
     * @param context the mapper's set-up context
     */
    @Override
    public void setupModule(SetupContext context) {
        SimpleSerializers serializers = new SimpleSerializers();
        serializers.addSerializer(IdoraId.class, IdoraIdSerializer.INSTANCE);
        context.addSerializers(serializers);

        SimpleDeserializers deserializers = new SimpleDeserializers();
        deserializers.addDeserializer(IdoraId.class, IdoraIdDeserializer.ANY_PREFIX);
        context.addDeserializers(deserializers);

        SimpleKeyDeserializers keyDeserializers = new SimpleKeyDeserializers();
        keyDeserializers.addDeserializer(IdoraId.class, IdoraIdKeyDeserializer.ANY_PREFIX);
        context.addKeyDeserializers(keyDeserializers);
    }
}
