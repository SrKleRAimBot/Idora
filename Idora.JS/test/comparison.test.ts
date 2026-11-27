import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { compareIds, EntityPrefixNotRegisteredError, Idora, IdoraId, InvalidEntityPrefixError, InvalidIdError } from '../src/index.js';
import { UnregisteredEntity, User } from './support/entities.js';

/** 2025-08-07T12:00:00.000Z, the millisecond most of these identifiers were minted in. */
const T = 1754568000000;

const parse = (text: string): IdoraId => IdoraId.parse(text);

/** Minted at T. */
const USER = parse('usr_01k226e0g0008j4ct4ank7f24s');
/** Minted at T, a UUIDv7. */
const ORDER_UUID = parse('ord_0198846702007011a233445566778899');
/** Minted at T, with a higher random field than USER. */
const ORDER = parse('ord_01k226e0g0fzzzzzzzzzzzzzzz');
/** Minted at T + 1. */
const LATER = parse('ord_01k226e0g1zzqdvk5vnacrgxv6');
/** The same value as LATER, with a prefix that sorts first. */
const LATER_UPPER = parse('Ord_01k226e0g1zzqdvk5vnacrgxv6');

describe('equality', () => {
  it('holds between two parses of the same text', () => {
    // Act & Assert
    assert.ok(parse(USER.text).equals(USER));
    assert.equal(parse(USER.text).compareTo(USER), 0);
  });

  it('does not hold between identifiers that differ only in the case of the prefix', () => {
    // Act & Assert
    assert.equal(LATER.equals(LATER_UPPER), false);
    assert.notEqual(LATER.compareTo(LATER_UPPER), 0);
  });

  it('does not hold with anything that is not an IdoraId, the identifier’s own text included', () => {
    // Act & Assert
    for (const other of [USER.text, undefined, null, { text: USER.text }]) {
      assert.equal(USER.equals(other), false);
    }
  });
});

describe('natural order', () => {
  it('puts the older identifier first, whatever the prefixes and formats', () => {
    // Arrange: ord sorts before usr as text, but this order was created a millisecond later.
    const olderUser = parse('usr_01k226e0g0008j4ct4ank7f24s');
    const newerOrder = parse('ord_0198846702017fee9dccbbaa99887766');

    // Act & Assert
    assert.ok(olderUser.isBefore(newerOrder));
    assert.ok(newerOrder.isAfter(olderUser));
    assert.ok(olderUser.compareTo(newerOrder) < 0);
    assert.ok(IdoraId.compare(newerOrder, olderUser) > 0);
  });

  it('breaks a tie in the millisecond by value, then by prefix', () => {
    // Act & Assert
    assert.ok(ORDER_UUID.isBefore(USER), 'value 0198… sorts before 01k2…');
    assert.ok(USER.isBefore(ORDER), 'value …0008… sorts before …0fzz…');
    assert.ok(LATER_UPPER.isBefore(LATER), "prefix 'Ord' sorts before 'ord'");
  });

  it('is strict: nothing is before or after itself', () => {
    // Act & Assert
    assert.equal(USER.isBefore(USER), false);
    assert.equal(USER.isAfter(USER), false);
  });

  it('sorts unbound, straight from Array.prototype.sort', () => {
    // Arrange
    const shuffled = [LATER, USER, LATER_UPPER, ORDER, ORDER_UUID];

    // Act
    const sorted = shuffled.sort(IdoraId.compare);

    // Assert
    assert.deepEqual(sorted, [ORDER_UUID, USER, ORDER, LATER_UPPER, LATER]);
  });

  it('is generation order for the ULIDs one generator mints', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });
    const generated = Array.from({ length: 2_000 }, (_, i) => (i % 2 === 0 ? ids.generate(User) : ids.generateWithPrefix('evt')));

    // Act
    const sorted = generated.map(parse).reverse().sort(IdoraId.compare).map(String);

    // Assert
    assert.deepEqual(sorted, generated);
  });
});

describe('compareIds', () => {
  it('sorts identifier strings in the natural order rather than as text', () => {
    // Arrange
    const texts = [LATER.text, USER.text, ORDER_UUID.text];

    // Act
    const sorted = texts.slice().sort(compareIds);

    // Assert
    assert.deepEqual(sorted, [ORDER_UUID.text, USER.text, LATER.text]);
    assert.deepEqual(texts.slice().sort(), [ORDER_UUID.text, LATER.text, USER.text], 'plain text order differs');
  });

  it('throws for a string that is not an identifier', () => {
    // Act & Assert
    assert.throws(() => compareIds(USER.text, 'nope'), InvalidIdError);
  });
});

