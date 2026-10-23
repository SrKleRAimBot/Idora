/**
 * Anything that can key a registration: a class, abstract or not.
 *
 * The class is used purely as a key — Idora never constructs it, and asks nothing of its shape.
 * TypeScript interfaces and type aliases do not exist at run time, so they cannot be keys; use the
 * class that implements them, or {@link Idora.generateWithPrefix}.
 */
export type EntityType = abstract new (...args: any[]) => unknown;

/** Which time-ordered value follows the prefix. */
export type IdStrategy =
  /**
   * A 26-character lowercase ULID, for example `01k2f9v8ta7m3x0qbrn4dzeh5c`. The default, and the
   * right choice unless something forces your hand: the same 128 bits as a UUID in six fewer
   * characters, in an alphabet without the characters people misread.
   */
  | 'ulid'
  /**
   * A 32-character lowercase UUIDv7 with no hyphens, for example
   * `0195e7b4a1c87f3d9e2b6c4a8f01d5e3`. For when a database column, schema or another service
   * insists on a real UUID.
   */
  | 'uuidv7';

/** One entry in {@link IdoraOptions.entities}: a class and its prefix, or a decorated class alone. */
export type EntityRegistration = readonly [entityType: EntityType, prefix: string] | EntityType;

/**
 * A module namespace — the object `import * as entities from './entities.js'` or
 * `await import('./entities.js')` gives you — or any object whose values may include classes.
 */
export type EntityModule = Readonly<Record<string, unknown>>;

/** Configuration for a new {@link Idora} generator. Read once, by the constructor. */
export interface IdoraOptions {
  /** The value format that follows the prefix. Defaults to `'ulid'`. */
  readonly strategy?: IdStrategy;

  /**
   * Entity types and the prefix each should carry, registered in the order given.
   *
   * Each entry is either a `[Class, 'prefix']` pair, or a class decorated with
   * {@link entityPrefix} on its own, in which case the prefix comes from the decorator.
   */
  readonly entities?: Iterable<EntityRegistration>;

  /**
   * Modules to collect {@link entityPrefix}-decorated classes from: every exported class that
   * carries the decorator is registered, and everything else is ignored.
   *
   * Registered after {@link entities}. Within one module, classes are registered in ordinal order
   * of their names so that a clash between two of them is always reported the same way round.
   */
  readonly modules?: Iterable<EntityModule>;
}
