package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.IdoraId;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** An IdoraId-keyed entity with a plain IdoraId attribute, mapped by the type contributor alone. */
@Entity
@Table(name = "orders")
@EntityPrefix("ord")
public class Order {

    @Id
    @IdoraGeneratedId
    private IdoraId id;

    private IdoraId customerId;

    protected Order() {
    }

    public Order(IdoraId customerId) {
        this.customerId = customerId;
    }

    public IdoraId getId() {
        return id;
    }

    public IdoraId getCustomerId() {
        return customerId;
    }
}
