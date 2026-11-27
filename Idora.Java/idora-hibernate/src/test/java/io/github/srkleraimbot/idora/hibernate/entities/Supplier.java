package io.github.srkleraimbot.idora.hibernate.entities;

import io.github.srkleraimbot.idora.EntityPrefix;
import jakarta.persistence.Entity;

/** Inherits its identifier from the same mapped superclass. */
@Entity
@EntityPrefix("sup")
public class Supplier extends BaseEntity {
}