describe('timestamp comparison', () => {
  it('returns zero for identifiers minted in the same millisecond, whatever else differs', () => {
    // Act & Assert
    assert.equal(USER.compareTimestamp(ORDER_UUID), 0);
    assert.equal(USER.compareTimestamp(ORDER), 0);
    assert.ok(USER.isSameMillisecond(ORDER_UUID));
    assert.ok(USER.isBefore(ORDER), 'while the natural order still tells them apart');
  });

  it('orders identifiers minted in different milliseconds', () => {
    // Act & Assert
    assert.equal(USER.compareTimestamp(LATER), -1);
    assert.equal(LATER.compareTimestamp(USER), 1);
    assert.equal(USER.isSameMillisecond(LATER), false);
  });
});

describe('created before and after', () => {
  it('compares against a Date or a millisecond count, strictly', () => {
    // Act & Assert
    assert.ok(USER.isCreatedBefore(T + 1));
    assert.ok(USER.isCreatedBefore(new Date(T + 1)));
    assert.ok(USER.isCreatedAfter(T - 1));
    assert.ok(USER.isCreatedAfter(new Date(T - 1)));
    assert.equal(USER.isCreatedBefore(T), false);
    assert.equal(USER.isCreatedAfter(T), false);
  });

  it('drops the sub-millisecond part of the instant before comparing', () => {
    // Act & Assert: T + 0.9 is still millisecond T.
    assert.equal(USER.isCreatedBefore(T + 0.9), false);
    assert.equal(USER.isCreatedAfter(T + 0.9), false);
    assert.ok(USER.isCreatedAfter(T - 0.1), 'T - 0.1 is millisecond T - 1');
  });

  it('accepts instants an identifier could never carry', () => {
    // Act & Assert
    assert.ok(USER.isCreatedAfter(-1));
    assert.ok(USER.isCreatedBefore(2 ** 53));
  });

  it('throws a RangeError for an invalid Date or a non-finite number, rather than quietly answering false', () => {
    // Act & Assert
    for (const instant of [new Date('not a date'), Number.NaN, Number.POSITIVE_INFINITY]) {
      assert.throws(() => USER.isCreatedBefore(instant), RangeError);
      assert.throws(() => USER.isCreatedAfter(instant), RangeError);
    }
  });

  it('throws a TypeError for an instant that is neither a Date nor a number', () => {
    // Act & Assert
    for (const instant of ['2025-08-07', undefined, null, BigInt(T)]) {
      assert.throws(() => USER.isCreatedBefore(instant as never), TypeError);
    }
  });
});

describe('hasPrefix', () => {
  it('is exact and ordinal, case included', () => {
    // Act & Assert
    assert.ok(USER.hasPrefix('usr'));
    assert.equal(USER.hasPrefix('USR'), false);
    assert.equal(USER.hasPrefix('us'), false);
    assert.equal(USER.hasPrefix('usr_'), false);
  });

  it('narrows the prefix type', () => {
    // Arrange
    const id: IdoraId = USER;

    // Act & Assert
    if (id.hasPrefix('usr')) {
      const prefix: 'usr' = id.prefix;
      assert.equal(prefix, 'usr');
    } else {
      assert.fail('expected the prefix usr');
    }
  });
});

describe('string conversion', () => {
  it('becomes its text as a string, in JSON and in a template literal', () => {
    // Act & Assert
    assert.equal(USER.toString(), USER.text);
    assert.equal(String(USER), USER.text);
    assert.equal(`${USER}`, USER.text);
    assert.equal(USER + '', USER.text);
    assert.equal(JSON.stringify({ id: USER }), `{"id":"${USER.text}"}`);
  });

  it('refuses to become a number, so < and > cannot silently compare texts', () => {
    // Act & Assert
    assert.throws(() => (USER as unknown as number) < (LATER as unknown as number), /cannot be converted to a number.*IdoraId\.compare/);
    assert.throws(() => Number(USER), TypeError);
    assert.throws(() => +(USER as unknown as number), TypeError);
  });
});

