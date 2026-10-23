package io.github.srkleraimbot.idora.fixtures;

import io.github.srkleraimbot.idora.EntityPrefix;
import io.github.srkleraimbot.idora.support.InitialisationLog;

/**
 * Records when its static initialiser runs. Scanning must load it without initialising it, so
 * that a scan never runs application code. No test may touch it in a way that initialises it.
 */
@EntityPrefix("fxinit")
public final class FixtureWithStaticInitialiser {

    static {
        InitialisationLog.record(FixtureWithStaticInitialiser.class);
    }

    private FixtureWithStaticInitialiser() {
    }
}
