import { type EntropyPool, sharedEntropy } from './entropy.js';

/** Number of characters in a UUID written without hyphens. */
export const UUID_LENGTH = 32;

/** Number of random bytes a UUIDv7 consumes: octets 6 to 15. */
export const UUID_RANDOM_BYTES = 10;

/** The character codes of the sixteen lowercase hexadecimal digits. */
const HEX_CODES = Uint8Array.from('0123456789abcdef', (character) => character.charCodeAt(0));

/** 2^32, for splitting the 48-bit timestamp into words JavaScript's bitwise operators can handle. */
const TWO_TO_THE_32 = 0x1_0000_0000;

/**
 * Lays out a version 7 UUID (RFC 9562) as 32 lowercase hexadecimal characters with no hyphens.
 *
 * | Octets | Content                                  |
 * | ------ | ---------------------------------------- |
 * | 0–5    | 48-bit Unix millisecond timestamp, big-endian, so byte order is sort order |
 * | 6      | version 7 in the high nibble, random low nibble |
 * | 7      | random                                   |
 * | 8      | variant `10` in the top two bits, random below |
 * | 9–15   | random                                   |
 *
 * `random` supplies octets 6 to 15, starting at `offset`; the version and variant bits are written
 * over the top, leaving 74 random bits.
 *
 * The hyphens are dropped because the identifier already has structure of its own — the prefix
 * and its separator — and a second set of delimiters only makes it longer.
 */
export function composeUuidV7(timestampMs: number, random: Uint8Array, offset = 0): string {
  const high = Math.floor(timestampMs / TWO_TO_THE_32); // top 16 bits of the timestamp
  const low = timestampMs % TWO_TO_THE_32; //              bottom 32 bits
  const h = HEX_CODES;
  const r = offset;

  const o6 = (random[r]! & 0x0f) | 0x70;
  const o7 = random[r + 1]!;
  const o8 = (random[r + 2]! & 0x3f) | 0x80;
  const o9 = random[r + 3]!;
  const o10 = random[r + 4]!;
  const o11 = random[r + 5]!;
  const o12 = random[r + 6]!;
  const o13 = random[r + 7]!;
  const o14 = random[r + 8]!;
  const o15 = random[r + 9]!;

  // One call with every character as an argument builds a flat string directly, where sixteen
  // two-character concatenations would build a rope and cost more than twice as much.
  return String.fromCharCode(
    h[(high >>> 12) & 0xf]!, h[(high >>> 8) & 0xf]!, h[(high >>> 4) & 0xf]!, h[high & 0xf]!,
    h[(low >>> 28) & 0xf]!, h[(low >>> 24) & 0xf]!, h[(low >>> 20) & 0xf]!, h[(low >>> 16) & 0xf]!,
    h[(low >>> 12) & 0xf]!, h[(low >>> 8) & 0xf]!, h[(low >>> 4) & 0xf]!, h[low & 0xf]!,
    h[o6 >>> 4]!, h[o6 & 0xf]!, h[o7 >>> 4]!, h[o7 & 0xf]!,
    h[o8 >>> 4]!, h[o8 & 0xf]!, h[o9 >>> 4]!, h[o9 & 0xf]!,
    h[o10 >>> 4]!, h[o10 & 0xf]!, h[o11 >>> 4]!, h[o11 & 0xf]!,
    h[o12 >>> 4]!, h[o12 & 0xf]!, h[o13 >>> 4]!, h[o13 & 0xf]!,
    h[o14 >>> 4]!, h[o14 & 0xf]!, h[o15 >>> 4]!, h[o15 & 0xf]!,
  );
}

/**
 * Creates a version 7 UUID stamped with `timestampMs`, drawing its randomness from the buffered
 * pool.
 *
 * `crypto.randomUUID()` produces version 4, which is random throughout and therefore scatters
 * inserts across an index; there is no built-in version 7. Only the millisecond of a UUIDv7 is
 * ordered: two made in the same millisecond are in no particular order relative to each other.
 */
export function createUuidV7(timestampMs: number, entropy: EntropyPool = sharedEntropy): string {
  return composeUuidV7(timestampMs, entropy.block, entropy.reserve(UUID_RANDOM_BYTES));
}
