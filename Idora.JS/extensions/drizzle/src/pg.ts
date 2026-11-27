/**
 * Idora identifier columns for PostgreSQL tables (`drizzle-orm/pg-core`).
 *
 * @packageDocumentation
 */

import type { HasDefault, IsPrimaryKey, NotNull } from 'drizzle-orm';
import { type ConvertCustomConfig, customType, type PgCustomColumnBuilder, type PgVarcharBuilderInitial, varchar } from 'drizzle-orm/pg-core';
import type { EntityType, Idora, IdoraId } from 'idora.js';

import { describeColumn, generatorFor, type IdoraColumnOptions, parsedConversions, recall, remember } from './columns.js';

export type { IdoraColumnMode, IdoraColumnOptions } from './columns.js';

/** An identifier column whose values are strings. */
export type PgIdoraIdColumn<TName extends string = ''> = PgVarcharBuilderInitial<TName, [string, ...string[]], number>;

/** An identifier column whose values are parsed `IdoraId` objects. */
export type PgParsedIdoraIdColumn<TName extends string = ''> = PgCustomColumnBuilder<
  ConvertCustomConfig<TName, { data: IdoraId; driverData: string }>
>;

/**
 * A column holding identifiers for `entityType` — a foreign key, typically:
 *
 * ```ts
 * import { idoraId, idoraPrimaryKey } from 'idora.js-drizzle/pg';
 *
 * export const orders = pgTable('orders', {
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
 * @param ids The generator `entityType` is registered with. Its strategy decides the length.
 * @throws {EntityPrefixNotRegisteredError} `entityType` is not registered with `ids`.
 * @throws {TypeError} `ids` is not an `Idora` generator, or `mode` is not a mode.
 */
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options?: IdoraColumnOptions<'string', TName>,
): PgIdoraIdColumn<TName>;
export function idoraId<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): PgParsedIdoraIdColumn<TName>;
export function idoraId(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): PgIdoraIdColumn<string> | PgParsedIdoraIdColumn<string> {
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
 * export const users = pgTable('users', {
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
): HasDefault<IsPrimaryKey<NotNull<PgIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey<TName extends string = ''>(
  ids: Idora,
  entityType: EntityType,
  options: IdoraColumnOptions<'parsed', TName>,
): HasDefault<IsPrimaryKey<NotNull<PgParsedIdoraIdColumn<TName>>>>;
export function idoraPrimaryKey(ids: Idora, entityType: EntityType, options: IdoraColumnOptions = {}): unknown {
  const column = idoraId(ids, entityType, options as IdoraColumnOptions<'string'>);
  return column.primaryKey().$defaultFn(generatorFor(recall(column)!) as () => string);
}
