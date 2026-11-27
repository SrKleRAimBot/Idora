package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import io.github.srkleraimbot.idora.hibernate.IdoraIdConverter;
import jakarta.persistence.Convert;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;

/** An explicit prefix, needing no registry, and an attribute mapped through the JPA converter. */
@Entity
public class Invoice {

    @Id
    @IdoraGeneratedId(prefix = "inv")
    private String id;

    @Convert(converter = IdoraIdConverter.class)
    private IdoraId orderId;

    protected Invoice() {
    }

    public Invoice(IdoraId orderId) {
        this.orderId = orderId;
    }

    public String getId() {
        return id;
    }

    public IdoraId getOrderId() {
        return orderId;
    }
}
