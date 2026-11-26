import { nameOf } from './entity-prefix.js';
import { InvalidEntityPrefixError, InvalidIdError, type InvalidIdReason } from './errors.js';
import { describe, isValidPrefix, SEPARATOR, validatePrefix } from './internal/prefix.js';
import { crockfordDigit, readUlidTimestamp, ULID_LENGTH, ulidBounds } from './internal/ulid.js';
import {
  hexDigit,
  isVariantNibble,
  readUuidV7Timestamp,
  UUID_LENGTH,
  UUID_VARIANT_INDEX,
  UUID_VERSION_INDEX,
  uuidV7Bounds,
} from './internal/uuidv7.js';
import type { EntityType, IdStrategy } from './types.js';

/** The phantom property that tells a checked identifier apart from any other string. */
declare const idBrand: unique symbol;

/**
 * A string that is known to be a well-formed Idora identifier with the prefix `P`.
 *
 * ```ts
 * function loadUser(id: Id<'usr'>) { ... }
 *
 * loadUser(request.params.id);                         // compile error: a plain string
 * if (IdoraId.isValid(request.params.id, 'usr')) {
 *   loadUser(request.params.id);                       // fine: narrowed to Id<'usr'>
 * }
 * loadUser(IdoraId.parse(request.params.id, 'usr').text); // also fine
 * ```
 *
 * It is a plain string at run time — the brand exists only for the type checker — so it costs
 * nothing, serialises as itself and works anywhere a string does. The only ways to get one are the
 * checks that justify it: {@link IdoraId.isValid}, {@link Idora.isIdFor}, and the
 * {@link IdoraId.text} of a parsed identifier. Without a type argument it means "some valid
 * identifier, whatever its prefix".
 */
export type Id<P extends string = string> = `${P}_${string}` & { readonly [idBrand]: P };

/** The largest millisecond count a 48-bit timestamp can hold: 2^48 - 1, in the year 10889. */
const MAX_TIMESTAMP_MS = 2 ** 48 - 1;

/** How much of an untrusted input an error message quotes before eliding the rest. */
const MAX_QUOTED_LENGTH = 64;

/**
 * An identifier read back from text: its prefix, its value, which format the value is in and the
 * moment it was minted.
 *
 * ```ts
 * const id = IdoraId.parse('usr_01k226e0g0008j4ct4ank7f24s');
 *
 * id.prefix;      // 'usr'
 * id.value;       // '01k226e0g0008j4ct4ank7f24s'
 * id.format;      // 'ulid'
 * id.timestampMs; // 1754568000000
 * id.createdAt;   // 2025-08-07T12:00:00.000Z
 * `${id}`;        // 'usr_01k226e0g0008j4ct4ank7f24s'
 * ```
 *
 * Any conforming identifier parses, whichever strategy the local generator uses and whichever Idora
 * package minted it. Parsing is strict: it accepts exactly what a generator emits, and nothing
 * else — see {@link InvalidIdError} for what is rejected and why.
 *
 * Parsed identifiers are immutable values. They compare {@link equals | equal} when their texts
 * are equal, and sort in their **natural order** — by timestamp, then value, then prefix — which
 * puts identifiers of every entity type and both formats in the order they were made:
 *
 * ```ts
 * events.sort(IdoraId.compare);       // oldest first
 * order.id.isAfter(user.id);          // true if the order was minted later
 * id.isCreatedBefore(Date.now() - 86_400_000); // older than a day?
 * ```
 *
 * The relational operators deliberately do not work: `a < b` would compare the texts, which is
 * not the natural order once prefixes or formats differ, so converting an `IdoraId` to a number
 * throws. Use {@link compare}, {@link compareTo}, {@link isBefore} or {@link isAfter}. To key a
 * `Map` or `Set`, use the {@link text}.
 */
export class IdoraId<P extends string = string> {
  /** The whole identifier, exactly as it was parsed. This is its string form. */
  readonly text: Id<P>;

  /** Everything before the first `_`, exactly as written, case included. */
  readonly prefix: P;

  /** Everything after the first `_`: a 26-character ULID or a 32-character UUIDv7. */
  readonly value: string;

  /** Which format {@link value} is in — the same names that select a generator's strategy. */
  readonly format: IdStrategy;

