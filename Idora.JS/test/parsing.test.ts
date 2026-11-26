import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  EntityPrefixNotRegisteredError,
  type Id,
  Idora,
  IdoraError,
  IdoraId,
  InvalidEntityPrefixError,
  InvalidIdError,
} from '../src/index.js';
import { Order, UnregisteredEntity, User } from './support/entities.js';

const USER_ULID = 'usr_01k226e0g0008j4ct4ank7f24s';
const INVOICE_UUID = 'inv_0198846702007011a233445566778899';

describe('IdoraId.parse', () => {
  it('reads every part of a ULID identifier', () => {
    // Act
    const id = IdoraId.parse(USER_ULID);

    // Assert
    assert.equal(id.text, USER_ULID);
    assert.equal(id.prefix, 'usr');
    assert.equal(id.value, '01k226e0g0008j4ct4ank7f24s');
    assert.equal(id.format, 'ulid');
    assert.equal(id.timestampMs, 1754568000000);
    assert.equal(id.createdAt.toISOString(), '2025-08-07T12:00:00.000Z');
  });

  it('reads every part of a UUIDv7 identifier', () => {
    // Act
    const id = IdoraId.parse(INVOICE_UUID);

    // Assert
    assert.equal(id.prefix, 'inv');
    assert.equal(id.value, '0198846702007011a233445566778899');
    assert.equal(id.format, 'uuidv7');
    assert.equal(id.timestampMs, 1754568000000);
  });

  it('reads back what every generator mints, in both formats and for every prefix shape', () => {
    // Arrange
    const before = Date.now();
    const generated = (['ulid', 'uuidv7'] as const).flatMap((strategy) => {
      const ids = new Idora({ strategy, entities: [[User, 'usr']] });
      return [ids.generate(User), ids.generateWithPrefix('A1b2C3d4E5f6G7h8'), ids.generateWithPrefix('x')].map(
        (text) => [strategy, text] as const,
      );
    });
    const after = Date.now();

    for (const [strategy, text] of generated) {
      // Act
      const id = IdoraId.parse(text);

      // Assert
      assert.equal(id.format, strategy);
      assert.equal(id.text, text);
      assert.ok(id.timestampMs >= before && id.timestampMs <= after, `${text} carries ${id.timestampMs}`);
    }
  });

  it('splits on the first underscore, so a later one is part of the value and rejected there', () => {
    // Act & Assert
    assert.equal(captureInvalid(() => IdoraId.parse(`${USER_ULID}_x`)).reason, 'value');
  });

  it('accepts an identifier with the expected prefix and types the prefix as that literal', () => {
    // Act
    const id = IdoraId.parse(USER_ULID, 'usr');

    // Assert
    const prefix: 'usr' = id.prefix;
    const text: Id<'usr'> = id.text;
    assert.equal(prefix, 'usr');
    assert.equal(text, USER_ULID);
  });

  it('compares the expected prefix exactly, case included', () => {
    // Act
    const error = captureInvalid(() => IdoraId.parse('USR_01k226e0g0008j4ct4ank7f24s', 'usr'));

    // Assert
    assert.equal(error.reason, 'prefixMismatch');
    assert.equal(error.expectedPrefix, 'usr');
    assert.match(error.message, /has the prefix 'USR', but the prefix 'usr' was expected/);
    assert.match(error.message, /compared exactly, case included/);
  });

  it('reports a malformed identifier as malformed even when a prefix was expected', () => {
    // Act & Assert
    assert.equal(captureInvalid(() => IdoraId.parse('usr_bad', 'ord')).reason, 'value');
    assert.equal(captureInvalid(() => IdoraId.parse('nope', 'usr')).reason, 'noSeparator');
  });

  it('refuses an invalid expected prefix whatever the text, because the mistake is in the calling code', () => {
    // Act & Assert
    for (const expectedPrefix of ['usr_', '', 'u-r', 'abcdefghijklmnopq']) {
      assert.throws(() => IdoraId.parse(USER_ULID, expectedPrefix), InvalidEntityPrefixError);
      assert.throws(() => IdoraId.parse('nope', expectedPrefix), InvalidEntityPrefixError);
      assert.throws(() => IdoraId.tryParse(USER_ULID, expectedPrefix), InvalidEntityPrefixError);
      assert.throws(() => IdoraId.isValid(USER_ULID, expectedPrefix), InvalidEntityPrefixError);
    }
  });

  for (const notAString of [undefined, null, 42, {}, ['usr_01k226e0g0008j4ct4ank7f24s']]) {
    it(`throws a TypeError, not an InvalidIdError, for ${JSON.stringify(notAString) ?? 'undefined'}`, () => {
      // Act & Assert
      assert.throws(
        () => IdoraId.parse(notAString as never),
        (error: unknown) => error instanceof TypeError && /An identifier must be a string/.test(error.message),
      );
    });
  }

  it('returns a frozen object', () => {
    // Act
    const id = IdoraId.parse(USER_ULID);

    // Assert
    assert.ok(Object.isFrozen(id));
    assert.throws(() => {
      (id as { prefix: string }).prefix = 'ord';
    }, TypeError);
  });

  it('hands out a fresh Date each time, so changing one does not change the identifier', () => {
    // Arrange
    const id = IdoraId.parse(USER_ULID);

    // Act
    id.createdAt.setFullYear(2000);

    // Assert
    assert.equal(id.createdAt.getTime(), 1754568000000);
    assert.notEqual(id.createdAt, id.createdAt);
  });

  it('cannot be constructed directly', () => {
    // Act & Assert: the constructor is private, so this is a compile error as well.
    // @ts-expect-error -- IdoraId has no public constructor; use parse.
    assert.ok(typeof (() => new IdoraId()) === 'function');
  });
});

