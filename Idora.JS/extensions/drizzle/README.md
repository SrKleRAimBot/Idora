# idora.js-drizzle

[Drizzle ORM](https://orm.drizzle.team) columns and filters for
[Idora](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS) identifiers such as
`usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.

```ts
import { createdBetween } from 'idora.js-drizzle';
import { idoraId, idoraPrimaryKey } from 'idora.js-drizzle/pg';

export const users = pgTable('users', {
  id: idoraPrimaryKey(ids, User),             // varchar(30), filled in on insert
  email: text().notNull(),
});

export const orders = pgTable('orders', {
  id: idoraPrimaryKey(ids, Order),
  userId: idoraId(ids, User).notNull().references(() => users.id),
});

const [user] = await db.insert(users).values({ email }).returning();
user.id; // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'

// Everyone who signed up this week, straight off the primary key's index.
await db.select().from(users).where(createdBetween(users.id, startOfWeek, now));
```

Primary keys that mint themselves in your process — no database extension, no round trip — in a
column sized exactly for them, and time-window queries that need no `created_at` column.

---

## Install

```bash
npm install idora.js-drizzle idora.js drizzle-orm
```

`idora.js` and `drizzle-orm` (0.44 or later, below 1.0) are peer dependencies, and this package adds
no others. It is an ES module with TypeScript declarations included.

---

## Columns

Drizzle's column types belong to a dialect, so the column helpers do too. Each module has the same
two functions:

| Module | For | Column type |
| --- | --- | --- |
| `idora.js-drizzle/pg` | PostgreSQL, and anything speaking its protocol | `varchar(n)` |
| `idora.js-drizzle/mysql` | MySQL, MariaDB, PlanetScale | `varchar(n)` |
| `idora.js-drizzle/sqlite` | SQLite, libSQL and Turso, Cloudflare D1, Bun | `text` |

`n` is the exact length of the entity's identifiers: its prefix, the separator and a 26-character
ULID or 32-character UUIDv7, whichever the generator mints. That is `varchar(30)` for `usr` with
ULIDs — every identifier for one entity type has the same length — and lets the database refuse a
value that cannot be one. SQLite does not enforce lengths, so there the column is plain `text`.

### `idoraPrimaryKey(ids, Entity, options?)`

The table's primary key, not null, filled in by `ids.generate(Entity)` on every insert that does
not supply one, through Drizzle's `$defaultFn`. A value you supply is kept. drizzle-kit migrations
show no default, because the identifier is made in your process rather than by the database.

Identifiers are time-ordered, so inserts land at the end of the primary key's index instead of
scattering through it, as random UUIDs do.

### `idoraId(ids, Entity, options?)`

A column that holds the entity's identifiers but mints none — a foreign key, typically. Chain
Drizzle's own modifiers onto it as usual: `.notNull()`, `.references(...)`, `.unique()`.

Both throw `EntityPrefixNotRegisteredError` for an entity type `ids` has not registered, when the
schema module loads — so a forgotten registration stops start-up rather than the first insert.

### Options

| Option | Default | |
| --- | --- | --- |
| `name` | the property's key | The column's name in the database. |
| `mode` | `'string'` | `'string'` for the identifier's text; `'parsed'` for an `IdoraId` |

With `mode: 'parsed'`, rows are read as `IdoraId` objects — with `createdAt`, `prefix` and the
natural order to hand — and `IdoraId` objects are written as their text. The database column is
the same either way. A parsed column checks the entity's prefix in both directions: a row holding
another entity's identifier throws `InvalidIdError` when read, and so does writing one.

```ts
export const events = pgTable('events', {
  id: idoraPrimaryKey(ids, Event, { mode: 'parsed' }),
});

const [event] = await db.select().from(events).limit(1);
event.id.createdAt; // a Date
```

---

## Filtering by time

An identifier begins with the time it was minted, so the identifiers minted in a span of time are a
contiguous range of strings. These filters turn a span into that range:

| Filter | Selects rows minted |
| --- | --- |
| `createdBetween(column, from, to)` | from `from` to `to`, both inclusive |
| `createdAfter(column, instant)` | strictly after `instant` |
| `createdBefore(column, instant)` | strictly before `instant` |

Times are `Date`s or milliseconds since the Unix epoch, compared to the millisecond. They come from
`idora.js-drizzle` itself and work with every dialect:

```ts
import { createdAfter, createdBetween } from 'idora.js-drizzle';

await db.select().from(users).where(createdBetween(users.id, from, to));
// select … from "users" where ("users"."id" >= $1 and "users"."id" <= $2)
//   $1 = 'usr_01k226e0g00000000000000000', $2 = 'usr_01k2…zzzzzzzzzzzzzzzz'
```

The database compares them with the column's collation, which must order the digits before the lowercase
letters character by character: binary and ordinary linguistic collations do, but a numeric
("natural sort") collation does not, and returns nothing for the range. See
[§6.4 of the specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md#64-range-bounds).

The bounds are the lowest and highest identifiers any generator can mint for the column's entity
type in its format, so the query is an ordinary range over the column and uses its index — the
primary key's, for a primary key. It is tested against a real PostgreSQL (PGlite) and SQLite,
including that the PostgreSQL plan is an index scan.

The filters read the prefix and format from columns declared with `idoraId` or `idoraPrimaryKey`.
For any other column of identifiers, say what it holds:

```ts
createdBetween(legacy.ref, from, to, { prefix: 'usr', format: 'ulid' });
```

A range compares correctly in the binary and `C` collations, and in the usual linguistic ones,
which agree with them on ASCII digits and lowercase letters. A column holding identifiers of more
than one prefix or format is not a range; filter each prefix separately.

---

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