  /** The milliseconds since the Unix epoch that the value begins with. */
  readonly timestampMs: number;

  private constructor(text: string, prefix: string, value: string, format: IdStrategy, timestampMs: number) {
    this.text = text as Id<P>;
    this.prefix = prefix as P;
    this.value = value;
    this.format = format;
    this.timestampMs = timestampMs;
    Object.freeze(this);
  }

  /**
   * Parses an identifier, throwing if it is malformed or — when `expectedPrefix` is given — if it
   * has any other prefix.
   *
   * ```ts
   * IdoraId.parse('usr_01k226e0g0008j4ct4ank7f24s');        // any prefix
   * IdoraId.parse(request.params.id, 'usr');                 // a user's identifier, or throw
   * IdoraId.parse('USR_01k226e0g0008j4ct4ank7f24s', 'usr'); // throws: prefixes match exactly
   * ```
   *
   * For an entity type you registered, {@link Idora.parse} looks the prefix up for you.
   *
   * @param expectedPrefix The prefix the identifier must carry, compared exactly, case included.
   * @throws {InvalidIdError} `text` is not a well-formed identifier, or has the wrong prefix. Its
   * `reason` says which check failed.
   * @throws {InvalidEntityPrefixError} `expectedPrefix` is not itself a valid prefix — a mistake in
   * the calling code, which is reported whatever the text.
   * @throws {TypeError} `text` is not a string.
   */
  static parse<P extends string = string>(text: string, expectedPrefix?: P): IdoraId<P> {
    return IdoraId.read(text, expectedPrefix, undefined, true) as IdoraId<P>;
  }

  /**
   * Parses an identifier, returning `undefined` instead of throwing if it is malformed, has the
   * wrong prefix, or is not a string at all.
   *
   * ```ts
   * const id = IdoraId.tryParse(request.query.after, 'evt');
   * if (id === undefined) {
   *   return response.status(400).send('after must be an event identifier');
   * }
   * ```
   *
   * @param expectedPrefix The prefix the identifier must carry, compared exactly, case included.
   * @throws {InvalidEntityPrefixError} `expectedPrefix` is not itself a valid prefix. That is a
   * mistake in the calling code rather than in the text, so it throws even here.
   */
  static tryParse<P extends string = string>(text: unknown, expectedPrefix?: P): IdoraId<P> | undefined {
    return IdoraId.read(text, expectedPrefix, undefined, false) as IdoraId<P> | undefined;
  }

  /**
   * Whether `text` is a well-formed identifier — with the prefix `expectedPrefix`, when given.
   * Anything that is not a string is simply not valid.
   *
   * In TypeScript this is a type guard, narrowing `text` to {@link Id}:
   *
   * ```ts
   * if (IdoraId.isValid(input, 'usr')) {
   *   input; // Id<'usr'>
   * }
   * ```
   *
   * @throws {InvalidEntityPrefixError} `expectedPrefix` is not itself a valid prefix.
   */
  static isValid<P extends string = string>(text: unknown, expectedPrefix?: P): text is Id<P> {
    return IdoraId.read(text, expectedPrefix, undefined, false) !== undefined;
  }

  /**
   * Compares two identifiers in their natural order: by timestamp, oldest first, then by value,
   * then by prefix. Returns a negative number, zero or a positive number, so it can be handed
   * straight to `Array.prototype.sort`:
   *
   * ```ts
   * ids.sort(IdoraId.compare);
   * ```
   *
   * Ordering by timestamp first is what makes identifiers of different entity types and formats
   * interleave in the order they were minted: an order created after a user sorts after that user,
   * although `ord` < `usr`. For ULIDs from one generator, the natural order is exactly generation
   * order.
   */
  static compare(this: void, a: IdoraId, b: IdoraId): number {
    if (a.timestampMs !== b.timestampMs) {
      return a.timestampMs < b.timestampMs ? -1 : 1;
    }

    return compareOrdinal(a.value, b.value) || compareOrdinal(a.prefix, b.prefix);
  }

