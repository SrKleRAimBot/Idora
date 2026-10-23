import { InvalidEntityPrefixError } from '../errors.js';

/** The character that separates the prefix from the value. */
export const SEPARATOR = '_';

/**
 * The longest prefix allowed. Long enough for a readable abbreviation, short enough that the prefix
 * never dominates the identifier.
 */
export const MAX_PREFIX_LENGTH = 16;

/**
 * Throws unless `prefix` is a valid entity prefix: 1 to 16 ASCII letters or digits.
 *
 * A single pass over the character codes, with no regular expression and nothing allocated on the
 * success path, because this runs on every call to `generateWithPrefix` as well as at start-up.
 *
 * @throws {TypeError} `prefix` is not a string — the JavaScript equivalent of a null argument.
 * @throws {InvalidEntityPrefixError} `prefix` is empty, too long, or contains anything other than
 * an ASCII letter or digit.
 */
export function validatePrefix(prefix: unknown, parameterName: string): asserts prefix is string {
  if (typeof prefix !== 'string') {
    throw new TypeError(
      `${parameterName} must be a string of 1 to ${MAX_PREFIX_LENGTH} ASCII letters or digits, but received ${describe(prefix)}.`,
    );
  }

  if (prefix.length === 0) {
    throw new InvalidEntityPrefixError(
      `An entity prefix cannot be empty. Supply 1 to ${MAX_PREFIX_LENGTH} ASCII letters or digits, for example "usr".`,
      prefix,
      'empty',
    );
  }

  if (prefix.length > MAX_PREFIX_LENGTH) {
    throw new InvalidEntityPrefixError(
      `The entity prefix '${prefix}' is ${prefix.length} characters long; the maximum is ${MAX_PREFIX_LENGTH}.`,
      prefix,
      'tooLong',
    );
  }

  for (let i = 0; i < prefix.length; i++) {
    const code = prefix.charCodeAt(i);
    if (isAsciiLetterOrDigit(code)) {
      continue;
    }

    // Report the whole code point, so an astral character reads as itself rather than half a
    // surrogate pair. The index stays a UTF-16 index, matching String.prototype semantics.
    const character = String.fromCodePoint(prefix.codePointAt(i) ?? code);
    if (character === SEPARATOR) {
      throw new InvalidEntityPrefixError(
        `The entity prefix '${prefix}' contains the separator '${SEPARATOR}' at index ${i}. The separator divides the prefix from the value, so a prefix cannot contain one.`,
        prefix,
        'separator',
        i,
      );
    }

    throw new InvalidEntityPrefixError(
      `The entity prefix '${prefix}' contains the character '${character}' at index ${i}. Prefixes may contain only ASCII letters and digits.`,
      prefix,
      'character',
      i,
    );
  }
}

/**
 * Folds ASCII letters to lowercase and leaves everything else alone — which, for a validated
 * prefix, is everything else. Used to detect collisions between prefixes that differ only in case.
 *
 * `String.prototype.toLowerCase` would do for a validated prefix, but is locale- and Unicode-aware;
 * spelling out the ASCII rule keeps the comparison identical to every other Idora package.
 */
export function foldPrefix(prefix: string): string {
  let folded = '';
  for (let i = 0; i < prefix.length; i++) {
    const code = prefix.charCodeAt(i);
    folded += String.fromCharCode(code >= 0x41 && code <= 0x5a ? code + 0x20 : code);
  }

  return folded;
}

function isAsciiLetterOrDigit(code: number): boolean {
  return (
    (code >= 0x30 && code <= 0x39) || // 0-9
    (code >= 0x41 && code <= 0x5a) || // A-Z
    (code >= 0x61 && code <= 0x7a) //    a-z
  );
}

function describe(value: unknown): string {
  if (value === null) {
    return 'null';
  }

  return typeof value === 'object' || typeof value === 'function' ? `a ${typeof value}` : `${typeof value} ${String(value)}`;
}
