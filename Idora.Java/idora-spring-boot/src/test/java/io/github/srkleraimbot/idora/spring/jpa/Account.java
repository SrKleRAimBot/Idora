package io.github.srkleraimbot.idora.spring.jpa;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;

/** An entity keyed by a generated identifier, registered through the annotation scan. */
@Entity
@EntityPrefix("acc")
public class Account {

    @Id
    @IdoraGeneratedId
    private IdoraId id;

    private IdoraId ownerId;

    protected Account() {
    }

    public Account(IdoraId ownerId) {
        this.ownerId = ownerId;
    }

    public IdoraId getId() {
        return id;
    }

    public IdoraId getOwnerId() {
        return ownerId;
    }
}