describe('IdoraId.tryParse and IdoraId.isValid', () => {
  it('return the identifier, or true, for a valid identifier', () => {
    // Act & Assert
    assert.equal(IdoraId.tryParse(USER_ULID)?.text, USER_ULID);
    assert.equal(IdoraId.tryParse(USER_ULID, 'usr')?.prefix, 'usr');
    assert.equal(IdoraId.isValid(USER_ULID), true);
    assert.equal(IdoraId.isValid(USER_ULID, 'usr'), true);
  });

  it('return undefined, or false, for a malformed identifier or the wrong prefix', () => {
    // Act & Assert
    for (const [text, expectedPrefix] of [['nope', undefined], [USER_ULID, 'ord'], ['usr_01K226E0G0008J4CT4ANK7F24S', undefined]] as const) {
      assert.equal(IdoraId.tryParse(text, expectedPrefix), undefined);
      assert.equal(IdoraId.isValid(text, expectedPrefix), false);
    }
  });

  for (const notAString of [undefined, null, 42, {}, new String(USER_ULID)]) {
    it(`treat ${typeof notAString === 'object' && notAString !== null ? `a ${notAString.constructor.name}` : String(notAString)} as not an identifier, without throwing`, () => {
      // Act & Assert
      assert.equal(IdoraId.tryParse(notAString), undefined);
      assert.equal(IdoraId.isValid(notAString), false);
    });
  }

  it('narrows an unknown input to Id, carrying the expected prefix', () => {
    // Arrange
    const input: unknown = USER_ULID;

    // Act & Assert
    if (!IdoraId.isValid(input, 'usr')) {
      assert.fail('expected a valid identifier');
    }

    const narrowed: Id<'usr'> = input;
    const asString: string = narrowed;
    assert.equal(asString, USER_ULID);
  });

  it('keeps plain strings and other prefixes out of a branded Id at compile time', () => {
    // Arrange
    const user = IdoraId.parse(USER_ULID, 'usr').text;

    // Act & Assert
    // @ts-expect-error -- a plain string has not been checked.
    const unchecked: Id<'usr'> = USER_ULID;
    // @ts-expect-error -- an identifier for a user is not one for an order.
    const wrongEntity: Id<'ord'> = user;
    const anyPrefix: Id = user;
    assert.equal(unchecked, anyPrefix);
    assert.equal(wrongEntity, anyPrefix);
  });
});

