import assert from 'node:assert/strict';
import { after, before, beforeEach, describe, it } from 'node:test';

import { PGlite } from '@electric-sql/pglite';
import { asc, eq, sql } from 'drizzle-orm';
import { alias, getTableConfig, pgTable, text } from 'drizzle-orm/pg-core';
import { drizzle } from 'drizzle-orm/pglite';
import { EntityPrefixNotRegisteredError, type Id, Idora, IdoraId, InvalidIdError } from 'idora.js';

import { createdAfter, createdBefore, createdBetween } from '../src/index.js';
import { idoraId, idoraPrimaryKey } from '../src/pg.js';
import { idAt, T } from './support.js';

class User {}
class Order {}
class Unregistered {}

const ids = new Idora({ entities: [[User, 'usr'], [Order, 'ord']] });

const users = pgTable('users', {
  id: idoraPrimaryKey(ids, User),
  name: text().notNull(),
});

const orders = pgTable('orders', {
  id: idoraPrimaryKey(ids, Order, { mode: 'parsed' }),
  userId: idoraId(ids, User, { name: 'user_id' })
    .notNull()
    .references(() => users.id),
});

/** A table of identifiers declared without this package, for the explicit-range form. */
const legacy = pgTable('legacy', { ref: text().primaryKey() });

/**
 * A real PostgreSQL, compiled to WebAssembly: no server, no native build, and the same parser,
 * planner and collation rules as the real thing.
 */
const client = new PGlite();
const db = drizzle({ client });

before(async () => {
  await client.exec(`
    CREATE TABLE users (id varchar(30) PRIMARY KEY, name text NOT NULL);
    CREATE TABLE orders (id varchar(30) PRIMARY KEY, user_id varchar(30) NOT NULL REFERENCES users (id));
    CREATE TABLE legacy (ref text PRIMARY KEY);
  `);
});

after(async () => {
  await client.close();
});

beforeEach(async () => {
  await client.exec('DELETE FROM orders; DELETE FROM users; DELETE FROM legacy;');
});

describe('PostgreSQL columns', () => {
  it('declares a varchar exactly as long as the entity’s identifiers', () => {
    // Arrange
    const uuidIds = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });
    const uuidTable = pgTable('t', { id: idoraPrimaryKey(uuidIds, User) });

    // Act
    const [id] = getTableConfig(users).columns;
    const [orderId, userId] = getTableConfig(orders).columns;

    // Assert
    assert.equal(id!.getSQLType(), 'varchar(30)');
    assert.equal(orderId!.getSQLType(), 'varchar(30)', 'a parsed column is the same type in the database');
    assert.equal(userId!.getSQLType(), 'varchar(30)');
    assert.equal(getTableConfig(uuidTable).columns[0]!.getSQLType(), 'varchar(36)');
  });

  it('makes idoraPrimaryKey a not-null primary key with a generated default, and idoraId neither', () => {
    // Act
    const [id] = getTableConfig(users).columns;
    const userId = getTableConfig(orders).columns[1]!;

    // Assert
    assert.equal(id!.primary, true);
    assert.equal(id!.notNull, true);
    assert.equal(id!.hasDefault, true);
    assert.equal(userId.primary, false);
    assert.equal(userId.hasDefault, false);
    assert.equal(userId.name, 'user_id');
  });

  it('types the insert model’s key as optional and the select model’s by mode', () => {
    // Arrange
    const newUser: typeof users.$inferInsert = { name: 'Ada' };
    const user: typeof users.$inferSelect = { id: 'usr_01k226e0g0008j4ct4ank7f24s', name: 'Ada' };
    const order: typeof orders.$inferSelect = { id: IdoraId.parse('ord_01k226e0g0008j4ct4ank7f24s'), userId: user.id };

    // Act & Assert
    // @ts-expect-error -- a parsed column holds IdoraId objects, not strings.
    const wrong: typeof orders.$inferSelect = { id: 'ord_01k226e0g0008j4ct4ank7f24s', userId: user.id };
    assert.equal(newUser.id, undefined);
    assert.equal(typeof user.id, 'string');
    assert.ok(order.id instanceof IdoraId);
    assert.equal(typeof wrong.id, 'string');
  });

  it('fails at definition time for an unregistered entity type, a missing generator or an unknown mode', () => {
    // Act & Assert
    assert.throws(() => idoraPrimaryKey(ids, Unregistered), EntityPrefixNotRegisteredError);
    assert.throws(() => idoraId({} as never, User), /needs the Idora generator/);
    assert.throws(() => idoraId(ids, User, { mode: 'object' as never }), /'object' is not an Idora column mode/);
  });
});

