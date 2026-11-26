import { type EntityType, Idora, IdoraId, type IdStrategy } from 'idora.js';

/**
 * What a column's values are in JavaScript.
 *
 * - `'string'` (the default): the identifier's text, as Idora generates it. The column is the
 *   dialect's own string type, so drizzle-kit and every query treat it as one.
 * - `'parsed'`: an `IdoraId`, parsed — and checked against the entity's prefix — as each row is
 *   read, and written back as its text. For code that wants `createdAt` and the natural order to
 *   hand, at the cost of a parse per value.
 */
export type IdoraColumnMode = 'string' | 'parsed';

/** Options shared by every column helper. */
export interface IdoraColumnOptions<TMode extends IdoraColumnMode = IdoraColumnMode, TName extends string = string> {
  /**
   * The column's name in the database. Omit it to use the property's key, as with any Drizzle
   * column declared without a name.
   */
  readonly name?: TName;

  /** What the column's values are in JavaScript. Defaults to `'string'`. See {@link IdoraColumnMode}. */
  readonly mode?: TMode;
}

/** @internal What this package knows about a column it declared. */
export interface IdoraColumnInfo {
  readonly ids: Idora;
  readonly entityType: EntityType;
  readonly prefix: string;
  readonly format: IdStrategy;
  readonly mode: IdoraColumnMode;
  /** The exact length of every identifier for the entity type: prefix, separator and value. */
  readonly length: number;
}

/**
 * The columns this package declared, keyed by their builder's configuration object.
 *
 * Drizzle hands that object on, unchanged, to the column it builds from the builder — the same
 * object, not a copy — so a column found in a table can be traced back to the entity type and
 * generator it was declared for without wrapping or subclassing anything of Drizzle's.
 */
const declaredColumns = new WeakMap<object, IdoraColumnInfo>();

/**
 * Validates a column helper's arguments, failing at schema-definition time rather than on the first
 * insert.
 *
 * @internal
 */
export function describeColumn(ids: Idora, entityType: EntityType, options: IdoraColumnOptions): IdoraColumnInfo {
  if (!(ids instanceof Idora)) {
    throw new TypeError(
      `An Idora column needs the Idora generator its entity types are registered with as its first argument, but received ${ids === null ? 'null' : typeof ids}.`,
    );
  }

  const mode = options.mode ?? 'string';
  if (mode !== 'string' && mode !== 'parsed') {
    throw new TypeError(`'${String(mode)}' is not an Idora column mode. Use 'string' (the default) or 'parsed'.`);
  }

  const prefix = ids.registry.getPrefix(entityType);
  return Object.freeze({
    ids,
    entityType,
    prefix,
    format: ids.strategy,
    mode,
    length: prefix.length + 1 + ids.valueLength,
  });
}

/** @internal Records that `builder` was declared by this package, and returns it. */
export function remember<T>(builder: T, info: IdoraColumnInfo): T {
  declaredColumns.set(configOf(builder)!, info);
  return builder;
}

/** @internal What this package recorded about `column`, if it declared it. */
export function recall(column: unknown): IdoraColumnInfo | undefined {
  const config = configOf(column);
  return config === undefined ? undefined : declaredColumns.get(config);
}

/**
 * @internal The conversions for a `'parsed'` column. Values are checked against the entity's
 * prefix and the column's format in both directions: a row holding another entity's identifier, or
 * one in the other format, fails to read, and writing one fails too, rather than either slipping
 * through. The format matters as much as the prefix: the time filters compute their bounds in the
 * column's format, so a row in the other format would silently fall outside them.
 */
export function parsedConversions(info: IdoraColumnInfo): {
  readonly toDriver: (value: IdoraId) => string;
  readonly fromDriver: (value: string) => IdoraId;
} {
  const { ids, entityType, prefix, format } = info;
  const inFormat = (id: IdoraId): IdoraId => {
    if (id.format !== format) {
      throw new TypeError(
        `'${id.text}' is a ${id.format} identifier, but this column holds ${format} identifiers, the generator's strategy. ` +
          'A column holds one format, so that its time filters find every row; migrate existing rows when changing strategy.',
      );
    }
    return id;
  };
  return {
    toDriver: (value) =>
      inFormat(
        value instanceof IdoraId && value.prefix === prefix
          ? value
          : ids.parse(entityType, value instanceof IdoraId ? value.text : (value as unknown as string)),
      ).text,
    fromDriver: (value) => inFormat(ids.parse(entityType, value)),
  };
}

/** @internal A generator of fresh values for the column: identifiers, or parsed identifiers. */
export function generatorFor(info: IdoraColumnInfo): () => string | IdoraId {
  const { ids, entityType } = info;
  return info.mode === 'parsed' ? () => ids.parse(entityType, ids.generate(entityType)) : () => ids.generate(entityType);
}

/**
 * Builders and columns both keep their runtime configuration in a `config` property. Drizzle types
 * it as protected, though its own query builders read it freely.
 */
function configOf(value: unknown): object | undefined {
  if (value === null || typeof value !== 'object') {
    return undefined;
  }

  const config: unknown = (value as { readonly config?: unknown }).config;
  return config !== null && typeof config === 'object' ? config : undefined;
}
