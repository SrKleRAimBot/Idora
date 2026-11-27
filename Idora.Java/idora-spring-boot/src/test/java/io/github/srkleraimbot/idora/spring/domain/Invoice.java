package io.github.srkleraimbot.idora.spring.domain;

import io.github.srkleraimbot.idora.EntityPrefix;

/** An annotated record. */
@EntityPrefix("inv")
public record Invoice(String id) {
}