describe('range bounds', () => {
  it('bracket every identifier minted in the span, and nothing outside it', () => {
    // Arrange
    const lower = IdoraId.lowerBound('ord', T).text;
    const upper = IdoraId.upperBound('ord', T + 1).text;
    const inside = [ORDER.text, LATER.text];
    const outside = ['ord_01k226e0fzzzzzzzzzzzzzzzzz', 'ord_01k226e0g20000000000000000', 'usr_01k226e0g0008j4ct4ank7f24s'];

    // Act & Assert
    for (const text of inside) {
      assert.ok(text >= lower && text <= upper, `${text} should be inside [${lower}, ${upper}]`);
    }

    for (const text of outside) {
      assert.ok(text < lower || text > upper, `${text} should be outside [${lower}, ${upper}]`);
    }
  });

  it('bracket every UUIDv7 one generator mints in the span', () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7' });
    const from = Date.now();
    const minted = Array.from({ length: 500 }, () => ids.generateWithPrefix('evt'));
    const to = Date.now();

    // Act
    const lower = IdoraId.lowerBound('evt', from, 'uuidv7').text;
    const upper = IdoraId.upperBound('evt', to, 'uuidv7').text;

    // Assert
    assert.ok(minted.every((text) => text >= lower && text <= upper));
  });

  it('are parsed identifiers in their own right, with the format and millisecond asked for', () => {
    // Act
    const lower = IdoraId.lowerBound('usr', new Date(T), 'uuidv7');

    // Assert
    assert.equal(lower.prefix, 'usr');
    assert.equal(lower.format, 'uuidv7');
    assert.equal(lower.timestampMs, T);
    assert.ok(lower.equals(IdoraId.parse(lower.text)));
    assert.ok(Object.isFrozen(lower));
  });

  it('default to ULID', () => {
    // Act & Assert
    assert.equal(IdoraId.lowerBound('usr', T).format, 'ulid');
    assert.equal(IdoraId.lowerBoundValue(T), '01k226e0g00000000000000000');
    assert.equal(IdoraId.upperBoundValue(T), '01k226e0g0zzzzzzzzzzzzzzzz');
  });

  it('drop the sub-millisecond part of the time', () => {
    // Act & Assert
    assert.equal(IdoraId.lowerBoundValue(T + 0.999), IdoraId.lowerBoundValue(T));
  });

  it('accept the first and last millisecond a timestamp can hold', () => {
    // Act & Assert
    assert.equal(IdoraId.lowerBoundValue(0), '00000000000000000000000000');
    assert.equal(IdoraId.upperBoundValue(2 ** 48 - 1, 'uuidv7'), 'ffffffffffff7fffbfffffffffffffff');
  });

  it('throw a RangeError for a time no identifier can carry', () => {
    // Act & Assert
    for (const time of [-1, 2 ** 48, new Date(-1), new Date(Number.NaN), Number.NaN, Number.NEGATIVE_INFINITY]) {
      assert.throws(() => IdoraId.lowerBound('usr', time), RangeError);
      assert.throws(() => IdoraId.upperBoundValue(time, 'uuidv7'), RangeError);
    }
  });

  it('validate the prefix as generateWithPrefix does', () => {
    // Act & Assert
    assert.throws(
      () => IdoraId.lowerBound('usr_', T),
      (error: unknown) => error instanceof InvalidEntityPrefixError && error.reason === 'separator',
    );
    assert.throws(() => IdoraId.upperBound(undefined as never, T), TypeError);
  });

  it('reject an unknown format', () => {
    // Act & Assert
    assert.throws(() => IdoraId.lowerBound('usr', T, 'uuidv4' as never), /'uuidv4' is not an identifier format/);
    assert.throws(() => IdoraId.upperBoundValue(T, 'uuid' as never), TypeError);
  });

  it('come from a generator in its own strategy, for a registered entity type', () => {
    // Arrange
    const ulids = new Idora({ entities: [[User, 'usr']] });
    const uuids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });

    // Act & Assert
    assert.equal(ulids.lowerBound(User, T).text, 'usr_01k226e0g00000000000000000');
    assert.equal(ulids.upperBound(User, T).text, 'usr_01k226e0g0zzzzzzzzzzzzzzzz');
    assert.equal(uuids.lowerBound(User, new Date(T)).text, 'usr_01988467020070008000000000000000');
    assert.equal(uuids.upperBound(User, T).text, 'usr_0198846702007fffbfffffffffffffff');
    assert.throws(() => ulids.lowerBound(UnregisteredEntity, T), EntityPrefixNotRegisteredError);
  });
});
