import { createUuidV7, UUID_LENGTH } from './internal/uuidv7.js';
import { SEPARATOR, validatePrefix } from './internal/prefix.js';
import { ULID_LENGTH, UlidStream } from './internal/ulid.js';
import { type EntityPrefixRegistry, RegistryBuilder } from './registry.js';
import type { EntityType, IdoraOptions, IdStrategy } from './types.js';

/**
 * The realm-wide ULID stream every generator shares.
 *
 * JavaScript runs one thread per realm, so one stream needs no locking and makes every ULID minted
 * in this realm — by any generator — strictly increasing. A worker or isolate loads its own copy of
 * this module, and with it a stream of its own.
 */
const sharedUlidStream = new UlidStream();

/**
 * Generates prefixed, time-ordered identifiers such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
 *
 * ```ts
 * import { Idora } from 'idora.js';
 *
 * export const ids = new Idora({
 *   entities: [
 *     [User, 'usr'],
 *     [Order, 'ord'],
 *   ],
 * });
 *
 * ids.generate(User);           // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
 * ids.generateWithPrefix('tmp'); // 'tmp_01k2f9v8tb9p5r2xnc7fkw3m0d'
 * ```
 *
 * Every identifier is `<prefix>_<value>`. The prefix says what it identifies, so an identifier in a
 * log line or a support ticket explains itself. The value is a ULID or a UUIDv7; both begin with a
 * millisecond timestamp, so identifiers sort into creation order as plain strings and rows inserted
 * with them land at the end of an index rather than scattering through it.
 *
 * Construct one per application and share it — export it from a module, or hand it to your DI
 * container as a singleton. The constructor validates every registration and throws straight away
 * on a mistake, so a misconfiguration fails at start-up rather than on the first request that
 * happens to create that kind of entity. Once built, a generator is immutable.
 */
export class Idora {
  /** The entity-type to prefix mappings this generator was built with. Read-only. */
  readonly registry: EntityPrefixRegistry;

  /** The value format that follows the prefix. */
  readonly strategy: IdStrategy;

  readonly #createValue: () => string;

  /**
   * @throws {InvalidEntityPrefixError} A registered prefix is not 1 to 16 ASCII letters or digits.
   * @throws {DuplicateEntityPrefixError} A prefix was registered for two entity types (ignoring
   * case), or an entity type was registered twice.
   * @throws {TypeError} An option has the wrong shape — an unknown strategy, or a class listed on
   * its own without an {@link entityPrefix} declaration.
   */
  constructor(options: IdoraOptions = {}) {
    const strategy = options.strategy ?? 'ulid';
    switch (strategy) {
      case 'ulid':
        this.#createValue = () => sharedUlidStream.next(Date.now());
        break;
      case 'uuidv7':
        this.#createValue = () => createUuidV7(Date.now());
        break;
      default:
        throw new TypeError(`'${String(strategy)}' is not an identifier strategy. Use 'ulid' (the default) or 'uuidv7'.`);
    }

    const builder = new RegistryBuilder();
    for (const entry of options.entities ?? []) {
      builder.registerEntry(entry);
    }

    for (const module of options.modules ?? []) {
      builder.registerModule(module);
    }

    this.registry = builder.build();
    this.strategy = strategy;
    Object.freeze(this);
  }

  /**
   * The number of characters in a generated value — the part after the separator. 26 for ULID,
   * 32 for UUIDv7. Add the prefix length plus one for the length of a whole identifier.
   */
  get valueLength(): number {
    return this.strategy === 'ulid' ? ULID_LENGTH : UUID_LENGTH;
  }

  /**
   * Generates an identifier for a registered entity type, using the prefix it was registered with.
   *
   * Prefer this to {@link generateWithPrefix}: the prefix lives in one place — your start-up
   * configuration — rather than being repeated as a string at every call site, so a typo is
   * impossible and renaming a prefix is a one-line change.
   *
   * ```ts
   * ids.generate(User);    // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
   * ids.generate(Comment); // throws EntityPrefixNotRegisteredError — Comment was never registered
   * ```
   *
   * @throws {EntityPrefixNotRegisteredError} `entityType` has no registered prefix.
   */
  generate(entityType: EntityType): string {
    return this.registry.prefixWithSeparator(entityType) + this.#createValue();
  }

  /**
   * Generates an identifier with a prefix supplied at the call site, for when the prefix genuinely
   * is not known until run time — a plugin's own types, or a prefix read from configuration.
   *
   * The prefix is validated on every call. For types known up front, register them and use
   * {@link generate}, which validates once at start-up.
   *
   * ```ts
   * ids.generateWithPrefix('tmp'); // 'tmp_01k2f9v8ta7m3x0qbrn4dzeh5c'
   * ```
   *
   * @param prefix 1 to 16 ASCII letters or digits, without the trailing underscore.
   * @throws {InvalidEntityPrefixError} `prefix` is empty, too long, or contains anything other than
   * an ASCII letter or digit.
   * @throws {TypeError} `prefix` is not a string.
   */
  generateWithPrefix(prefix: string): string {
    validatePrefix(prefix, 'prefix');
    return prefix + SEPARATOR + this.#createValue();
  }
}
