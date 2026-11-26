/**
 * Idora identifier columns for SQLite tables (`drizzle-orm/sqlite-core`), including libSQL, Turso,
 * Cloudflare D1 and Bun's SQLite.
 *
 * @packageDocumentation
 */

import type { HasDefault, IsPrimaryKey, NotNull } from 'drizzle-orm';
import {
  type ConvertCustomConfig,
  customType,
  type SQLiteCustomColumnBuilder,
  type SQLiteTextBuilderInitial,
  text,
} from 'drizzle-orm/sqlite-core';
import type { EntityType, Idora, IdoraId } from 'idora.js';

import { describeColumn, generatorFor, type IdoraColumnOptions, parsedConversions, recall, remember } from './columns.js';

export type { IdoraColumnMode, IdoraColumnOptions } from './columns.js';

/** An identifier column whose values are strings. */
export type SQLiteIdoraIdColumn<TName extends string = ''> = SQLiteTextBuilderInitial<TName, [string, ...string[]], number | undefined>;

/** An identifier column whose values are parsed `IdoraId` objects. */
export type SQLiteParsedIdoraIdColumn<TName extends string = ''> = SQLiteCustomColumnBuilder<
  ConvertCustomConfig<TName, { data: IdoraId; driverData: string }>
>;

/**
 * A column holding identifiers for `entityType` — a foreign key, typically:
 *
 * ```ts
 * import { idoraId, idoraPrimaryKey } from 'idora.js-drizzle/sqlite';
 *
 * export const orders = sqliteTable('orders', {
 *   id: idoraPrimaryKey(ids, Order),
 *   userId: idoraId(ids, User).notNull().references(() => users.id),
 * });
 * ```
 *
 * The column is `text`: SQLite does not enforce lengths, so declaring one would only mislead. It
 * has no default: use {@link idoraPrimaryKey} for the column that mints them.
 *
 * @param ids The generator `entityType` is registered with.
 * @throws {EntityPrefixNotRegisteredError} `entityType` is not registered with `ids`.
 * @throws {TypeError} `ids` is not an `Idora` generator, or `mode` is not a mode.
 */
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options?: IdoraColumnOptions<'string', TName>,
): SQLiteIdoraIdColumn<TName>;
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): SQLiteParsedIdoraIdColumn<TName>;
export function idoraId(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): SQLiteIdoraIdColumn<string> | SQLiteParsedIdoraIdColumn<string> {
  const info = describeColumn(ids, entityType, options);
  const name = options.name ?? '';
  const builder =
    info.mode === 'parsed'
      ? customType<{ data: IdoraId; driverData: string }>({ dataType: () => 'text', ...parsedConversions(info) })(name)
      : text(name);

  return remember(builder, info);
}

/**
 * The primary key column for `entityType`, filled with a fresh identifier on every insert that does
 * not supply one:
 *
 * ```ts
 * export const users = sqliteTable('users', {
 *   id: idoraPrimaryKey(ids, User),
 *   email: text().notNull(),
 * });
 *
 * const [user] = await db.insert(users).values({ email }).returning();
 * user.id; // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
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
): HasDefault<IsPrimaryKey<NotNull<SQLiteIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): HasDefault<IsPrimaryKey<NotNull<SQLiteParsedIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): unknown {
  const column = idoraId(ids, entityType, options as IdoraColumnOptions<'string'>);
  return column.primaryKey().$defaultFn(generatorFor(recall(column)!) as () => string);
}
