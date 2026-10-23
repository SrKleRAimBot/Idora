/**
 * Decodes a ULID back into its parts.
 *
 * Written independently of the encoder under test, and structured differently from it — BigInt
 * arithmetic over the whole string rather than byte unpacking — so that a mistake in the encoder
 * cannot be mirrored here and cancel itself out.
 */
const ALPHABET = '0123456789abcdefghjkmnpqrstvwxyz';

export function decodeUlid(ulid: string): bigint {
  let value = 0n;
  for (const character of ulid) {
    const digit = ALPHABET.indexOf(character);
    if (digit < 0) {
      throw new Error(`'${character}' is not a Crockford Base32 character.`);
    }

    value = (value << 5n) | BigInt(digit);
  }

  return value;
}

/** The 48-bit millisecond timestamp in the first ten characters. */
export function decodeTimestamp(ulid: string): number {
  return Number(decodeUlid(ulid.slice(0, 10)));
}

/** The 80-bit random field in the last sixteen characters. */
export function decodeRandom(ulid: string): bigint {
  return decodeUlid(ulid.slice(10));
}

/** Splits an identifier on its first underscore. */
export function split(id: string): { prefix: string; value: string } {
  const separator = id.indexOf('_');
  return { prefix: id.slice(0, separator), value: id.slice(separator + 1) };
}
