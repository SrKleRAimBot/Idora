import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { Idora } from '../src/index.js';
import { Order, User } from './support/entities.js';
import { split } from './support/ulid-decoder.js';

const ULID_VALUE = /^[0-7][0-9abcdefghjkmnpqrstvwxyz]{25}$/;
const UUIDV7_VALUE = /^[0-9a-f]{12}7[0-9a-f]{3}[89ab][0-9a-f]{15}$/;

describe('identifier format', () => {
  it('is the registered prefix, an underscore, and a 26-character lowercase ULID by default', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const id = ids.generate(User);

    // Assert
    const { prefix, value } = split(id);
    assert.equal(prefix, 'usr');
    assert.match(value, ULID_VALUE);
    assert.equal(id.length, 'usr_'.length + 26);
  });

  it('uses a 32-character lowercase hyphen-free UUIDv7 when asked to', () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });

    // Act
    const id = ids.generate(User);

    // Assert
    const { prefix, value } = split(id);
    assert.equal(prefix, 'usr');
    assert.match(value, UUIDV7_VALUE);
    assert.equal(id.length, 'usr_'.length + 32);
  });

  it('emits the prefix exactly as registered, including its case', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'USR'], [Order, 'Ord']] });

    // Act & Assert
    assert.ok(ids.generate(User).startsWith('USR_'));
    assert.ok(ids.generate(Order).startsWith('Ord_'));
  });

  it('formats an ad-hoc prefix the same way as a registered one', () => {
    // Arrange
    const ids = new Idora();

    // Act
    const { prefix, value } = split(ids.generateWithPrefix('tmp'));

    // Assert
    assert.equal(prefix, 'tmp');
    assert.match(value, ULID_VALUE);
  });

  it('formats an ad-hoc UUIDv7 the same way as a registered one', () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7' });

    // Act
    const { value } = split(ids.generateWithPrefix('tmp'));

    // Assert
    assert.match(value, UUIDV7_VALUE);
  });

  it('contains exactly one underscore, so splitting on the first is unambiguous', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const id = ids.generate(User);

    // Assert
    assert.equal(id.split('_').length, 2);
  });

  it('is entirely ASCII', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const id = ids.generate(User);

    // Assert
    assert.match(id, /^[\x21-\x7e]+$/);
  });

  it('accepts the longest permitted prefix', () => {
    // Arrange
    const longest = 'abcdefghijklmnop';
    const ids = new Idora({ entities: [[User, longest]] });

    // Act
    const id = ids.generate(User);

    // Assert
    assert.equal(id.length, longest.length + 1 + 26);
  });

  it('reports the value length for each strategy', () => {
    // Arrange & Act & Assert
    assert.equal(new Idora().valueLength, 26);
    assert.equal(new Idora({ strategy: 'ulid' }).valueLength, 26);
    assert.equal(new Idora({ strategy: 'uuidv7' }).valueLength, 32);
  });

  it('defaults to the ULID strategy and says so', () => {
    // Arrange & Act
    const ids = new Idora();

    // Assert
    assert.equal(ids.strategy, 'ulid');
  });

  it('rejects an unknown strategy at construction', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ strategy: 'uuidv4' as never }),
      (error: unknown) => error instanceof TypeError && /'uuidv4' is not an identifier strategy/.test(error.message),
    );
  });

  it('is immutable once built', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act & Assert
    assert.ok(Object.isFrozen(ids));
    assert.throws(() => {
      (ids as { strategy: string }).strategy = 'uuidv7';
    }, TypeError);
  });
});
