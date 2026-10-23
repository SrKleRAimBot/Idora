import { declaredPrefixOf, nameOf } from './entity-prefix.js';
import { DuplicateEntityPrefixError, EntityPrefixNotRegisteredError, InvalidEntityPrefixError } from './errors.js';
import { foldPrefix, SEPARATOR, validatePrefix } from './internal/prefix.js';
import type { EntityModule, EntityRegistration, EntityType } from './types.js';

/**
 * The immutable set of entity-type to prefix mappings built when an {@link Idora} generator is
 * constructed.
 *
 * Reach for it when you need the prefix itself rather than a whole identifier — checking that an
 * incoming identifier belongs to the entity a route expects, say:
 *
 * ```ts
 * const isOrderId = (id: string) => id.startsWith(`${ids.registry.getPrefix(Order)}_`);
 * ```
 */
export class EntityPrefixRegistry {
  readonly #prefixes: ReadonlyMap<EntityType, string>;
  readonly #prefixesWithSeparator: ReadonlyMap<EntityType, string>;
  readonly #view: ReadonlyMap<EntityType, string>;

  /** @internal Built by {@link Idora}; see {@link RegistryBuilder}. */
  constructor(prefixes: ReadonlyMap<EntityType, string>) {
    this.#prefixes = prefixes;
    this.#view = new ReadonlyMapView(prefixes);
    this.#prefixesWithSeparator = new Map(
      Array.from(prefixes, ([entityType, prefix]) => [entityType, prefix + SEPARATOR] as const),
    );
  }

  /**
   * Every registered entity type and its prefix, in registration order. A read-only view: the
   * registry cannot be changed once built.
   */
  get registrations(): ReadonlyMap<EntityType, string> {
    return this.#view;
  }

  /**
   * Returns the prefix registered for `entityType`, without the trailing separator.
   *
   * @throws {EntityPrefixNotRegisteredError} `entityType` has no registered prefix.
   */
  getPrefix(entityType: EntityType): string {
    const prefix = this.#prefixes.get(entityType);
    if (prefix === undefined) {
      throw notRegistered(entityType);
    }

    return prefix;
  }

  /** Returns the prefix registered for `entityType`, or `undefined` if there is none. */
  findPrefix(entityType: EntityType): string | undefined {
    return this.#prefixes.get(entityType);
  }

  /** Whether `entityType` has a registered prefix. */
  has(entityType: EntityType): boolean {
    return this.#prefixes.has(entityType);
  }

  /**
   * @internal The prefix with the separator already appended, ready to be the start of an
   * identifier. Stored separately so generation is one concatenation rather than two.
   */
  prefixWithSeparator(entityType: EntityType): string {
    const prefix = this.#prefixesWithSeparator.get(entityType);
    if (prefix === undefined) {
      throw notRegistered(entityType);
    }

    return prefix;
  }
}

/**
 * A read-only window onto a map. `ReadonlyMap` is only a compile-time promise, and handing out the
 * map itself would let a cast reach in and change the registry; this has no mutators to reach.
 */
class ReadonlyMapView<K, V> implements ReadonlyMap<K, V> {
  readonly #map: ReadonlyMap<K, V>;

  constructor(map: ReadonlyMap<K, V>) {
    this.#map = map;
    Object.freeze(this);
  }

  get size(): number {
    return this.#map.size;
  }

  get(key: K): V | undefined {
    return this.#map.get(key);
  }

  has(key: K): boolean {
    return this.#map.has(key);
  }

  forEach(callback: (value: V, key: K, map: ReadonlyMap<K, V>) => void, thisArg?: unknown): void {
    this.#map.forEach((value, key) => callback.call(thisArg, value, key, this));
  }

  entries(): MapIterator<[K, V]> {
    return this.#map.entries();
  }

  keys(): MapIterator<K> {
    return this.#map.keys();
  }

  values(): MapIterator<V> {
    return this.#map.values();
  }

  [Symbol.iterator](): MapIterator<[K, V]> {
    return this.#map[Symbol.iterator]();
  }
}

/**
 * Collects registrations and polices them as they arrive. Used once, by the {@link Idora}
 * constructor, and then discarded.
 *
 * Every registration — explicit or decorated — goes through {@link register}, which is the single
 * place prefixes are validated and both kinds of collision are detected. That is what lets the
 * two styles mix freely and police each other, in either order.
 *
 * @internal
 */
export class RegistryBuilder {
  readonly #prefixesByEntityType = new Map<EntityType, string>();

  /** The reverse lookup, keyed by ASCII-case-folded prefix, so `usr` and `USR` collide. */
  readonly #entityTypesByFoldedPrefix = new Map<string, EntityType>();

  register(entityType: EntityType, prefix: unknown): void {
    if (typeof entityType !== 'function') {
      throw new TypeError(`An entity type must be a class, but received ${entityType === null ? 'null' : typeof entityType}.`);
    }

    validatePrefix(prefix, 'prefix');

    // Checked first, because "you already registered this type" is the more useful message when
    // both problems are true.
    const existingPrefix = this.#prefixesByEntityType.get(entityType);
    if (existingPrefix !== undefined) {
      throw duplicateEntityType(entityType, existingPrefix, prefix);
    }

    const folded = foldPrefix(prefix);
    const conflictingEntityType = this.#entityTypesByFoldedPrefix.get(folded);
    if (conflictingEntityType !== undefined) {
      throw new DuplicateEntityPrefixError(
        `The entity prefix '${prefix}' is already registered for '${nameOf(conflictingEntityType)}' and cannot also be used for '${nameOf(entityType)}'. Prefixes must identify exactly one entity type, and are compared ignoring case. Choose a different prefix for '${nameOf(entityType)}'.`,
        prefix,
        entityType,
        conflictingEntityType,
      );
    }

    this.#prefixesByEntityType.set(entityType, prefix);
    this.#entityTypesByFoldedPrefix.set(folded, entityType);
  }

