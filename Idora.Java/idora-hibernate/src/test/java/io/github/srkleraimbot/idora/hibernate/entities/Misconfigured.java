package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;

/** Entities that must each fail Hibernate's start-up. */
public final class Misconfigured {

    private Misconfigured() {
    }

    /** Names both a prefix and an entity. */
    @Entity
    public static class BothAttributes {
        @Id
        @IdoraGeneratedId(prefix = "abc", entity = Order.class)
        private String id;
    }

    /** An identifier type that cannot hold an Idora identifier. */
    @Entity
    public static class NumericId {
        @Id
        @IdoraGeneratedId(prefix = "num")
        private Long id;
    }

    /** A prefix that breaks the rules. */
    @Entity
    public static class BadPrefix {
        @Id
        @IdoraGeneratedId(prefix = "b_d")
        private String id;
    }

    /** Carries no prefix and is never registered. */
    @Entity
    public static class Unregistered {
        @Id
        @IdoraGeneratedId
        private String id;
    }
}