describe('InvalidIdError', () => {
  it('is an IdoraError, so callers can catch every Idora failure at once', () => {
    // Act
    const error = captureInvalid(() => IdoraId.parse('nope'));

    // Assert
    assert.ok(error instanceof IdoraError);
    assert.ok(error instanceof Error);
    assert.equal(error.name, 'InvalidIdError');
    assert.equal(error.text, 'nope');
    assert.equal(error.expectedPrefix, undefined);
    assert.equal(error.entityType, undefined);
    assert.equal(error.cause, undefined);
    assert.equal(Object.hasOwn(error, 'cause'), false);
  });

  it('explains a missing separator with an example', () => {
    // Act
    const error = captureInvalid(() => IdoraId.parse('usr01k226e0g0008j4ct4ank7f24s'));

    // Assert
    assert.equal(error.reason, 'noSeparator');
    assert.match(error.message, /has no '_' separating a prefix from a value/);
    assert.match(error.message, /looks like usr_01k2f9v8ta7m3x0qbrn4dzeh5c/);
  });

  it('attaches the prefix error as the cause, and repeats its explanation', () => {
    // Act
    const error = captureInvalid(() => IdoraId.parse('us-r_01k226e0g0008j4ct4ank7f24s'));

    // Assert
    assert.equal(error.reason, 'prefix');
    assert.ok(error.cause instanceof InvalidEntityPrefixError);
    assert.equal(error.cause.prefix, 'us-r');
    assert.equal(error.cause.reason, 'character');
    assert.equal(error.cause.index, 2);
    assert.match(error.message, /its prefix is invalid\. The entity prefix 'us-r' contains the character '-' at index 2/);
  });

  it('reports an empty prefix through the cause', () => {
    // Act
    const error = captureInvalid(() => IdoraId.parse('_01k226e0g0008j4ct4ank7f24s'));

    // Assert
    assert.ok(error.cause instanceof InvalidEntityPrefixError);
    assert.equal(error.cause.reason, 'empty');
  });

  const VALUE_FAULTS: ReadonlyArray<readonly [text: string, explanation: RegExp]> = [
    ['usr_', /nothing after the '_'/],
    ['usr_01k226e0g0008j4ct4ank7f24', /is 25 characters long, but a ULID value is 26 characters and a UUIDv7 value 32/],
    ['usr_01K226E0G0008J4CT4ANK7F24S', /uppercase 'K' at index 2, but values are written in lowercase/],
    ['usr_01k226e0g0008j4ct4ank7f24u', /contains 'u' at index 25, which is not a lowercase Crockford Base32 character/],
    ['usr_81k226e0g0008j4ct4ank7f24s', /begins with '8', but the first character of a ULID is never greater than 7/],
    ['inv_0198846702007011A233445566778899', /uppercase 'A' at index 16, but values are written in lowercase/],
    ['inv_0000000000017000800000000000000g', /contains 'g' at index 31, which is not a lowercase hexadecimal digit/],
    ['inv_00000000000140008000000000000001', /not a version 7 UUID: character 12 is '4'/],
    ['inv_0000000000017000c000000000000001', /does not carry the RFC 9562 variant: character 16 is 'c'/],
    ['inv_01988467-0200-7011-a233-445566778899', /a UUID written with hyphens/],
    ['usr_01k226e0g0008j4ct4ank7f2\u{1F600}', /contains '\u{1F600}' at index 24/u],
  ];

  for (const [text, explanation] of VALUE_FAULTS) {
    it(`explains what is wrong with the value of ${JSON.stringify(text)}`, () => {
      // Act
      const error = captureInvalid(() => IdoraId.parse(text));

      // Assert
      assert.equal(error.reason, 'value');
      assert.match(error.message, explanation);
    });
  }

  it('quotes only the start of a long input, but keeps all of it on the error', () => {
    // Arrange
    const text = `usr_${'0'.repeat(10_000)}`;

    // Act
    const error = captureInvalid(() => IdoraId.parse(text));

    // Assert
    assert.equal(error.text, text);
    assert.ok(error.message.length < 300, `the message is ${error.message.length} characters`);
    assert.match(error.message, /^'usr_0+…' is not a valid Idora identifier/);
  });
});