  /** Registers one entry from {@link IdoraOptions.entities}. */
  registerEntry(entry: EntityRegistration): void {
    if (Array.isArray(entry)) {
      const [entityType, prefix] = entry as readonly [EntityType, string];
      this.register(entityType, prefix);
      return;
    }

    const entityType = entry as EntityType;
    if (typeof entityType !== 'function') {
      throw new TypeError(
        `Each entry in 'entities' must be a [Class, 'prefix'] pair or a class decorated with @entityPrefix, but received ${entityType === null ? 'null' : typeof entityType}.`,
      );
    }

    const declaration = declaredPrefixOf(entityType);
    if (!declaration.declared) {
      throw new TypeError(
        `${nameOf(entityType)} was listed in 'entities' on its own, but carries no @entityPrefix declaration. Decorate it with @entityPrefix('abc'), or list it as [${nameOf(entityType)}, 'abc'].`,
      );
    }

    this.#registerDeclared(entityType, declaration.prefix);
  }

  /**
   * Registers every exported class in `module` that carries an {@link entityPrefix} declaration.
   *
   * Exports are visited in ordinal order of class name, so that a clash between two of them is
   * reported the same way round on every run, whatever order the module happened to list them in.
   * A class exported under two names (a default and a named export, say) is registered once.
   */
  registerModule(module: EntityModule): void {
    if (module === null || typeof module !== 'object') {
      throw new TypeError(`Each entry in 'modules' must be a module namespace object, but received ${module === null ? 'null' : typeof module}.`);
    }

    const declared = new Set<EntityType>();
    for (const key of Object.keys(module)) {
      const value = module[key];
      if (typeof value === 'function' && declaredPrefixOf(value as EntityType).declared) {
        declared.add(value as EntityType);
      }
    }

    const ordered = Array.from(declared).sort((a, b) => compareOrdinal(nameOf(a), nameOf(b)));
    for (const entityType of ordered) {
      this.#registerDeclared(entityType, declaredPrefixOf(entityType).prefix);
    }
  }

  build(): EntityPrefixRegistry {
    return new EntityPrefixRegistry(new Map(this.#prefixesByEntityType));
  }

  /**
   * Registers a decorated class, restating any complaint about its prefix in terms of the
   * decorator. An explicit registration with a bad prefix leaves a stack trace pointing at the line
   * that made it; a decorator leaves nothing, so the message names the class instead.
   */
  #registerDeclared(entityType: EntityType, prefix: unknown): void {
    // Reachable from plain JavaScript, where nothing stops `@entityPrefix(undefined)`.
    if (typeof prefix !== 'string') {
      throw new TypeError(
        `The @entityPrefix decorator on '${nameOf(entityType)}' does not declare a valid prefix. The prefix is ${prefix === null ? 'null' : typeof prefix}; supply 1 to 16 ASCII letters or digits, for example 'usr'.`,
      );
    }

    try {
      this.register(entityType, prefix);
    } catch (error) {
      if (error instanceof InvalidEntityPrefixError) {
        throw new InvalidEntityPrefixError(
          `The @entityPrefix decorator on '${nameOf(entityType)}' does not declare a valid prefix. ${error.message}`,
          error.prefix,
          error.reason,
          error.index,
          entityType,
        );
      }

      throw error;
    }
  }
}

function duplicateEntityType(entityType: EntityType, existingPrefix: string, attemptedPrefix: string): DuplicateEntityPrefixError {
  const name = nameOf(entityType);

  // Both registrations agreeing on the prefix means the mapping was declared twice rather than
  // declared differently: a different mistake, with a different fix. Saying "cannot also be
  // registered with 'usr'" when it already is 'usr' just reads as a bug.
  const message =
    existingPrefix === attemptedPrefix
      ? `The entity type '${name}' is already registered with the prefix '${existingPrefix}', and cannot be registered a second time. Each entity type may be registered exactly once. Look for a class listed twice in 'entities', a class carrying an @entityPrefix decorator that is also registered as a pair, or a module listed in 'modules' that exports a class registered elsewhere.`
      : `The entity type '${name}' is already registered with the prefix '${existingPrefix}' and cannot also be registered with '${attemptedPrefix}'. Each entity type may have only one prefix. Remove whichever registration is wrong: a [${name}, '...'] pair, an @entityPrefix decorator, or a module that exports the class.`;

  return new DuplicateEntityPrefixError(message, attemptedPrefix, entityType, entityType);
}

function notRegistered(entityType: EntityType): EntityPrefixNotRegisteredError {
  const name = typeof entityType === 'function' ? nameOf(entityType) : String(entityType);
  return new EntityPrefixNotRegisteredError(
    `No entity prefix is registered for '${name}'. Register one when creating the generator, for example new Idora({ entities: [[${name}, 'abc']] }), decorate the class with @entityPrefix('abc'), or call generateWithPrefix with an explicit prefix.`,
    entityType,
  );
}

/** Compares UTF-16 code units, like .NET's StringComparer.Ordinal, independent of locale. */
function compareOrdinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}
