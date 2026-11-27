import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { asc, sql } from 'drizzle-orm';
import { getTableConfig, QueryBuilder, sqliteTable, text } from 'drizzle-orm/sqlite-core';
import { drizzle } from 'drizzle-orm/sqlite-proxy';
import { Idora, IdoraId } from 'idora.js';

import { createdAfter, createdBetween } from '../src/index.js';
import { idoraId, idoraPrimaryKey } from '../src/sqlite.js';
import { idAt, T } from './support.js';

class User {}
class Session {}

const ids = new Idora({ entities: [[User, 'usr'], [Session, 'ses']] });

const users = sqliteTable('users', {
  id: idoraPrimaryKey(ids, User),
  name: text().notNull(),
});

const sessions = sqliteTable('sessions', {
  id: idoraPrimaryKey(ids, Session, { mode: 'parsed', name: 'session_id' }),
  userId: idoraId(ids, User).notNull(),
});

/**
 * Node.js's built-in SQLite, where this version of Node.js has it (22.13 and later without a flag).
 * Drizzle reaches it through its proxy driver, which hands each statement to a callback.
 */
const sqlite = await import('node:sqlite').catch(() => undefined);

function connect() {
  const database = new sqlite!.DatabaseSync(':memory:');
  database.exec(`
    CREATE TABLE users (id text PRIMARY KEY NOT NULL, name text NOT NULL);
    CREATE TABLE sessions (session_id text PRIMARY KEY NOT NULL, "userId" text NOT NULL);
  `);

  return drizzle(async (query, params, method) => {
    const statement = database.prepare(query);
    const values = params as (string | number | null)[];
    if (method === 'run') {
      statement.run(...values);
      return { rows: [] };
    }

    const rows = statement.all(...values).map((row) => Object.values(row as object));
    return { rows: method === 'get' ? (rows[0] ?? []) : rows };
  });
}

describe('SQLite columns', () => {
  it('declares text, which is all SQLite has', () => {
    // Act
    const [id] = getTableConfig(users).columns;
    const [sessionId, userId] = getTableConfig(sessions).columns;

    // Assert
    assert.equal(id!.getSQLType(), 'text');
    assert.equal(sessionId!.getSQLType(), 'text');
    assert.equal(sessionId!.name, 'session_id');
    assert.equal(userId!.getSQLType(), 'text');
    assert.equal(id!.primary && id!.notNull && id!.hasDefault, true);
  });

  it('renders the time filters as a range on the column', () => {
    // Act
    const query = new QueryBuilder().select().from(users).where(createdBetween(users.id, T, T)).toSQL();

    // Assert
    assert.match(query.sql, /where \("users"\."id" >= \? and "users"\."id" <= \?\)$/);
    assert.deepEqual(query.params, ['usr_01k226e0g00000000000000000', 'usr_01k226e0g0zzzzzzzzzzzzzzzz']);
  });
});

describe('SQLite inserts, reads and filters', { skip: sqlite === undefined && 'node:sqlite is not available in this Node.js' }, () => {
  it('fills in identifiers, and filters by when they were minted', async () => {
    // Arrange
    const db = connect();
    const old = idAt('usr', T);
    const from = Date.now();

    // Act
    await db.insert(users).values([{ id: old, name: 'Ada' }, { name: 'Grace' }, { name: 'Edsger' }]);
    const recent = await db.select().from(users).where(createdAfter(users.id, from - 1)).orderBy(asc(users.id));
    const all = await db.select().from(users).orderBy(asc(users.id));

    // Assert
    assert.equal(all.length, 3);
    assert.equal(all[0]!.id, old, 'the oldest identifier sorts first');
    assert.deepEqual(recent.map((row) => row.name), ['Grace', 'Edsger']);
    for (const row of recent) {
      assert.ok(ids.isIdFor(User, row.id));
    }
  });

  it('reads and writes parsed identifiers', async () => {
    // Arrange
    const db = connect();
    const userId = ids.generate(User);

    // Act
    await db.insert(sessions).values([{ userId }, { id: ids.parse(Session, idAt('ses', T)), userId }]);
    const rows = await db.select().from(sessions).where(createdBetween(sessions.id, T, T));

    // Assert
    assert.equal(rows.length, 1);
    assert.ok(rows[0]!.id instanceof IdoraId);
    assert.equal(rows[0]!.id.timestampMs, T);
    assert.equal(rows[0]!.userId, userId);
  });

  it('refuses to write an identifier in the other format into a parsed column', async () => {
    // Arrange: SQLite's text column has no length to catch it, and the ULID time filters would
    // never find a UUIDv7 row.
    const db = connect();
    const uuidV7 = IdoraId.parse(new Idora({ strategy: 'uuidv7', entities: [[Session, 'ses']] }).generate(Session));

    // Act & Assert
    await assert.rejects(
      db.insert(sessions).values({ id: uuidV7, userId: ids.generate(User) }),
      (error: unknown) => error instanceof TypeError && /uuidv7 identifier, but this column holds ulid/.test(error.message),
    );
  });

  it('refuses to read a row whose identifier is in the other format into a parsed column', async () => {
    // Arrange
    const db = connect();
    const uuidV7 = new Idora({ strategy: 'uuidv7', entities: [[Session, 'ses']] }).generate(Session);
    await db.run(sql`INSERT INTO sessions (session_id, "userId") VALUES (${uuidV7}, ${ids.generate(User)})`);

    // Act & Assert
    await assert.rejects(
      db.select().from(sessions),
      (error: unknown) => error instanceof TypeError && /uuidv7 identifier, but this column holds ulid/.test(error.message),
    );
  });
});