describe('registry-aware parsing', () => {
  const ids = new Idora({ entities: [[User, 'usr'], [Order, 'ord']] });

  it('parses an identifier with the prefix the entity type was registered with', () => {
    // Act & Assert
    assert.equal(ids.parse(User, USER_ULID).text, USER_ULID);
    assert.equal(ids.tryParse(User, USER_ULID)?.text, USER_ULID);
    assert.equal(ids.isIdFor(User, USER_ULID), true);
    assert.equal(ids.registry.parse(User, USER_ULID).text, USER_ULID);
    assert.equal(ids.registry.tryParse(User, USER_ULID)?.text, USER_ULID);
    assert.equal(ids.registry.isIdFor(User, USER_ULID), true);
  });

  it('parses the other format too, whatever strategy the generator uses', () => {
    // Arrange
    const uuidIds = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });

    // Act & Assert
    assert.equal(uuidIds.parse(User, USER_ULID).format, 'ulid');
    assert.equal(ids.parse(User, 'usr_0198846702007011a233445566778899').format, 'uuidv7');
  });

  it('rejects another entity type’s identifier, naming the entity type it expected', () => {
    // Act
    const error = captureInvalid(() => ids.parse(Order, USER_ULID));

    // Assert
    assert.equal(error.reason, 'prefixMismatch');
    assert.equal(error.expectedPrefix, 'ord');
    assert.equal(error.entityType, Order);
    assert.match(error.message, /has the prefix 'usr', but an identifier for 'Order' has the prefix 'ord'/);
    assert.equal(ids.tryParse(Order, USER_ULID), undefined);
    assert.equal(ids.isIdFor(Order, USER_ULID), false);
  });

  it('carries the entity type on errors for malformed text too', () => {
    // Act
    const error = captureInvalid(() => ids.parse(User, 'usr_nope'));

    // Assert
    assert.equal(error.reason, 'value');
    assert.equal(error.entityType, User);
    assert.equal(error.expectedPrefix, 'usr');
  });

  it('rejects text that is not a string: a TypeError from parse, false and undefined otherwise', () => {
    // Act & Assert
    assert.throws(() => ids.parse(User, undefined as never), TypeError);
    assert.equal(ids.tryParse(User, undefined), undefined);
    assert.equal(ids.isIdFor(User, 42), false);
  });

  it('fails for an unregistered entity type with EntityPrefixNotRegisteredError, even from the non-failing forms', () => {
    // Act & Assert
    for (const action of [
      () => ids.parse(UnregisteredEntity, USER_ULID),
      () => ids.tryParse(UnregisteredEntity, USER_ULID),
      () => ids.isIdFor(UnregisteredEntity, 'garbage'),
      () => ids.registry.parse(UnregisteredEntity, USER_ULID),
    ]) {
      assert.throws(
        action,
        (error: unknown) =>
          error instanceof EntityPrefixNotRegisteredError &&
          error.entityType === UnregisteredEntity &&
          /IdoraId\.parse and an explicit expected prefix/.test(error.message),
      );
    }
  });

  it('narrows unknown input with isIdFor', () => {
    // Arrange
    const input: unknown = USER_ULID;

    // Act & Assert
    if (!ids.isIdFor(User, input)) {
      assert.fail('expected an identifier for User');
    }

    const narrowed: Id = input;
    assert.equal(narrowed, USER_ULID);
  });
});

function captureInvalid(action: () => unknown): InvalidIdError {
  try {
    action();
  } catch (error) {
    assert.ok(error instanceof InvalidIdError, `expected an InvalidIdError, got ${String(error)}`);
    return error;
  }

  assert.fail('expected an InvalidIdError');
}
