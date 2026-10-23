package io.github.srkleraimbot.idora.fixtures;

import io.github.srkleraimbot.idora.EntityPrefix;

/**
 * Not annotated, but its class file mentions the annotation type (through this field's
 * descriptor), so it passes the scanner's byte-level pre-filter. The reflective check must still
 * reject it.
 */
public final class FixtureMentionsAnnotation {

    /** Never set; it exists only for its type. */
    public EntityPrefix declared;
}
