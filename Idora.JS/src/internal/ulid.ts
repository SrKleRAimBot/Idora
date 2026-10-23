import { type EntropyPool, sharedEntropy } from './entropy.js';

/**
 * The lowercase Crockford Base32 alphabet: digits and letters, less `i l o u`.
 *
 * Crockford omits `i`, `l` and `o` because they are easily mistaken for `1` and `0` when an
 * identifier is read aloud or copied by hand, and `u` so the encoding cannot spell obscenities by
 * accident. The canonical form is uppercase; Idora emits lowercase throughout because it reads
 * better in URLs and logs, and since no two characters in the alphabet differ only by case,
 * lowercasing loses nothing.
 */
export const CROCKFORD_ALPHABET = '0123456789abcdefghjkmnpqrstvwxyz';

/** Number of characters a ULID occupies when encoded. */
export const ULID_LENGTH = 26;

/** Number of leading characters that encode the 48-bit timestamp. */
export const ULID_TIMESTAMP_LENGTH = 10;

/** Number of random bytes a ULID consumes: 80 bits. */
export const ULID_RANDOM_BYTES = 10;

/** Number of base-32 digits in the 80-bit random field. */
const RANDOM_DIGITS = 16;

/** The alphabet as character codes, so a digit becomes a character with one array read. */
const ALPHABET_CODES = Uint8Array.from(CROCKFORD_ALPHABET, (character) => character.charCodeAt(0));

/**
 * Encodes a 48-bit millisecond timestamp as the 10 characters that begin a ULID.
 *
 * Ten characters hold 50 bits, so the first never exceeds `7`.
 */
export function encodeTimestamp(timestampMs: number): string {
  // 48 bits do not fit JavaScript's 32-bit bitwise operators, but they fit a double exactly, so
  // peel off five bits at a time arithmetically.
  let remaining = timestampMs;
  const codes = new Array<number>(ULID_TIMESTAMP_LENGTH);
  for (let i = ULID_TIMESTAMP_LENGTH - 1; i >= 0; i--) {
    codes[i] = ALPHABET_CODES[remaining % 32]!;
    remaining = Math.floor(remaining / 32);
  }

  return String.fromCharCode(...codes);
}

/**
 * Splits 80 random bits — ten bytes, big-endian, starting at `offset` — into sixteen 5-bit digits.
 *
 * Done as two 40-bit groups of five bytes, the textbook Base32 unpacking, so that everything stays
 * within 32-bit integer arithmetic.
 */
export function unpackRandom(bytes: Uint8Array, offset: number, digits: Uint8Array): void {
  for (let group = 0; group < 2; group++) {
    const b = offset + group * 5;
    const d = group * 8;
    const b0 = bytes[b]!;
    const b1 = bytes[b + 1]!;
    const b2 = bytes[b + 2]!;
    const b3 = bytes[b + 3]!;
    const b4 = bytes[b + 4]!;

    digits[d] = b0 >> 3;
    digits[d + 1] = ((b0 & 0x07) << 2) | (b1 >> 6);
    digits[d + 2] = (b1 >> 1) & 0x1f;
    digits[d + 3] = ((b1 & 0x01) << 4) | (b2 >> 4);
    digits[d + 4] = ((b2 & 0x0f) << 1) | (b3 >> 7);
    digits[d + 5] = (b3 >> 2) & 0x1f;
    digits[d + 6] = ((b3 & 0x03) << 3) | (b4 >> 5);
    digits[d + 7] = b4 & 0x1f;
  }
}

/** Writes the sixteen random digits out as characters, in one flat string. */
function encodeRandom(digits: Uint8Array): string {
  const a = ALPHABET_CODES;
  // Sixteen explicit arguments rather than a spread: V8 builds a flat string from this directly.
  return String.fromCharCode(
    a[digits[0]!]!, a[digits[1]!]!, a[digits[2]!]!, a[digits[3]!]!,
    a[digits[4]!]!, a[digits[5]!]!, a[digits[6]!]!, a[digits[7]!]!,
    a[digits[8]!]!, a[digits[9]!]!, a[digits[10]!]!, a[digits[11]!]!,
    a[digits[12]!]!, a[digits[13]!]!, a[digits[14]!]!, a[digits[15]!]!,
  );
}

