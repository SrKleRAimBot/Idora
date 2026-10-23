import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { Idora, IdoraError, InvalidEntityPrefixError } from '../src/index.js';
import { User } from './support/entities.js';

/** Prefixes that must be rejected, and why. */
const INVALID: ReadonlyArray<readonly [prefix: string, reason: string]> = [
  ['', 'empty'],
  [' ', 'character'], // whitespace is not a letter or digit
  ['us r', 'character'], // embedded space
  ['us_r', 'separator'],
  ['_usr', 'separator'],
  ['usr_', 'separator'],
  ['usr-1', 'character'],
  ['usr.1', 'character'],
  ['user!', 'character'],
  ['usér', 'character'], // non-ASCII letter
  ['ユーザ', 'character'], // non-Latin script
  ['\u{1F600}', 'character'], // emoji, an astral code point
  ['usr\n', 'character'], // control character
  ['١٢٣', 'character'], // Arabic-Indic digits are digits, but not ASCII ones
  ['12345678901234567', 'tooLong'], // 17 characters, one over the limit
  ['a'.repeat(32), 'tooLong'],
];

/** Prefixes that must be accepted. */
const VALID = ['a', '1', 'usr', 'usr1', 'USR', 'UsR', '0123456789', 'abcdefghijklmnop', 'ABCDEFGHIJKLMNOP'];

describe('prefix validation', () => {
  for (const [prefix, reason] of INVALID) {
    it(`rejects ${JSON.stringify(prefix)} at registration (${reason})`, () => {
      // Arrange & Act & Assert
      assert.throws(
        () => new Idora({ entities: [[User, prefix]] }),
        (error: unknown) => error instanceof InvalidEntityPrefixError && error.reason === reason && error.prefix === prefix,
      );
    });

    it(`rejects ${JSON.stringify(prefix)} on every generateWithPrefix call (${reason})`, () => {
      // Arrange
      const ids = new Idora();

      // Act & Assert
      assert.throws(
        () => ids.generateWithPrefix(prefix),
        (error: unknown) => error instanceof InvalidEntityPrefixError && error.reason === reason,
      );
    });
  }

  for (const prefix of VALID) {
    it(`accepts ${JSON.stringify(prefix)}`, () => {
      // Arrange
      const ids = new Idora({ entities: [[User, prefix]] });

      // Act & Assert
      assert.ok(ids.generate(User).startsWith(`${prefix}_`));
      assert.ok(ids.generateWithPrefix(prefix).startsWith(`${prefix}_`));
    });
  }

  it('explains the separator specifically, with its index', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix('user_account'));

    // Assert
    assert.equal(error.index, 4);
    assert.match(error.message, /contains the separator '_' at index 4/);
    assert.match(error.message, /divides the prefix from the value/);
  });

  it('names the offending character and its index', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix('usr!'));

    // Assert
    assert.equal(error.index, 3);
    assert.match(error.message, /contains the character '!' at index 3/);
  });

  it('reports an astral character whole rather than as half a surrogate pair', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix('a\u{1F600}'));

    // Assert
    assert.equal(error.index, 1);
    assert.ok(error.message.includes("'\u{1F600}'"));
  });

  it('states the limit when a prefix is too long', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix('12345678901234567'));

    // Assert
    assert.equal(error.index, undefined);
    assert.match(error.message, /is 17 characters long; the maximum is 16/);
  });

  it('suggests an example when the prefix is empty', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix(''));

    // Assert
    assert.match(error.message, /cannot be empty.*for example "usr"/);
  });

  it('derives from IdoraError, so callers can catch every Idora failure at once', () => {
    // Arrange & Act
    const error = captureInvalid(() => new Idora().generateWithPrefix(''));

    // Assert
    assert.ok(error instanceof IdoraError);
    assert.ok(error instanceof Error);
    assert.equal(error.name, 'InvalidEntityPrefixError');
  });

  for (const notAString of [undefined, null, 42, {}, Symbol('usr')]) {
    it(`throws a TypeError, not an IdoraError, for a prefix of ${typeof notAString === 'symbol' ? 'symbol' : JSON.stringify(notAString) ?? 'undefined'}`, () => {
      // Arrange
      const ids = new Idora();

      // Act & Assert
      assert.throws(() => ids.generateWithPrefix(notAString as never), TypeError);
      assert.throws(() => new Idora({ entities: [[User, notAString as never]] }), TypeError);
    });
  }
});

function captureInvalid(action: () => unknown): InvalidEntityPrefixError {
  try {
    action();
  } catch (error) {
    assert.ok(error instanceof InvalidEntityPrefixError);
    return error;
  }

  assert.fail('expected an InvalidEntityPrefixError');
}
