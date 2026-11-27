package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.EntityPrefix;
import jakarta.persistence.Entity;

/** Inherits its identifier from a mapped superclass. */
@Entity
@EntityPrefix("cus")
public class Customer extends BaseEntity {
}
