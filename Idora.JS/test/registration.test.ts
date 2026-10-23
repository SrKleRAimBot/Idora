import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { DuplicateEntityPrefixError, EntityPrefixNotRegisteredError, Idora, IdoraError } from '../src/index.js';
import { AbstractEntity, Account, Accrual, Invoice, Order, UnregisteredEntity, User } from './support/entities.js';

describe('explicit registration', () => {
  it('maps each class to its own prefix', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr'], [Order, 'ord'], [Invoice, 'inv']] });

    // Act & Assert
    assert.ok(ids.generate(User).startsWith('usr_'));
    assert.ok(ids.generate(Order).startsWith('ord_'));
    assert.ok(ids.generate(Invoice).startsWith('inv_'));
  });

  it('accepts any iterable of entries, such as a Map', () => {
    // Arrange
    const entities = new Map<typeof User | typeof Order, string>([
      [User, 'usr'],
      [Order, 'ord'],
    ]);

    // Act
    const ids = new Idora({ entities });

    // Assert
    assert.equal(ids.registry.getPrefix(Order), 'ord');
  });

  it('accepts abstract classes as keys', () => {
    // Arrange & Act
    const ids = new Idora({ entities: [[AbstractEntity, 'abs']] });

    // Assert
    assert.ok(ids.generate(AbstractEntity).startsWith('abs_'));
  });

  it('works with no registrations at all, for ad-hoc prefixes only', () => {
    // Arrange & Act
    const ids = new Idora();

    // Assert
    assert.equal(ids.registry.registrations.size, 0);
    assert.ok(ids.generateWithPrefix('tmp').startsWith('tmp_'));
  });

  it('refuses to invent a prefix for an unregistered class', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act & Assert
    assert.throws(
      () => ids.generate(UnregisteredEntity),
      (error: unknown) =>
        error instanceof EntityPrefixNotRegisteredError &&
        error instanceof IdoraError &&
        error.entityType === UnregisteredEntity &&
        /No entity prefix is registered for 'UnregisteredEntity'/.test(error.message) &&
        /generateWithPrefix/.test(error.message),
    );
  });

  it('does not treat a subclass of a registered class as registered', () => {
    // Arrange
    class Admin extends User {}
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act & Assert
    assert.throws(() => ids.generate(Admin), EntityPrefixNotRegisteredError);
  });

  it('rejects a prefix registered for two classes', () => {
    // Arrange & Act
    const error = captureDuplicate(() => new Idora({ entities: [[Account, 'acc'], [Accrual, 'acc']] }));

    // Assert
    assert.equal(error.prefix, 'acc');
    assert.equal(error.entityType, Accrual);
    assert.equal(error.conflictingEntityType, Account);
    assert.match(error.message, /'acc' is already registered for 'Account' and cannot also be used for 'Accrual'/);
  });

  it('treats prefixes that differ only in case as a collision', () => {
    // Arrange & Act
    const error = captureDuplicate(() => new Idora({ entities: [[Account, 'acc'], [Accrual, 'ACC']] }));

    // Assert
    assert.equal(error.prefix, 'ACC');
    assert.match(error.message, /compared ignoring case/);
  });

  it('rejects one class registered twice with the same prefix, saying it was registered twice', () => {
    // Arrange & Act
    const error = captureDuplicate(() => new Idora({ entities: [[User, 'usr'], [User, 'usr']] }));

    // Assert
    assert.equal(error.entityType, User);
    assert.equal(error.conflictingEntityType, User);
    assert.match(error.message, /cannot be registered a second time/);
    assert.doesNotMatch(error.message, /cannot also be registered with/);
  });

  it('rejects one class registered twice with different prefixes, naming both', () => {
    // Arrange & Act
    const error = captureDuplicate(() => new Idora({ entities: [[User, 'usr'], [User, 'person']] }));

    // Assert
    assert.equal(error.prefix, 'person');
    assert.match(error.message, /already registered with the prefix 'usr' and cannot also be registered with 'person'/);
  });

  it('reports the type collision first when both rules are broken', () => {
    // Arrange & Act
    const error = captureDuplicate(() => new Idora({ entities: [[User, 'usr'], [Order, 'ord'], [User, 'ord']] }));

    // Assert
    assert.equal(error.conflictingEntityType, User);
    assert.match(error.message, /The entity type 'User' is already registered/);
  });

  it('rejects an entry that is not a class', () => {
    // Arrange & Act & Assert
    assert.throws(() => new Idora({ entities: [['usr' as never, 'usr']] }), TypeError);
    assert.throws(() => new Idora({ entities: [null as never] }), TypeError);
  });

  it('rejects an undecorated class listed without a prefix, saying how to fix it', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ entities: [User] }),
      (error: unknown) =>
        error instanceof TypeError && /carries no @entityPrefix declaration/.test(error.message) && /\[User, 'abc'\]/.test(error.message),
    );
  });
});

describe('the registry', () => {
  const ids = new Idora({ entities: [[User, 'usr'], [Order, 'ord']] });

  it('returns a registered prefix without its separator', () => {
    // Act & Assert
    assert.equal(ids.registry.getPrefix(User), 'usr');
  });

  it('throws for an unregistered class from getPrefix', () => {
    // Act & Assert
    assert.throws(() => ids.registry.getPrefix(UnregisteredEntity), EntityPrefixNotRegisteredError);
  });

  it('returns undefined for an unregistered class from findPrefix', () => {
    // Act & Assert
    assert.equal(ids.registry.findPrefix(UnregisteredEntity), undefined);
    assert.equal(ids.registry.findPrefix(Order), 'ord');
  });

  it('answers has() without throwing', () => {
    // Act & Assert
    assert.equal(ids.registry.has(User), true);
    assert.equal(ids.registry.has(UnregisteredEntity), false);
  });

  it('lists every registration in the order it was made', () => {
    // Act
    const registrations = [...ids.registry.registrations];

    // Assert
    assert.deepEqual(registrations, [
      [User, 'usr'],
      [Order, 'ord'],
    ]);
  });

  it('cannot be changed through the registrations view, even with a cast', () => {
    // Arrange
    const view = ids.registry.registrations as Map<unknown, string>;

    // Act & Assert: ReadonlyMap is only a compile-time promise, so the view has no mutators at all.
    assert.equal(typeof view.set, 'undefined');
    assert.equal(typeof view.delete, 'undefined');
    assert.equal(typeof view.clear, 'undefined');
    assert.throws(() => view.set(UnregisteredEntity, 'oops'), TypeError);
    assert.equal(ids.registry.findPrefix(UnregisteredEntity), undefined);
  });

  it('is independent per generator', () => {
    // Arrange
    const other = new Idora({ entities: [[User, 'person']] });

    // Act & Assert
    assert.equal(ids.registry.getPrefix(User), 'usr');
    assert.equal(other.registry.getPrefix(User), 'person');
  });
});

function captureDuplicate(action: () => unknown): DuplicateEntityPrefixError {
  try {
    action();
  } catch (error) {
    assert.ok(error instanceof DuplicateEntityPrefixError, `expected DuplicateEntityPrefixError, got ${String(error)}`);
    assert.ok(error instanceof IdoraError);
    return error;
  }

  assert.fail('expected a DuplicateEntityPrefixError');
}