describe('PostgreSQL inserts and reads', () => {
  it('fills in a fresh identifier for every row inserted without one', async () => {
    // Arrange
    const startedAt = Date.now();

    // Act
    const inserted = await db.insert(users).values([{ name: 'Ada' }, { name: 'Grace' }]).returning();

    // Assert
    assert.equal(inserted.length, 2);
    assert.notEqual(inserted[0]!.id, inserted[1]!.id);
    for (const { id } of inserted) {
      const parsed = ids.parse(User, id);
      assert.ok(parsed.timestampMs >= startedAt);
    }
  });

  it('keeps an identifier the insert supplies', async () => {
    // Arrange
    const id = ids.generate(User);

    // Act
    await db.insert(users).values({ id, name: 'Ada' });

    // Assert
    const [row] = await db.select().from(users).where(eq(users.id, id));
    assert.equal(row?.id, id);
  });

  it('reads a parsed column back as IdoraId objects, and writes IdoraId objects', async () => {
    // Arrange
    const [user] = await db.insert(users).values({ name: 'Ada' }).returning();
    const chosen = ids.parse(Order, ids.generate(Order));

    // Act
    await db.insert(orders).values([{ userId: user!.id }, { id: chosen, userId: user!.id }]);
    const rows = await db.select().from(orders).orderBy(asc(orders.id));

    // Assert
    assert.equal(rows.length, 2);
    for (const row of rows) {
      assert.ok(row.id instanceof IdoraId);
      assert.equal(row.id.prefix, 'ord');
      assert.equal(typeof row.userId, 'string');
    }

    assert.ok(rows.some((row) => row.id.equals(chosen)));
    assert.deepEqual(rows.map((row) => row.id.text), rows.map((row) => row.id).sort(IdoraId.compare).map(String));
  });

  it('refuses to write another entity’s IdoraId into a parsed column', async () => {
    // Arrange
    const [user] = await db.insert(users).values({ name: 'Ada' }).returning();
    const userId = ids.parse(User, user!.id);

    // Act & Assert
    await assert.rejects(
      db.insert(orders).values({ id: userId as never, userId: user!.id }),
      (error: unknown) => error instanceof InvalidIdError && error.reason === 'prefixMismatch',
    );
  });

  it('refuses to read a row whose identifier belongs to another entity into a parsed column', async () => {
    // Arrange
    const [user] = await db.insert(users).values({ name: 'Ada' }).returning();
    await client.query('INSERT INTO orders (id, user_id) VALUES ($1, $2)', [ids.generate(User), user!.id]);

    // Act & Assert
    await assert.rejects(
      db.select().from(orders),
      (error: unknown) => error instanceof InvalidIdError && error.reason === 'prefixMismatch',
    );
  });
});

