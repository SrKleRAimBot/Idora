package io.github.srkleraimbot.idora.support;

import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/** Records which fixture classes have had their static initialisers run. */
public final class InitialisationLog {

    private static final Set<String> INITIALISED = ConcurrentHashMap.newKeySet();

    private InitialisationLog() {
    }

    /** Called from a fixture's static initialiser. */
    public static void record(Class<?> type) {
        INITIALISED.add(type.getName());
    }

    /** Whether {@code type}'s static initialiser has run. */
    public static boolean wasInitialised(String typeName) {
        return INITIALISED.contains(typeName);
    }
}
