import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { getTableConfig, mysqlTable, QueryBuilder, varchar } from 'drizzle-orm/mysql-core';
import { Idora, IdoraId } from 'idora.js';

import { createdAfter, createdBefore, createdBetween } from '../src/index.js';
import { idoraId, idoraPrimaryKey } from '../src/mysql.js';
import { T } from './support.js';

class User {}
class Invoice {}

const ids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr'], [Invoice, 'invoice']] });

const users = mysqlTable('users', {
  id: idoraPrimaryKey(ids, User),
  email: varchar({ length: 320 }).notNull(),
});

const invoices = mysqlTable('invoices', {
  id: idoraPrimaryKey(ids, Invoice, { mode: 'parsed' }),
  userId: idoraId(ids, User, { name: 'user_id' }).notNull(),
});

/*
 * There is no MySQL that runs in-process, so these tests stop at the SQL and the column
 * definitions. What reaches the database is the same as for the other dialects, which are tested
 * against real databases.
 */

describe('MySQL columns', () => {
  it('declares a varchar exactly as long as the entity’s identifiers in the generator’s format', () => {
    // Act
    const [id] = getTableConfig(users).columns;
    const [invoiceId, userId] = getTableConfig(invoices).columns;

    // Assert: UUIDv7 values are 32 characters.
    assert.equal(id!.getSQLType(), 'varchar(36)');
    assert.equal(invoiceId!.getSQLType(), 'varchar(40)');
    assert.equal(userId!.getSQLType(), 'varchar(36)');
    assert.equal(userId!.name, 'user_id');
    assert.equal(id!.primary && id!.notNull && id!.hasDefault, true);
    assert.equal(userId!.hasDefault, false);
  });

  it('generates identifiers, or parsed identifiers, as defaults', () => {
    // Act
    const [id] = getTableConfig(users).columns;
    const [invoiceId] = getTableConfig(invoices).columns;
    const generated = id!.defaultFn!() as string;
    const parsed = invoiceId!.defaultFn!() as IdoraId;

    // Assert
    assert.equal(ids.parse(User, generated).format, 'uuidv7');
    assert.ok(parsed instanceof IdoraId);
    assert.equal(parsed.prefix, 'invoice');
  });

  it('renders the time filters as ranges in the generator’s format', () => {
    // Arrange
    const qb = new QueryBuilder();

    // Act
    const between = qb.select().from(users).where(createdBetween(users.id, T, T)).toSQL();
    const afterT = qb.select().from(invoices).where(createdAfter(invoices.id, T)).toSQL();
    const beforeT = qb.select().from(invoices).where(createdBefore(invoices.id, new Date(T))).toSQL();

    // Assert
    assert.match(between.sql, /where \(`users`\.`id` >= \? and `users`\.`id` <= \?\)$/);
    assert.deepEqual(between.params, ['usr_01988467020070008000000000000000', 'usr_0198846702007fffbfffffffffffffff']);
    assert.match(afterT.sql, /where `invoices`\.`id` > \?$/);
    assert.deepEqual(afterT.params, ['invoice_0198846702007fffbfffffffffffffff'], 'a parsed column binds the text');
    assert.deepEqual(beforeT.params, ['invoice_01988467020070008000000000000000']);
  });
});