  /**
   * The lowest identifier with `prefix` that any generator can mint in the millisecond `time`. With
   * {@link upperBound}, it brackets every identifier minted in a span of time as a plain string
   * range, so a database can find them with an index scan and without parsing a row:
   *
   * ```ts
   * const lower = IdoraId.lowerBound('usr', from);
   * const upper = IdoraId.upperBound('usr', to);
   * // SELECT * FROM users WHERE id >= $1 AND id <= $2   — with lower.text and upper.text
   * ```
   *
   * The bounds compare correctly only against identifiers with the same prefix and format, as
   * strings: the shape of a database column, which holds one entity type in one format. For a
   * registered entity type, {@link Idora.lowerBound} supplies both for you.
   *
   * @param time A `Date`, or milliseconds since the Unix epoch. Any sub-millisecond part is dropped.
   * @param format The format of the column being queried. Defaults to `'ulid'`.
   * @throws {InvalidEntityPrefixError} `prefix` is not a valid prefix.
   * @throws {RangeError} `time` is before the Unix epoch, after the year 10889, or not a valid time.
   * @throws {TypeError} An argument has the wrong type, or `format` is not a strategy.
   */
  static lowerBound<P extends string>(prefix: P, time: Date | number, format: IdStrategy = 'ulid'): IdoraId<P> {
    return IdoraId.#bound(prefix, time, format, 'lower');
  }

  /**
   * The highest identifier with `prefix` that any generator can mint in the millisecond `time`. See
   * {@link lowerBound}; the range from one to the other is inclusive at both ends.
   *
   * @param time A `Date`, or milliseconds since the Unix epoch. Any sub-millisecond part is dropped.
   * @param format The format of the column being queried. Defaults to `'ulid'`.
   * @throws {InvalidEntityPrefixError} `prefix` is not a valid prefix.
   * @throws {RangeError} `time` is before the Unix epoch, after the year 10889, or not a valid time.
   * @throws {TypeError} An argument has the wrong type, or `format` is not a strategy.
   */
  static upperBound<P extends string>(prefix: P, time: Date | number, format: IdStrategy = 'ulid'): IdoraId<P> {
    return IdoraId.#bound(prefix, time, format, 'upper');
  }

  /**
   * The lowest value — no prefix — that any generator can produce in the millisecond `time`, for a
   * column that stores values without their prefix.
   *
   * ```ts
   * IdoraId.lowerBoundValue(0);           // '00000000000000000000000000'
   * IdoraId.lowerBoundValue(0, 'uuidv7'); // '00000000000070008000000000000000'
   * ```
   *
   * @throws {RangeError} `time` is before the Unix epoch, after the year 10889, or not a valid time.
   * @throws {TypeError} An argument has the wrong type, or `format` is not a strategy.
   */
  static lowerBoundValue(time: Date | number, format: IdStrategy = 'ulid'): string {
    return boundsOf(boundTimestamp(time), format).lower;
  }

  /**
   * The highest value — no prefix — that any generator can produce in the millisecond `time`.
   *
   * ```ts
   * IdoraId.upperBoundValue(0);           // '0000000000zzzzzzzzzzzzzzzz'
   * IdoraId.upperBoundValue(0, 'uuidv7'); // '0000000000007fffbfffffffffffffff'
   * ```
   *
   * @throws {RangeError} `time` is before the Unix epoch, after the year 10889, or not a valid time.
   * @throws {TypeError} An argument has the wrong type, or `format` is not a strategy.
   */
  static upperBoundValue(time: Date | number, format: IdStrategy = 'ulid'): string {
    return boundsOf(boundTimestamp(time), format).upper;
  }

  /**
   * The moment the identifier was minted, as a `Date` in UTC. A new `Date` on every read, because
   * a `Date` can be changed and this object cannot.
   */
  get createdAt(): Date {
    return new Date(this.timestampMs);
  }

  /** Whether `other` is an identifier with exactly the same text. */
  equals(other: unknown): boolean {
    return other instanceof IdoraId && other.text === this.text;
  }

  /**
   * Compares this identifier with `other` in their natural order. Negative if this one comes first,
   * positive if it comes second, zero only if both are equal. See {@link IdoraId.compare}.
   */
  compareTo(other: IdoraId): number {
    return IdoraId.compare(this, other);
  }

  /** Whether this identifier comes strictly before `other` in the natural order. */
  isBefore(other: IdoraId): boolean {
    return IdoraId.compare(this, other) < 0;
  }

  /** Whether this identifier comes strictly after `other` in the natural order. */
  isAfter(other: IdoraId): boolean {
    return IdoraId.compare(this, other) > 0;
  }

