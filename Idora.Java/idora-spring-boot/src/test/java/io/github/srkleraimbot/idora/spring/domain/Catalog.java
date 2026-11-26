package io.github.srkleraimbot.idora.spring.domain;

import io.github.srkleraimbot.idora.EntityPrefix;

/** Not annotated itself, but holds an annotated nested interface. */
public final class Catalog {

    private Catalog() {
    }

    /** An annotated nested interface. */
    @EntityPrefix("itm")
    public interface Item {
    }
}
