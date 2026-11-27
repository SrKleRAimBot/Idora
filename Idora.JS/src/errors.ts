import type { EntityType } from './types.js';

/**
 * The base class of every error Idora throws.
 *
 * Catch this if you want to handle any misuse of the generator uniformly; check for one of the
 * subclasses if you care which problem it was. Each subclass carries the details as properties,
 * not just in the message.
 *
 * ```ts
 * try {
 *   new Idora({ entities: [[Account, 'acc'], [Accrual, 'acc']] });
 * } catch (error) {
 *   if (error instanceof DuplicateEntityPrefixError) {
 *     error.prefix;                 // 'acc'
 *     error.entityType;             // Accrual
 *     error.conflictingEntityType;  // Account
 *   }
 * }
 * ```
 */
export abstract class IdoraError extends Error {
  // `{ cause }` is spelled out rather than typed as the standard ErrorOptions, which is an ES2022
  // library type: naming it in this public declaration would stop a consumer compiling against an
  // older `lib` from importing idora.js at all, as it could before the cause existed.
  protected constructor(message: string, options?: { readonly cause?: unknown }) {
    super(message, options);
    this.name = new.target.name;
  }
}

/** Why a prefix was rejected. */
export type InvalidPrefixReason =
  /** The prefix is the empty string. */
  | 'empty'
  /** The prefix is longer than 16 characters. */
  | 'tooLong'
  /** The prefix contains the `_` separator. */
  | 'separator'
  /** The prefix contains a character that is not an ASCII letter or digit. */
  | 'character';

/**
 * Thrown when an entity prefix breaks the rules: it must be 1 to 16 ASCII letters or digits.
 *
 * The underscore is excluded because it separates the prefix from the value; if a prefix could
 * contain one, an identifier could no longer be split back into its two parts unambiguously.
 *
 * When the prefix came from an {@link entityPrefix} decorator, the message names the class that
 * carries it, because a decorator leaves no line of your code in the stack trace to point at.
 */
export class InvalidEntityPrefixError extends IdoraError {
  /** The prefix that was rejected. */
  readonly prefix: string;

  /** Which rule the prefix broke. */
  readonly reason: InvalidPrefixReason;

  /**
   * The UTF-16 index of the first offending character, for the `separator` and `character`
   * reasons; otherwise `undefined`.
   */
  readonly index: number | undefined;

  /**
   * The class whose {@link entityPrefix} declaration was rejected, when the prefix came from one;
   * otherwise `undefined`.
   */
  readonly entityType: EntityType | undefined;

  constructor(
    message: string,
    prefix: string,
    reason: InvalidPrefixReason,
    index?: number,
    entityType?: EntityType,
  ) {
    super(message);
    this.prefix = prefix;
    this.reason = reason;
    this.index = index;
    this.entityType = entityType;
  }
}

/**
 * Thrown at start-up when a prefix would no longer identify exactly one entity type.
 *
 * That covers two mistakes. Giving one prefix to two different types defeats the point of a
 * prefix: you could no longer tell from `acc_01k2f9…` whether you were looking at an `Account` or
 * an `Accrual`. Registering one type twice is almost always a copy-paste slip, and would leave it
 * unclear which prefix wins.
 *
 * Prefixes are compared ignoring ASCII case, so `usr` and `USR` collide. It does not matter where
 * the two registrations came from — two explicit entries, two decorators, or one of each.
 */
export class DuplicateEntityPrefixError extends IdoraError {
  /** The prefix involved in the clash: the one being registered when it was detected. */
  readonly prefix: string;

  /** The entity type that was being registered when the clash was detected. */
  readonly entityType: EntityType;

  /**
   * The entity type that already held the prefix. When one type is registered twice this is the
   * same value as {@link entityType}.
   */
  readonly conflictingEntityType: EntityType;

  constructor(message: string, prefix: string, entityType: EntityType, conflictingEntityType: EntityType) {
    super(message);
    this.prefix = prefix;
    this.entityType = entityType;
    this.conflictingEntityType = conflictingEntityType;
  }
}

/**
 * Thrown by {@link Idora.generate}, {@link EntityPrefixRegistry.getPrefix} and the registry-aware
 * parsing methods when the entity type has no registered prefix.
 *
 * Idora deliberately refuses to invent a prefix. Guessing one from the class name would produce
 * identifiers that silently change the day somebody renames the class — or that a minifier
 * mangles — and two classes sharing a name would quietly collide. Register the type, or use
 * {@link Idora.generateWithPrefix} if the prefix genuinely is not known until run time.
 */
export class EntityPrefixNotRegisteredError extends IdoraError {
  /** The entity type that has no registered prefix. */
  readonly entityType: EntityType;

  constructor(message: string, entityType: EntityType) {
    super(message);
    this.entityType = entityType;
  }
}

/** Why an identifier was rejected, in the order the checks run. */
export type InvalidIdReason =
  /** The text contains no `_`, so it cannot be split into a prefix and a value. */
  | 'noSeparator'
  /** Everything before the first `_` is not a valid prefix. The prefix error is the `cause`. */
  | 'prefix'
  /** Everything after the first `_` is neither a lowercase ULID nor a lowercase UUIDv7. */
  | 'value'
  /** The identifier is well formed, but its prefix is not the one that was asked for. */
  | 'prefixMismatch';

/**
 * Thrown by {@link IdoraId.parse} and the registry-aware parsing methods when text is not an Idora
 * identifier, or not one with the prefix the caller expected.
 *
 * Parsing is strict: it accepts exactly what a generator emits and nothing else — no surrounding
 * whitespace, no uppercase value, no UUID hyphens. The checks run in a fixed order and the first
 * failure is the {@link reason}, so the same text is rejected for the same reason by every Idora
 * package.
 *
 * ```ts
 * try {
 *   IdoraId.parse(request.params.id, 'usr');
 * } catch (error) {
 *   if (error instanceof InvalidIdError) {
 *     error.reason;         // 'prefixMismatch'
 *     error.text;           // 'ord_01k2f9v8ta7m3x0qbrn4dzeh5c'
 *     error.expectedPrefix; // 'usr'
 *   }
 * }
 * ```
 *
 * For the `prefix` reason the {@link InvalidEntityPrefixError} describing what is wrong with the
 * prefix is attached as the standard `cause`.
 */
export class InvalidIdError extends IdoraError {
  /** The text that was rejected, exactly as it was passed in. */
  readonly text: string;

  /** Which check the text failed. */
  readonly reason: InvalidIdReason;

  /** The prefix the caller asked for, when one was; otherwise `undefined`. */
  readonly expectedPrefix: string | undefined;

  /**
   * The entity type whose registered prefix was expected, when the identifier was parsed with one
   * of the registry-aware methods; otherwise `undefined`.
   */
  readonly entityType: EntityType | undefined;

  constructor(
    message: string,
    text: string,
    reason: InvalidIdReason,
    expectedPrefix?: string,
    entityType?: EntityType,
    cause?: InvalidEntityPrefixError,
  ) {
    super(message, cause === undefined ? undefined : { cause });
    this.text = text;
    this.reason = reason;
    this.expectedPrefix = expectedPrefix;
    this.entityType = entityType;
  }
}