  /**
   * Compares only the milliseconds the two identifiers were minted in: negative if this one is
   * older, positive if newer, and zero for two minted in the same millisecond — whatever their
   * prefixes, values or formats.
   */
  compareTimestamp(other: IdoraId): number {
    return this.timestampMs === other.timestampMs ? 0 : this.timestampMs < other.timestampMs ? -1 : 1;
  }

  /** Whether the two identifiers were minted in the same millisecond. */
  isSameMillisecond(other: IdoraId): boolean {
    return this.timestampMs === other.timestampMs;
  }

  /**
   * Whether this identifier was minted strictly before `instant`, to the millisecond. A fraction of
   * a millisecond in `instant` is dropped before comparing.
   *
   * @param instant A `Date`, or milliseconds since the Unix epoch.
   * @throws {RangeError} `instant` is not a valid time — an invalid `Date`, `NaN` or an infinity.
   * @throws {TypeError} `instant` is neither a `Date` nor a number.
   */
  isCreatedBefore(instant: Date | number): boolean {
    return this.timestampMs < toMilliseconds(instant, 'instant');
  }

  /**
   * Whether this identifier was minted strictly after `instant`, to the millisecond. A fraction of
   * a millisecond in `instant` is dropped before comparing.
   *
   * @param instant A `Date`, or milliseconds since the Unix epoch.
   * @throws {RangeError} `instant` is not a valid time — an invalid `Date`, `NaN` or an infinity.
   * @throws {TypeError} `instant` is neither a `Date` nor a number.
   */
  isCreatedAfter(instant: Date | number): boolean {
    return this.timestampMs > toMilliseconds(instant, 'instant');
  }

  /**
   * Whether the identifier's prefix is exactly `prefix`, case included. In TypeScript this narrows
   * the identifier's prefix type.
   */
  hasPrefix<Q extends string>(prefix: Q): this is IdoraId<Q> {
    return (this.prefix as string) === prefix;
  }

  /** The identifier's text. */
  toString(): Id<P> {
    return this.text;
  }

  /** The identifier's text, so that `JSON.stringify` writes an identifier as a plain string. */
  toJSON(): Id<P> {
    return this.text;
  }

  /**
   * Converts to the text wherever a string is wanted — a template literal, `+ ''`, a loose `==` —
   * and refuses to become a number, which is what `<` and `>` ask for and which would quietly
   * compare the texts rather than the natural order.
   */
  [Symbol.toPrimitive](hint: string): string {
    if (hint === 'number') {
      throw new TypeError(
        `An IdoraId cannot be converted to a number, so operators such as < and > do not apply to it. Use IdoraId.compare, compareTo, isBefore or isAfter to put identifiers in order, and timestampMs for the time one was minted.`,
      );
    }

    return this.text;
  }

  /**
   * @internal The one implementation behind every parse: the static methods here and the
   * registry-aware ones, which pass the entity type so that a mismatch can name it.
   */
  static read(
    text: unknown,
    expectedPrefix: string | undefined,
    entityType: EntityType | undefined,
    throwOnFailure: boolean,
  ): IdoraId | undefined {
    if (expectedPrefix !== undefined) {
      validatePrefix(expectedPrefix, 'expectedPrefix');
    }

    if (typeof text !== 'string') {
      if (throwOnFailure) {
        throw new TypeError(`An identifier must be a string, but received ${describe(text)}.`);
      }

      return undefined;
    }

    const fail = (reason: InvalidIdReason): undefined => {
      if (throwOnFailure) {
        throw invalidId(text, reason, expectedPrefix, entityType);
      }

      return undefined;
    };

    const separator = text.indexOf(SEPARATOR);
    if (separator < 0) {
      return fail('noSeparator');
    }

    const prefix = text.slice(0, separator);
    if (!isValidPrefix(prefix)) {
      return fail('prefix');
    }

    const value = text.slice(separator + 1);
    let format: IdStrategy;
    let timestampMs: number;
    if (value.length === ULID_LENGTH) {
      format = 'ulid';
      timestampMs = readUlidTimestamp(value);
    } else if (value.length === UUID_LENGTH) {
      format = 'uuidv7';
      timestampMs = readUuidV7Timestamp(value);
    } else {
      return fail('value');
    }

    if (timestampMs < 0) {
      return fail('value');
    }

    if (expectedPrefix !== undefined && prefix !== expectedPrefix) {
      return fail('prefixMismatch');
    }

    return new IdoraId(text, prefix, value, format, timestampMs);
  }

