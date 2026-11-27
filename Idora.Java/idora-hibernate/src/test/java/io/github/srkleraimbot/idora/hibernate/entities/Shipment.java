package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;

/** Takes the prefix registered for another type. */
@Entity
public class Shipment {

    @Id
    @IdoraGeneratedId(entity = Order.class)
    private IdoraId id;

    public IdoraId getId() {
        return id;
    }
}
