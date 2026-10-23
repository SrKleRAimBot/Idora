package io.github.srkleraimbot.idora.fixturesx;

import io.github.srkleraimbot.idora.EntityPrefix;

/**
 * Lives in a package whose name merely starts with {@code fixtures}. It is not a sub-package, so a
 * scan of {@code fixtures} must not see it; if it did, its prefix would collide with
 * {@code FixtureUser}'s and fail the scan.
 */
@EntityPrefix("fxusr")
public final class FixtureImpostor {
}
