import { entityPrefix } from '../../src/index.js';

/*
 * A well-formed set of decorated classes, and enough undecorated exports to prove the module scan
 * is selective. Every prefix starts with `fx`, so a stray registration from elsewhere is obvious in
 * a failing assertion. Clashing and malformed declarations live in their own fixture modules, so
 * scanning this one always succeeds and the happy-path tests can assert an exact set.
 */

@entityPrefix('fxusr')
export class FixtureUser {}

@entityPrefix('fxord')
export class FixtureOrder {}

/** Abstract classes are keys like any other. */
@entityPrefix('fxabs')
export abstract class FixtureAbstract {}

/** Carries no decorator, and must not be registered. */
export class FixtureUndecorated {}

/**
 * Base of FixtureDerived. Together they guard the not-inherited rule: were the prefix inherited,
 * scanning this module would find two classes claiming `fxbase` and every scan would fail.
 */
@entityPrefix('fxbase')
export class FixtureBase {}

export class FixtureDerived extends FixtureBase {}

/** Exported twice — here and as the default — to prove a class is registered once per module. */
export default FixtureUser;

/** Things a module exports that are not classes at all. */
export const fixtureConstant = 42;
export function fixtureFunction(): number {
  return fixtureConstant;
}