/**
 * Encodes a whole ULID from a timestamp and ten random bytes. The building block the monotonic
 * stream is made of, exposed separately so the encoding can be checked against known vectors.
 */
export function encodeUlid(timestampMs: number, random: Uint8Array): string {
  const digits = new Uint8Array(RANDOM_DIGITS);
  unpackRandom(random, 0, digits);
  return encodeTimestamp(timestampMs) + encodeRandom(digits);
}

/**
 * A stream of strictly increasing ULIDs: the millisecond the last one was stamped with, and the
 * 80-bit random field that went with it.
 *
 * The clock reading is a parameter to {@link next} rather than something the stream reads itself,
 * which keeps the awkward cases — two identifiers in one millisecond, a clock that steps backwards,
 * an exhausted counter — deterministic and directly testable.
 */
export class UlidStream {
  /** Milliseconds since the Unix epoch of the previous identifier; -1 before the first. */
  #lastTimestampMs = -1;

  /** The previous identifier's random field, as sixteen 5-bit digits, most significant first. */
  readonly #digits = new Uint8Array(RANDOM_DIGITS);

  /** {@link #lastTimestampMs} already encoded, because consecutive identifiers usually share it. */
  #encodedTimestamp = '';

  readonly #entropy: EntropyPool;

  constructor(entropy: EntropyPool = sharedEntropy) {
    this.#entropy = entropy;
  }

  /** The millisecond the most recent identifier was stamped with. */
  get lastTimestampMs(): number {
    return this.#lastTimestampMs;
  }

  /**
   * Advances the stream and returns the next 26-character ULID.
   *
   * - A later millisecond than the last: stamp it and draw a fresh random field.
   * - The same millisecond, or an earlier one because the clock stepped backwards: keep the last
   *   millisecond and add one to the random field, per the ULID spec's monotonic mode. The
   *   sequence therefore never regresses; identifiers made while the clock is behind carry a
   *   timestamp a little ahead of it, which is the lesser evil.
   * - The random field already at its maximum: borrow the next millisecond and reseed, rather than
   *   wrapping or stalling. That takes 2^80 identifiers in one millisecond, so it is theoretical.
   */
  next(nowMs: number): string {
    if (nowMs > this.#lastTimestampMs) {
      this.#reseed(nowMs);
    } else if (!this.#increment()) {
      this.#reseed(this.#lastTimestampMs + 1);
    }

    return this.#encodedTimestamp + encodeRandom(this.#digits);
  }

  /**
   * Replaces the stream's state outright. A test seam, for starting from a known random field;
   * `random` is ten big-endian bytes.
   */
  restore(lastTimestampMs: number, random: Uint8Array): void {
    this.#lastTimestampMs = lastTimestampMs;
    this.#encodedTimestamp = encodeTimestamp(lastTimestampMs);
    unpackRandom(random, 0, this.#digits);
  }

  #reseed(timestampMs: number): void {
    this.#lastTimestampMs = timestampMs;
    this.#encodedTimestamp = encodeTimestamp(timestampMs);
    unpackRandom(this.#entropy.block, this.#entropy.reserve(ULID_RANDOM_BYTES), this.#digits);
  }

  /** Adds one to the 80-bit field, carrying leftwards; false if it was already all ones. */
  #increment(): boolean {
    const digits = this.#digits;
    for (let i = RANDOM_DIGITS - 1; i >= 0; i--) {
      if (digits[i]! < 0x1f) {
        digits[i]!++;
        return true;
      }

      digits[i] = 0;
    }

    return false;
  }
}
