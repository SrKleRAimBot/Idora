package io.github.srkleraimbot.idora.fixtures;

import io.github.srkleraimbot.idora.EntityPrefix;

/**
 * Base of {@link FixtureDerived}. Together they guard the annotation not being inherited: were it
 * inherited, scanning this package would find two types claiming {@code fxbase} and every scan
 * test would fail with a duplicate.
 */
@EntityPrefix("fxbase")
public class FixtureBase {
}