describe('PostgreSQL time filters', () => {
  /** Users minted at T - 1, T, T + 1 and T + 2. */
  async function seedUsers(): Promise<Map<number, string>> {
    const byTime = new Map([-1, 0, 1, 2].map((offset) => [T + offset, idAt('usr', T + offset)] as const));
    await db.insert(users).values([...byTime.values()].map((id) => ({ id, name: id })));
    return byTime;
  }

  async function userIdsWhere(condition: ReturnType<typeof createdBetween>): Promise<string[]> {
    const rows = await db.select({ id: users.id }).from(users).where(condition).orderBy(asc(users.id));
    return rows.map((row) => row.id);
  }

  it('selects exactly the rows minted between two times, inclusive', async () => {
    // Arrange
    const byTime = await seedUsers();

    // Act
    const selected = await userIdsWhere(createdBetween(users.id, T, new Date(T + 1)));

    // Assert
    assert.deepEqual(selected, [byTime.get(T), byTime.get(T + 1)]);
  });

  it('selects the rows minted strictly after, or strictly before, an instant', async () => {
    // Arrange
    const byTime = await seedUsers();

    // Act
    const afterT = await userIdsWhere(createdAfter(users.id, T));
    const beforeT = await userIdsWhere(createdBefore(users.id, new Date(T)));

    // Assert
    assert.deepEqual(afterT, [byTime.get(T + 1), byTime.get(T + 2)]);
    assert.deepEqual(beforeT, [byTime.get(T - 1)]);
  });

  it('selects what the generator has just minted, and nothing from an earlier span', async () => {
    // Arrange
    const from = Date.now();
    const inserted = await db.insert(users).values([{ name: 'Ada' }, { name: 'Grace' }]).returning();
    const to = Date.now();

    // Act
    const now = await userIdsWhere(createdBetween(users.id, from, to));
    const earlier = await userIdsWhere(createdBetween(users.id, from - 60_000, from - 1));

    // Assert
    assert.deepEqual(now, inserted.map((row) => row.id).sort());
    assert.deepEqual(earlier, []);
  });

  it('compares the bounds as identifiers in a parsed column', async () => {
    // Arrange
    const [user] = await db.insert(users).values({ name: 'Ada' }).returning();
    const atT = ids.parse(Order, idAt('ord', T));
    const later = ids.parse(Order, idAt('ord', T + 5));
    await db.insert(orders).values([atT, later].map((id) => ({ id, userId: user!.id })));

    // Act
    const rows = await db.select().from(orders).where(createdBetween(orders.id, T, T + 1));

    // Assert
    assert.deepEqual(rows.map((row) => row.id.text), [atT.text]);
  });

  it('uses the index rather than reading every row', async () => {
    // Arrange
    await seedUsers();
    await client.exec('SET enable_seqscan = off');

    // Act
    const query = db.select().from(users).where(createdBetween(users.id, T, T + 1)).toSQL();
    const plan = await client.query<{ 'QUERY PLAN': string }>(`EXPLAIN ${query.sql}`, query.params);
    await client.exec('SET enable_seqscan = on');

    // Assert
    assert.match(plan.rows.map((row) => row['QUERY PLAN']).join('\n'), /Index (Only )?Scan (using|on) users_pkey/);
  });

  it('works through a table alias', async () => {
    // Arrange
    const byTime = await seedUsers();
    const u = alias(users, 'u');

    // Act
    const rows = await db.select({ id: u.id }).from(u).where(createdBetween(u.id, T + 2, T + 2));

    // Assert
    assert.deepEqual(rows.map((row) => row.id), [byTime.get(T + 2)]);
  });

  it('renders as a parameterised range on the column', () => {
    // Act
    const query = db.select({ id: users.id }).from(users).where(createdBetween(users.id, T, T)).toSQL();

    // Assert
    assert.equal(query.sql, 'select "id" from "users" where ("users"."id" >= $1 and "users"."id" <= $2)');
    assert.deepEqual(query.params, ['usr_01k226e0g00000000000000000', 'usr_01k226e0g0zzzzzzzzzzzzzzzz']);
  });

  it('works on any identifier column when told its prefix and format', async () => {
    // Arrange
    const inside = idAt('usr', T);
    await db.insert(legacy).values([{ ref: inside }, { ref: idAt('usr', T + 10) }]);

    // Act
    const rows = await db.select().from(legacy).where(createdBetween(legacy.ref, T, T, { prefix: 'usr', format: 'ulid' }));
    const uuidBounds = db.select().from(legacy).where(createdAfter(legacy.ref, T, { prefix: 'evt', format: 'uuidv7' })).toSQL();

    // Assert
    assert.deepEqual(rows.map((row) => row.ref), [inside]);
    assert.deepEqual(uuidBounds.params, ['evt_0198846702007fffbfffffffffffffff']);
  });

  it('refuses to guess the prefix of a column it did not declare, or to be told it twice', () => {
    // Act & Assert
    assert.throws(() => createdBetween(legacy.ref, T, T), /was not declared with idoraId or idoraPrimaryKey/);
    assert.throws(() => createdBefore(users.id, T, { prefix: 'usr' }), /already knows its prefix and format/);
  });

  it('refuses a time no identifier can carry', () => {
    // Act & Assert
    assert.throws(() => createdBetween(users.id, -1, T), RangeError);
    assert.throws(() => createdAfter(users.id, new Date(Number.NaN)), RangeError);
  });

  it('leaves the column usable in ordinary comparisons', async () => {
    // Arrange
    const byTime = await seedUsers();
    const id: Id = IdoraId.parse(byTime.get(T)!).text;

    // Act
    const [row] = await db.select().from(users).where(sql`${users.id} = ${id}`);

    // Assert
    assert.equal(row?.id, id);
  });
});
