package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Inheritance;
import jakarta.persistence.InheritanceType;

/** The root of a hierarchy; its subclasses share its identifier column, and so its prefix. */
@Entity
@Inheritance(strategy = InheritanceType.SINGLE_TABLE)
@EntityPrefix("ani")
public class Animal {

    @Id
    @IdoraGeneratedId
    private String id;

    public String getId() {
        return id;
    }
}
