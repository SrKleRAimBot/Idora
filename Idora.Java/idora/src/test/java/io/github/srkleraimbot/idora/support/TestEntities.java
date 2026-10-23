package io.github.srkleraimbot.idora.support;

/**
 * Stand-in entity types. Nothing is required of an entity beyond being a type, so these are empty
 * on purpose. None carries {@code @EntityPrefix}.
 */
public final class TestEntities {

    private TestEntities() {
    }

    /** A user. */
    public static final class User {
    }

    /** An order. */
    public static final class Order {
    }

    /** An invoice. */
    public static final class Invoice {
    }

    /** An account. */
    public static final class Account {
    }

    /** Used to prove that a type with no registration is rejected rather than guessed at. */
    public static final class UnregisteredEntity {
    }

    /** A record, to prove records work as registry keys too. */
    public record Payment(String id) {
    }

    /** An interface, to prove a marker interface works as a key. */
    public interface Auditable {
    }
}
