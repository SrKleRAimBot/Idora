/**
 * idora.js-drizzle — Drizzle ORM columns and filters for Idora identifiers such as
 * `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
 *
 * The column helpers live in one module per dialect — `idora.js-drizzle/pg`, `/mysql` and
 * `/sqlite` — because Drizzle's column types do. The filters here work with all three.
 *
 * @packageDocumentation
 */

import { and, type Column, gt, gte, lt, lte, type SQL } from 'drizzle-orm';
import { IdoraId, type IdStrategy } from 'idora.js';

import { recall } from './columns.js';

export type { IdoraColumnMode, IdoraColumnOptions } from './columns.js';

/**
 * The prefix and format of a column that was not declared with this package's helpers, so the
 * filters can still work out its bounds.
 */
export interface IdRange {
  /** The prefix every identifier in the column carries. */
  readonly prefix: string;
  /** The format of the values in the column. Defaults to `'ulid'`. */
  readonly format?: IdStrategy;
}

/**
 * Selects the rows whose identifier was minted between `from` and `to`, both inclusive, to the
 * millisecond:
 *
 * ```ts
 * import { createdBetween } from 'idora.js-drizzle';
 *
 * await db.select().from(users).where(createdBetween(users.id, lastMonday, now));
 * // … WHERE "id" >= 'usr_01k226e0g00000000000000000' AND "id" <= 'usr_01k2…zzzzzzzzzzzzzzzz'
 * ```
 *
 * An identifier begins with the time it was minted, so this is a plain string range over the
 * column: it uses the column's index — the primary key's, for a primary key — and reads no row it
 * does not return. There is no need for a separate `created_at` column just to filter on.
 *
 * The bounds are the lowest and highest identifiers any generator can mint for the column's entity
 * type in its format; see `IdoraId.lowerBound` in `idora.js`. They compare correctly in a collation
 * that orders identifiers as bytes, binary or `C`. Many linguistic collations do not (Czech treats
 * `ch` as one letter, Lithuanian sorts `y` before `j`, a numeric collation compares digit runs as
 * numbers), and can put a bound on the wrong side of a row, so the range silently misses it.
 *
 * @param column A column declared with `idoraId` or `idoraPrimaryKey` from this package — or any
 * column of identifiers, when `range` says what prefix and format it holds.
 * @param from A `Date`, or milliseconds since the Unix epoch.
 * @param to A `Date`, or milliseconds since the Unix epoch.
 * @param range Only for a column not declared with this package's helpers.
 * @throws {RangeError} `from` or `to` is before the Unix epoch, after the year 10889, or not a valid
 * time.
 * @throws {TypeError} The column's prefix and format are unknown, or given twice.
 */
export function createdBetween(column: Column, from: Date | number, to: Date | number, range?: IdRange): SQL {
  const bounds = boundsFor(column, range);
  // `and` returns undefined only when every condition is undefined, which these never are.
  return and(gte(column, bounds.lower(from)), lte(column, bounds.upper(to)))!;
}

/**
 * Selects the rows whose identifier was minted strictly after `instant`, to the millisecond. See
 * {@link createdBetween}.
 *
 * @throws {RangeError} `instant` is before the Unix epoch, after the year 10889, or not a valid time.
 * @throws {TypeError} The column's prefix and format are unknown, or given twice.
 */
export function createdAfter(column: Column, instant: Date | number, range?: IdRange): SQL {
  return gt(column, boundsFor(column, range).upper(instant));
}

/**
 * Selects the rows whose identifier was minted strictly before `instant`, to the millisecond. See
 * {@link createdBetween}.
 *
 * @throws {RangeError} `instant` is before the Unix epoch, after the year 10889, or not a valid time.
 * @throws {TypeError} The column's prefix and format are unknown, or given twice.
 */
export function createdBefore(column: Column, instant: Date | number, range?: IdRange): SQL {
  return lt(column, boundsFor(column, range).lower(instant));
}

/**
 * The bound values to compare `column` with, in the form the column's own mapping expects: the
 * text for a string column, an `IdoraId` for a parsed one. The comparison operators pass each one
 * through the column's mapping, exactly as they would a value you wrote yourself — hence the casts,
 * since the operators' types cannot know which form this column takes.
 */
function boundsFor(column: Column, range: IdRange | undefined): {
  readonly lower: (time: Date | number) => never;
  readonly upper: (time: Date | number) => never;
} {
  const info = recall(column);
  if (info !== undefined) {
    if (range !== undefined) {
      throw new TypeError(
        `The column '${column.name}' was declared with idora.js-drizzle and already knows its prefix and format; leave out the range.`,
      );
    }

    const { prefix, format } = info;
    return info.mode === 'parsed'
      ? {
          lower: (time) => IdoraId.lowerBound(prefix, time, format) as never,
          upper: (time) => IdoraId.upperBound(prefix, time, format) as never,
        }
      : {
          lower: (time) => IdoraId.lowerBound(prefix, time, format).text as never,
          upper: (time) => IdoraId.upperBound(prefix, time, format).text as never,
        };
  }

  if (range === undefined) {
    throw new TypeError(
      `The column '${column.name}' was not declared with idoraId or idoraPrimaryKey from idora.js-drizzle, so its prefix and format are unknown. Declare it with one of them, or say what it holds, as in createdBetween(table.${column.name}, from, to, { prefix: 'usr', format: 'ulid' }).`,
    );
  }

  const { prefix, format = 'ulid' } = range;
  return {
    lower: (time) => IdoraId.lowerBound(prefix, time, format).text as never,
    upper: (time) => IdoraId.upperBound(prefix, time, format).text as never,
  };
}
