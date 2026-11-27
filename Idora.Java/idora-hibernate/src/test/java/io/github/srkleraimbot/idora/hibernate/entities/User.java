package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** A String-keyed entity whose prefix comes from its annotation, through the registry. */
@Entity
@Table(name = "users")
@EntityPrefix("usr")
public class User {

    @Id
    @IdoraGeneratedId
    private String id;

    private String name;

    protected User() {
    }

    public User(String name) {
        this.name = name;
    }

    public String getId() {
        return id;
    }

    public String getName() {
        return name;
    }
}
