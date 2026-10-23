import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { DuplicateEntityPrefixError, entityPrefix, Idora, InvalidEntityPrefixError } from '../src/index.js';
import * as clashing from './fixtures/clashing.js';
import * as fixtures from './fixtures/entities.js';
import { FixtureBase, FixtureDerived, FixtureOrder, FixtureUndecorated, FixtureUser } from './fixtures/entities.js';
import * as malformed from './fixtures/malformed.js';
import { User } from './support/entities.js';

describe('@entityPrefix', () => {
  it('lets a decorated class be listed in entities without repeating its prefix', () => {
    // Arrange & Act
    const ids = new Idora({ entities: [FixtureUser, FixtureOrder] });

    // Assert
    assert.ok(ids.generate(FixtureUser).startsWith('fxusr_'));
    assert.ok(ids.generate(FixtureOrder).startsWith('fxord_'));
  });

  it('collects every decorated class a module exports, and nothing else', () => {
    // Arrange & Act
    const ids = new Idora({ modules: [fixtures] });

    // Assert
    const registered = new Map(ids.registry.registrations);
    assert.deepEqual(
      new Map([...registered].sort(([a], [b]) => (a.name < b.name ? -1 : 1))),
      new Map([
        [fixtures.FixtureAbstract, 'fxabs'],
        [FixtureBase, 'fxbase'],
        [FixtureOrder, 'fxord'],
        [FixtureUser, 'fxusr'],
      ]),
    );
    assert.equal(ids.registry.has(FixtureUndecorated), false);
  });

  it('registers a class exported under two names only once', () => {
    // Arrange: the fixture module exports FixtureUser both by name and as its default.
    assert.equal(fixtures.default, FixtureUser);

    // Act
    const ids = new Idora({ modules: [fixtures] });

    // Assert
    assert.equal([...ids.registry.registrations.keys()].filter((type) => type === FixtureUser).length, 1);
  });

  it('works with a dynamically imported module', async () => {
    // Arrange & Act
    const ids = new Idora({ modules: [await import('./fixtures/entities.js')] });

    // Assert
    assert.equal(ids.registry.getPrefix(FixtureUser), 'fxusr');
  });

  it('is not inherited by subclasses', () => {
    // Arrange & Act
    const ids = new Idora({ modules: [fixtures] });

    // Assert
    assert.equal(ids.registry.has(FixtureDerived), false);
    assert.throws(() => new Idora({ entities: [FixtureDerived] }), /carries no @entityPrefix declaration/);
  });

  it('can be applied by calling it, for plain JavaScript without decorator syntax', () => {
    // Arrange
    class Plain {}
    entityPrefix('pln')(Plain);

    // Act
    const ids = new Idora({ entities: [Plain] });

    // Assert
    assert.ok(ids.generate(Plain).startsWith('pln_'));
  });

  it('refuses to decorate one class twice', () => {
    // Arrange
    class Twice {}
    entityPrefix('one')(Twice);

    // Act & Assert
    assert.throws(() => entityPrefix('two')(Twice), /Twice already declares an entity prefix/);
  });

  it('defers validation to registration, then names the class that carries the bad prefix', () => {
    // Arrange: decorating with a bad prefix does not throw by itself — the fixture module loaded.
    assert.ok(malformed.MalformedEntity);

    // Act & Assert
    assert.throws(
      () => new Idora({ modules: [malformed] }),
      (error: unknown) =>
        error instanceof InvalidEntityPrefixError &&
        error.entityType === malformed.MalformedEntity &&
        error.reason === 'separator' &&
        error.prefix === 'bad_prefix' &&
        /The @entityPrefix decorator on 'MalformedEntity' does not declare a valid prefix/.test(error.message),
    );
  });

  it('names the class when a decorated class listed in entities has a bad prefix', () => {
    // Arrange
    class TooLong {}
    entityPrefix('x'.repeat(17))(TooLong);

    // Act & Assert
    assert.throws(
      () => new Idora({ entities: [TooLong] }),
      (error: unknown) => error instanceof InvalidEntityPrefixError && error.entityType === TooLong && error.reason === 'tooLong',
    );
  });

  it('rejects a non-string declaration, as plain JavaScript could make', () => {
    // Arrange
    class Missing {}
    entityPrefix(undefined as never)(Missing);

    // Act & Assert
    assert.throws(() => new Idora({ entities: [Missing] }), /decorator on 'Missing' does not declare a valid prefix/);
  });

  it('reports a clash inside one module the same way round, whatever the export order', () => {
    // Arrange: the fixture exports Zebra before Aardvark; the scan must visit Aardvark first.
    assert.deepEqual(Object.keys(clashing).sort(), ['Aardvark', 'Zebra']);

    // Act & Assert
    assert.throws(
      () => new Idora({ modules: [clashing] }),
      (error: unknown) =>
        error instanceof DuplicateEntityPrefixError &&
        error.conflictingEntityType === clashing.Aardvark &&
        error.entityType === clashing.Zebra &&
        error.prefix === 'ZCLASH',
    );
  });

  it('rejects the same module listed twice', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ modules: [fixtures, fixtures] }),
      (error: unknown) => error instanceof DuplicateEntityPrefixError && /registered a second time/.test(error.message),
    );
  });

  it('mixes freely with explicit pairs', () => {
    // Arrange & Act
    const ids = new Idora({ entities: [[User, 'usr']], modules: [fixtures] });

    // Assert
    assert.equal(ids.registry.getPrefix(User), 'usr');
    assert.equal(ids.registry.getPrefix(FixtureUser), 'fxusr');
  });

  it('collides with an explicit pair claiming the same prefix', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ entities: [[User, 'FXUSR']], modules: [fixtures] }),
      (error: unknown) =>
        error instanceof DuplicateEntityPrefixError && error.entityType === FixtureUser && error.conflictingEntityType === User,
    );
  });

  it('rejects a decorated class that is also registered explicitly, even with the same prefix', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ entities: [[FixtureUser, 'fxusr']], modules: [fixtures] }),
      (error: unknown) =>
        error instanceof DuplicateEntityPrefixError &&
        error.entityType === FixtureUser &&
        /cannot be registered a second time/.test(error.message),
    );
  });

  it('cannot be overridden by an explicit pair with a different prefix', () => {
    // Arrange & Act & Assert
    assert.throws(
      () => new Idora({ entities: [[FixtureUser, 'other']], modules: [fixtures] }),
      /already registered with the prefix 'other' and cannot also be registered with 'fxusr'/,
    );
  });

  it('rejects something that is not a module', () => {
    // Arrange & Act & Assert
    assert.throws(() => new Idora({ modules: [null as never] }), TypeError);
    assert.throws(() => new Idora({ modules: ['entities' as never] }), TypeError);
  });

  it('works with TypeScript decorator syntax on a local class', () => {
    // Arrange
    @entityPrefix('loc')
    class Local {}

    // Act
    const ids = new Idora({ entities: [Local] });

    // Assert
    assert.ok(ids.generate(Local).startsWith('loc_'));
  });
});
