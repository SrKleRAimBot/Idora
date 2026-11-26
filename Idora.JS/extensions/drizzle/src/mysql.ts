/**
 * Idora identifier columns for MySQL and MariaDB tables (`drizzle-orm/mysql-core`).
 *
 * @packageDocumentation
 */

import type { HasDefault, IsPrimaryKey, NotNull } from 'drizzle-orm';
import {
  type ConvertCustomConfig,
  customType,
  type MySqlCustomColumnBuilder,
  type MySqlVarCharBuilderInitial,
  varchar,
} from 'drizzle-orm/mysql-core';
import type { EntityType, Idora, IdoraId } from 'idora.js';

import { describeColumn, generatorFor, type IdoraColumnOptions, parsedConversions, recall, remember } from './columns.js';

export type { IdoraColumnMode, IdoraColumnOptions } from './columns.js';

/** An identifier column whose values are strings. */
export type MySqlIdoraIdColumn<TName extends string = ''> = MySqlVarCharBuilderInitial<TName, [string, ...string[]], number>;

/** An identifier column whose values are parsed `IdoraId` objects. */
export type MySqlParsedIdoraIdColumn<TName extends string = ''> = MySqlCustomColumnBuilder<
  ConvertCustomConfig<TName, { data: IdoraId; driverData: string }>
>;

/**
 * A column holding identifiers for `entityType` — a foreign key, typically:
 *
 * ```ts
 * import { idoraId, idoraPrimaryKey } from 'idora.js-drizzle/mysql';
 *
 * export const orders = mysqlTable('orders', {
 *   id: idoraPrimaryKey(ids, Order),
 *   userId: idoraId(ids, User).notNull().references(() => users.id),
 * });
 * ```
 *
 * The column is a `varchar` exactly as long as the entity's identifiers — `varchar(30)` for a
 * three-letter prefix and ULIDs — which both documents the format and lets the database refuse a
 * value that cannot be one. It has no default: use {@link idoraPrimaryKey} for the column that
 * mints them.
 *
 * MySQL's default collations ignore case, so the column treats `usr_…` and `USR_…` as equal. That
 * costs nothing here: Idora refuses to register two prefixes that differ only in case, and values
 * are always lowercase.
 *
 * @param ids The generator `entityType` is registered with. Its strategy decides the length.
 * @throws {EntityPrefixNotRegisteredError} `entityType` is not registered with `ids`.
 * @throws {TypeError} `ids` is not an `Idora` generator, or `mode` is not a mode.
 */
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options?: IdoraColumnOptions<'string', TName>,
): MySqlIdoraIdColumn<TName>;
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): MySqlParsedIdoraIdColumn<TName>;
export function idoraId(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): MySqlIdoraIdColumn<string> | MySqlParsedIdoraIdColumn<string> {
  const info = describeColumn(ids, entityType, options);
  const name = options.name ?? '';
  const builder =
    info.mode === 'parsed'
      ? customType<{ data: IdoraId; driverData: string }>({ dataType: () => `varchar(${info.length})`, ...parsedConversions(info) })(name)
      : varchar(name, { length: info.length });

  return remember(builder, info);
}

/**
 * The primary key column for `entityType`, filled with a fresh identifier on every insert that does
 * not supply one:
 *
 * ```ts
 * export const users = mysqlTable('users', {
 *   id: idoraPrimaryKey(ids, User),
 *   email: varchar({ length: 320 }).notNull(),
 * });
 *
 * await db.insert(users).values({ email }); // id: 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
 * ```
 *
 * The identifier is generated in your process by `ids.generate(entityType)`, through Drizzle's
 * `$defaultFn`, so it needs no database extension and no round trip, and drizzle-kit migrations
 * show no default. Because identifiers are time-ordered, inserts land at the end of the primary
 * key's index rather than scattering through it.
 *
 * @param ids The generator `entityType` is registered with.
 * @throws {EntityPrefixNotRegisteredError} `entityType` is not registered with `ids`.
 * @throws {TypeError} `ids` is not an `Idora` generator, or `mode` is not a mode.
 */
export function idoraPrimaryKey<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options?: IdoraColumnOptions<'string', TName>,
): HasDefault<IsPrimaryKey<NotNull<MySqlIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): HasDefault<IsPrimaryKey<NotNull<MySqlParsedIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): unknown {
  const column = idoraId(ids, entityType, options as IdoraColumnOptions<'string'>);
  return column.primaryKey().$defaultFn(generatorFor(recall(column)!) as () => string);
}
