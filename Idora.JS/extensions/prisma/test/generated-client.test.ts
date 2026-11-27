import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { Idora } from 'idora.js';

import { idoraExtension } from '../src/index.js';
import type { PrismaClient } from './generated/client.js';

class User {}

const ids = new Idora({ entities: [[User, 'usr']] });

/**
 * Compile-time checks against a client that `prisma generate` built from the README's schema
 * (test/fixtures/schema.prisma). Nothing here runs a query: the hooks are exercised in
 * extension.test.ts. What only a generated client can show is whether the README's calls type-check.
 */
describe('a generated Prisma client, with the schema the README documents', () => {
  it('lets create, createMany and upsert leave the identifier out', () => {
    // Arrange: compiled, never run. If `id` were required by the generated input types, as it is
    // for a field with no @default, this would fail to compile rather than fail here.
    const typeCheck = (client: PrismaClient) => {
      const prisma = client.$extends(idoraExtension({ ids, models: { user: User } }));
      return [
        () => prisma.user.create({ data: { email: 'ada@example.com' } }),
        () => prisma.user.createMany({ data: [{ email: 'ada@example.com' }, { email: 'grace@example.com' }] }),
        () =>
          prisma.user.upsert({
            where: { email: 'ada@example.com' },
            create: { email: 'ada@example.com' },
            update: {},
          }),
      ];
    };

    // Act & Assert
    assert.equal(typeof typeCheck, 'function');
  });
});
