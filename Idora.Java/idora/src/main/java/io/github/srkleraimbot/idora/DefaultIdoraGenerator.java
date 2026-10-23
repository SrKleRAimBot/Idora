package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.internal.Stripe;

/**
 * The only {@link IdoraGenerator} implementation. Immutable; every mutable piece of state lives in
 * the striped pool behind {@link Stripe}, which is shared by every generator in the process.
 */
final class DefaultIdoraGenerator implements IdoraGenerator {

    private final IdGenerationStrategy strategy;
    private final DefaultEntityPrefixRegistry registry;

    /** Read on every call, so cached as a boolean rather than switching on the enum. */
    private final boolean uuidV7;

    DefaultIdoraGenerator(IdGenerationStrategy strategy, DefaultEntityPrefixRegistry registry) {
        this.strategy = strategy;
        this.registry = registry;
        this.uuidV7 = strategy == IdGenerationStrategy.UUID_V7;
    }

    @Override
    public String generate(Class<?> entityType) {
        byte[] prefix = registry.prefixBytesWithSeparator(entityType);
        Stripe stripe = Stripe.current();
        return uuidV7 ? stripe.uuidV7(prefix) : stripe.ulid(prefix);
    }

    @Override
    public String generate(String entityPrefix) {
        EntityPrefixValidator.validate(entityPrefix, "entityPrefix");
        Stripe stripe = Stripe.current();
        return uuidV7 ? stripe.uuidV7(entityPrefix) : stripe.ulid(entityPrefix);
    }

    @Override
    public EntityPrefixRegistry registry() {
        return registry;
    }

    @Override
    public IdGenerationStrategy strategy() {
        return strategy;
    }

    @Override
    public String toString() {
        return "IdoraGenerator[strategy=" + strategy + ", registrations=" + registry.registrations() + "]";
    }
}
