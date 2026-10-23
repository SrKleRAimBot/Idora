package io.github.srkleraimbot.idora.unscanned.invalid;

import io.github.srkleraimbot.idora.EntityPrefix;

/** The only type in its package, with an invalid prefix, so that a scan of it must fail. */
@EntityPrefix("bad prefix")
public final class InvalidAnnotated {
}
