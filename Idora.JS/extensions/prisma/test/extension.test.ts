import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PrismaClientExtends } from '@prisma/client/extension';
import { EntityPrefixNotRegisteredError, Idora, IdoraId, InvalidEntityPrefixError } from 'idora.js';

import { type IdoraExtensionOptions, idoraExtension } from '../src/index.js';

class User {}
class Post {}
class Unregistered {}

const ids = new Idora({ entities: [[User, 'usr'], [Post, 'pst']] });

const OPERATIONS = ['create', 'createMany', 'createManyAndReturn', 'upsert', 'update', 'findMany'] as const;
type Operation = (typeof OPERATIONS)[number];

type Hook = (options: { model: string; operation: string; args: Record<string, unknown>; query: Query }) => Promise<unknown>;
type Query = (args: Record<string, unknown>) => Promise<unknown>;

/** What a model delegate's operation was finally sent to the database with. */
interface Sent {
  readonly model: string;
  readonly operation: Operation;
  readonly args: Record<string, unknown>;
}

type FakeClient = { readonly name: string | undefined; readonly sent: Sent[] } & Record<
  'user' | 'post' | 'auditLog' | 'session',
  Record<Operation, (args: Record<string, unknown>) => Promise<unknown>>
>;

/**
 * Just enough of a Prisma client to run an extension the way Prisma does, without generating one:
 * `$extends` takes the extension's query hooks, and each model operation passes its arguments
 * through the hook for that operation — whose `query` is the database, here a recorder.
 */
function extend(options: IdoraExtensionOptions): FakeClient {
  const sent: Sent[] = [];
  const base = {
    $extends(extension: { name?: string; query?: { $allModels?: Partial<Record<Operation, Hook>> } }) {
      const hooks = extension.query?.$allModels ?? {};
      const delegate = (model: string) =>
        Object.fromEntries(
          OPERATIONS.map((operation) => [
            operation,
            (args: Record<string, unknown>) => {
              const query: Query = async (finalArgs) => {
                sent.push({ model, operation, args: finalArgs });
                return finalArgs;
              };
              const hook = hooks[operation];
              return hook === undefined ? query(args) : hook({ model, operation, args, query });
            },
          ]),
        );

      return {
        name: extension.name,
        sent,
        user: delegate('User'),
        post: delegate('Post'),
        auditLog: delegate('AuditLog'),
        session: delegate('Session'),
      };
    },
  };

  return idoraExtension(options)(base as never) as unknown as FakeClient;
}

function lastSent(client: FakeClient): Sent {
  const sent = client.sent.at(-1);
  assert.ok(sent, 'expected a query to have been sent');
  return sent;
}