  static #bound<P extends string>(prefix: P, time: Date | number, format: IdStrategy, which: 'lower' | 'upper'): IdoraId<P> {
    validatePrefix(prefix, 'prefix');
    const timestampMs = boundTimestamp(time);
    const value = boundsOf(timestampMs, format)[which];
    return new IdoraId(prefix + SEPARATOR + value, prefix, value, format, timestampMs);
  }
}

/**
 * Compares two identifier strings in their natural order — by timestamp, then value, then prefix —
 * for sorting text without parsing it yourself:
 *
 * ```ts
 * ['ord_01k226e0g1…', 'usr_01hf7yat00…'].sort(compareIds); // the user first: it is older
 * ```
 *
 * Both are parsed on every call. To sort many identifiers, or sort them more than once, parse them
 * once with {@link IdoraId.parse} and sort with {@link IdoraId.compare}.
 *
 * @throws {InvalidIdError} Either string is not a well-formed identifier.
 * @throws {TypeError} Either argument is not a string.
 */
export function compareIds(this: void, a: string, b: string): number {
  return IdoraId.compare(IdoraId.parse(a), IdoraId.parse(b));
}

/** Compares UTF-16 code units, like .NET's StringComparer.Ordinal, independent of locale. */
function compareOrdinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Reads a `Date` or a millisecond count, dropping any fraction of a millisecond. */
function toMilliseconds(instant: Date | number, parameterName: string): number {
  const ms: unknown = instant instanceof Date ? instant.getTime() : instant;
  if (typeof ms !== 'number') {
    throw new TypeError(
      `${parameterName} must be a Date or a number of milliseconds since the Unix epoch, but received ${describe(instant)}.`,
    );
  }

  if (!Number.isFinite(ms)) {
    throw new RangeError(
      `${parameterName} is ${instant instanceof Date ? 'an invalid Date' : String(ms)}, which is not a point in time. Pass a valid Date, or a finite number of milliseconds since the Unix epoch.`,
    );
  }

  return Math.floor(ms);
}

/** {@link toMilliseconds}, additionally refusing a time no 48-bit timestamp can carry. */
function boundTimestamp(time: Date | number): number {
  const timestampMs = toMilliseconds(time, 'time');
  if (timestampMs < 0 || timestampMs > MAX_TIMESTAMP_MS) {
    throw new RangeError(
      `time is ${timestampMs} milliseconds since the Unix epoch, which an identifier cannot carry. Identifiers hold a 48-bit timestamp, from 0 (1970-01-01T00:00:00.000Z) to ${MAX_TIMESTAMP_MS} (in the year 10889).`,
    );
  }

  return timestampMs;
}

function boundsOf(timestampMs: number, format: IdStrategy): { readonly lower: string; readonly upper: string } {
  switch (format) {
    case 'ulid':
      return ulidBounds(timestampMs);
    case 'uuidv7':
      return uuidV7Bounds(timestampMs);
    default:
      throw new TypeError(`'${String(format)}' is not an identifier format. Use 'ulid' (the default) or 'uuidv7'.`);
  }
}

/** Builds the error for a failed parse, working out the details only now that they are needed. */
function invalidId(
  text: string,
  reason: InvalidIdReason,
  expectedPrefix: string | undefined,
  entityType: EntityType | undefined,
): InvalidIdError {
  const quoted = quote(text);
  const separator = text.indexOf(SEPARATOR);

  switch (reason) {
    case 'noSeparator':
      return new InvalidIdError(
        `${quoted} is not an Idora identifier: it has no '${SEPARATOR}' separating a prefix from a value. An identifier looks like usr_01k2f9v8ta7m3x0qbrn4dzeh5c.`,
        text,
        reason,
        expectedPrefix,
        entityType,
      );

    case 'prefix': {
      const cause = prefixError(text.slice(0, separator));
      return new InvalidIdError(
        `${quoted} is not a valid Idora identifier, because its prefix is invalid. ${cause.message}`,
        text,
        reason,
        expectedPrefix,
        entityType,
        cause,
      );
    }

    case 'value':
      return new InvalidIdError(
        `${quoted} is not a valid Idora identifier: ${describeValueFault(text.slice(separator + 1))}.`,
        text,
        reason,
        expectedPrefix,
        entityType,
      );

    case 'prefixMismatch': {
      const expectation =
        entityType === undefined
          ? `the prefix '${expectedPrefix}' was expected`
          : `an identifier for '${nameOf(entityType)}' has the prefix '${expectedPrefix}'`;
      return new InvalidIdError(
        `${quoted} has the prefix '${text.slice(0, separator)}', but ${expectation}. Prefixes are compared exactly, case included.`,
        text,
        reason,
        expectedPrefix,
        entityType,
      );
    }
  }
}

