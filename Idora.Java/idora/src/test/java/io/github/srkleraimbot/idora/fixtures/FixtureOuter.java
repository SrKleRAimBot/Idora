package io.github.srkleraimbot.idora.fixtures;

import io.github.srkleraimbot.idora.EntityPrefix;

/** Not annotated itself; holds nested annotated types. */
public final class FixtureOuter {

    private FixtureOuter() {
    }

    /** A public nested type. */
    @EntityPrefix("fxline")
    public static final class Line {
    }

    /** A private nested type, to prove the scan sees past access modifiers. */
    @EntityPrefix("fxsec")
    private static final class Secret {
    }

    /** Referenced so the compiler does not consider the private nested type unused. */
    static Class<?> secretType() {
        return Secret.class;
    }
}