describe('idoraExtension', () => {
  it('is named idora', () => {
    // Act & Assert
    assert.equal(extend({ ids, models: {} }).name, 'idora');
  });

  it('is accepted by a Prisma client’s $extends', () => {
    // Arrange: compiled, never run — the check is that this type-checks against Prisma's own client type.
    const typeCheck = (client: PrismaClientExtends) => client.$extends(idoraExtension({ ids, models: { user: User } }));

    // Act & Assert
    assert.equal(typeof typeCheck, 'function');
  });

  it('fills in a missing identifier on create, from the entity type’s registered prefix', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });
    const startedAt = Date.now();

    // Act
    await prisma.user.create({ data: { email: 'ada@example.com' } });

    // Assert
    const { data } = lastSent(prisma).args as { data: { id: string; email: string } };
    const id = ids.parse(User, data.id);
    assert.equal(data.email, 'ada@example.com');
    assert.ok(id.timestampMs >= startedAt);
  });

  it('keeps an identifier the query supplies', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });
    const id = ids.generate(User);

    // Act
    await prisma.user.create({ data: { id, email: 'ada@example.com' } });

    // Assert
    assert.equal((lastSent(prisma).args['data'] as { id: string }).id, id);
  });

  it('leaves an explicit null for Prisma to reject, rather than replacing it', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });

    // Act
    await prisma.user.create({ data: { id: null } });

    // Assert
    assert.deepEqual(lastSent(prisma).args['data'], { id: null });
  });

  it('never modifies the arguments it was given', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });
    const args = { data: [{ email: 'a@example.com' }], select: { id: true } };
    const snapshot = structuredClone(args);

    // Act
    await prisma.user.createMany(args);

    // Assert
    assert.deepEqual(args, snapshot);
    assert.deepEqual(lastSent(prisma).args['select'], { id: true }, 'everything else is passed on');
  });

  it('fills every record of createMany and createManyAndReturn, a single record included', async () => {
    // Arrange
    const prisma = extend({ ids, models: { post: Post } });
    const kept = ids.generate(Post);

    // Act
    await prisma.post.createMany({ data: [{ title: 'a' }, { id: kept, title: 'b' }, { title: 'c' }] });
    const many = lastSent(prisma).args['data'] as { id: string }[];
    await prisma.post.createManyAndReturn({ data: { title: 'd' } });
    const single = lastSent(prisma).args['data'] as { id: string };

    // Assert
    assert.equal(many.length, 3);
    assert.equal(many[1]!.id, kept);
    assert.equal(new Set(many.map((record) => record.id)).size, 3);
    for (const { id } of [...many, single]) {
      assert.ok(ids.isIdFor(Post, id), `${id} should be a Post identifier`);
    }
  });

  it('fills the create half of an upsert and leaves the update half alone', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });

    // Act
    await prisma.user.upsert({ where: { email: 'a@example.com' }, create: { email: 'a@example.com' }, update: { name: 'Ada' } });

    // Assert
    const { create, update, where } = lastSent(prisma).args as { create: { id: string }; update: object; where: object };
    assert.ok(ids.isIdFor(User, create.id));
    assert.deepEqual(update, { name: 'Ada' });
    assert.deepEqual(where, { email: 'a@example.com' });
  });

  it('leaves other operations, and models it was not told about, untouched', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });
    const createArgs = { data: { title: 'a' } };
    const updateArgs = { where: { id: 'x' }, data: { name: 'Ada' } };

    // Act
    await prisma.post.create(createArgs);
    const unlisted = lastSent(prisma).args;
    await prisma.user.update(updateArgs);
    const update = lastSent(prisma).args;

    // Assert
    assert.equal(unlisted, createArgs);
    assert.equal(update, updateArgs);
  });

  it('accepts a model by its client name or its schema name', async () => {
    // Arrange
    const byClientName = extend({ ids, models: { auditLog: 'aud' } });
    const bySchemaName = extend({ ids, models: { AuditLog: 'aud' } });

    // Act
    await byClientName.auditLog.create({ data: {} });
    await bySchemaName.auditLog.create({ data: {} });

    // Assert
    for (const prisma of [byClientName, bySchemaName]) {
      const { data } = lastSent(prisma).args as { data: { id: string } };
      assert.equal(IdoraId.parse(data.id).prefix, 'aud');
    }
  });

  it('fills a field other than id when told to, from an entity type or a prefix', async () => {
    // Arrange
    const prisma = extend({ ids, models: { session: { prefix: 'ses', field: 'token' }, user: { entity: User, field: 'publicId' } } });

    // Act
    await prisma.session.create({ data: {} });
    const session = lastSent(prisma).args['data'] as Record<string, string>;
    await prisma.user.create({ data: {} });
    const user = lastSent(prisma).args['data'] as Record<string, string>;

    // Assert
    assert.deepEqual(Object.keys(session), ['token']);
    assert.equal(IdoraId.parse(session['token']!).prefix, 'ses');
    assert.deepEqual(Object.keys(user), ['publicId']);
    assert.ok(ids.isIdFor(User, user['publicId']));
  });

  it('mints identifiers in the generator’s strategy', async () => {
    // Arrange
    const uuidIds = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });
    const prisma = extend({ ids: uuidIds, models: { user: User, post: 'pst' } });

    // Act
    await prisma.user.create({ data: {} });
    const user = lastSent(prisma).args['data'] as { id: string };
    await prisma.post.create({ data: {} });
    const post = lastSent(prisma).args['data'] as { id: string };

    // Assert
    assert.equal(IdoraId.parse(user.id).format, 'uuidv7');
    assert.equal(IdoraId.parse(post.id).format, 'uuidv7');
  });

  it('passes data it does not understand through for Prisma to report', async () => {
    // Arrange
    const prisma = extend({ ids, models: { user: User } });

    // Act
    await prisma.user.create({});
    const missing = lastSent(prisma).args;
    await prisma.user.create({ data: 'nonsense' });
    const nonsense = lastSent(prisma).args;

    // Assert
    assert.deepEqual(missing, { data: undefined });
    assert.deepEqual(nonsense, { data: 'nonsense' });
  });
});

describe('idoraExtension configuration', () => {
  it('fails at creation for an unregistered entity type', () => {
    // Act & Assert
    assert.throws(() => idoraExtension({ ids, models: { user: Unregistered } }), EntityPrefixNotRegisteredError);
  });

  it('fails at creation for an invalid prefix', () => {
    // Act & Assert
    assert.throws(() => idoraExtension({ ids, models: { auditLog: 'audit_log' } }), InvalidEntityPrefixError);
    assert.throws(() => idoraExtension({ ids, models: { auditLog: { prefix: '' } } }), InvalidEntityPrefixError);
  });

  it('refuses a model listed twice under its two names', () => {
    // Act & Assert
    assert.throws(() => idoraExtension({ ids, models: { user: User, User: 'usr' } }), /'User' and 'user' given to idoraExtension are the same model/);
  });

  it('refuses a model with both, or neither, of an entity and a prefix, or with a bad field', () => {
    // Act & Assert
    assert.throws(() => idoraExtension({ ids, models: { user: { entity: User, prefix: 'usr' } as never } }), /both an entity and a prefix/);
    assert.throws(() => idoraExtension({ ids, models: { user: {} as never } }), /needs an entity type registered with the generator, or a prefix/);
    assert.throws(() => idoraExtension({ ids, models: { user: null as never } }), /needs an entity type/);
    assert.throws(() => idoraExtension({ ids, models: { user: { entity: User, field: '' } } }), /must be the name of a field/);
  });

  it('refuses options without a generator or models', () => {
    // Act & Assert
    assert.throws(() => idoraExtension({ ids: {} as never, models: {} }), /needs the Idora generator/);
    assert.throws(() => idoraExtension({ ids } as never), /needs 'models'/);
    assert.throws(() => idoraExtension(undefined as never), /needs an options object/);
  });
});
