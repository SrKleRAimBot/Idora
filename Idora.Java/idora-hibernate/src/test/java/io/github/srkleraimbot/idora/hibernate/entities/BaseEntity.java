package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Id;
import jakarta.persistence.MappedSuperclass;

/** One identifier declaration shared by several unrelated entities. */
@MappedSuperclass
public abstract class BaseEntity {

    @Id
    @IdoraGeneratedId
    private String id;

    public String getId() {
        return id;
    }
}