/** The error {@link validatePrefix} raises for a prefix already known to be invalid. */
function prefixError(prefix: string): InvalidEntityPrefixError {
  try {
    validatePrefix(prefix, 'prefix');
  } catch (error) {
    if (error instanceof InvalidEntityPrefixError) {
      return error;
    }

    throw error;
  }

  throw new Error(`Idora internal error: the prefix '${prefix}' was rejected but passes validation.`);
}

/**
 * Says what is wrong with a value that failed to parse, in terms of the format its length implies.
 * Off the hot path: it runs only when an error is being built.
 */
function describeValueFault(value: string): string {
  if (value.length === 0) {
    return `there is nothing after the '${SEPARATOR}', where a ULID or UUIDv7 value belongs`;
  }

  if (value.length === ULID_LENGTH) {
    for (let i = 0; i < value.length; i++) {
      const code = value.charCodeAt(i);
      if (crockfordDigit(code) >= 0) {
        continue;
      }

      if (code >= 0x41 && code <= 0x5a && crockfordDigit(code + 0x20) >= 0) {
        return `its ULID value contains the uppercase '${value[i]}' at index ${i}, but values are written in lowercase`;
      }

      return `its ULID value contains '${characterAt(value, i)}' at index ${i}, which is not a lowercase Crockford Base32 character (0-9 and a-z, less i, l, o and u)`;
    }

    return `its ULID value begins with '${value[0]}', but the first character of a ULID is never greater than 7`;
  }

  if (value.length === UUID_LENGTH) {
    for (let i = 0; i < value.length; i++) {
      const code = value.charCodeAt(i);
      if (hexDigit(code) >= 0) {
        continue;
      }

      if (code >= 0x41 && code <= 0x46) {
        return `its UUIDv7 value contains the uppercase '${value[i]}' at index ${i}, but values are written in lowercase`;
      }

      return `its UUIDv7 value contains '${characterAt(value, i)}' at index ${i}, which is not a lowercase hexadecimal digit`;
    }

    if (value.charCodeAt(UUID_VERSION_INDEX) !== 0x37) {
      return `its value is not a version 7 UUID: character ${UUID_VERSION_INDEX} is '${value[UUID_VERSION_INDEX]}', where a UUIDv7 has '7'`;
    }

    if (!isVariantNibble(value.charCodeAt(UUID_VARIANT_INDEX))) {
      return `its value does not carry the RFC 9562 variant: character ${UUID_VARIANT_INDEX} is '${value[UUID_VARIANT_INDEX]}', where a UUIDv7 has 8, 9, a or b`;
    }
  }

  if (value.length === UUID_LENGTH + 4 && value[8] === '-' && value[13] === '-' && value[18] === '-' && value[23] === '-') {
    return `its value is a UUID written with hyphens, but Idora writes UUIDv7 values as 32 characters without them`;
  }

  return `its value is ${value.length} characters long, but a ULID value is ${ULID_LENGTH} characters and a UUIDv7 value ${UUID_LENGTH}`;
}

/** The whole code point at `index`, so an astral character reads as itself rather than half of one. */
function characterAt(text: string, index: number): string {
  return String.fromCodePoint(text.codePointAt(index) ?? text.charCodeAt(index));
}

/** Quotes untrusted text for a message, cutting anything long short. */
function quote(text: string): string {
  return text.length <= MAX_QUOTED_LENGTH ? `'${text}'` : `'${text.slice(0, MAX_QUOTED_LENGTH - 1)}…'`;
}
