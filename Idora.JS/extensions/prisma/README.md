# idora.js-prisma

A [Prisma Client extension](https://www.prisma.io/docs/orm/prisma-client/client-extensions) that
gives new rows [Idora](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS) identifiers such
as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.

```ts
import { idoraExtension } from 'idora.js-prisma';
import { PrismaClient } from './generated/prisma/client.js';

export const prisma = new PrismaClient({ adapter }).$extends(
  idoraExtension({
    ids,
    models: {
      user: User,       // the prefix User is registered with
      auditLog: 'aud',  // or a prefix of its own
    },
  }),
);

const user = await prisma.user.create({ data: { email } });
user.id; // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
```

Prisma's `@default` cannot call your code, so a prefixed, time-ordered key otherwise means
remembering `id: ids.generate(User)` at every create. This puts it in one place.

---

## Install

```bash
npm install idora.js-prisma idora.js @prisma/client
```

`idora.js` and `@prisma/client` (6.4 or later, or 7) are peer dependencies, and this package adds no others. It
is an ES module with TypeScript declarations included.

---

## The schema

The identifier is generated in your process, before the query is sent, but the field still needs
`@default(dbgenerated())`:

```prisma
model User {
  id    String @id @default(dbgenerated()) @db.VarChar(30)
  email String @unique
}
```

Prisma's generated types make a field optional in `create`, `createMany` and `upsert` only when
it has a `@default`. Without one, `prisma.user.create({ data: { email } })` does not compile:
`UserCreateInput` requires `id`, and the extension never gets the chance to fill it in.
`dbgenerated()` with no argument is the default that says "the value comes from elsewhere": it
makes `id` optional in the client's types and puts no `DEFAULT` on the column. Prisma Migrate
emits exactly the DDL it would for a plain `String @id` on PostgreSQL, MySQL and SQLite, so a
create that somehow bypasses the extension fails on the `NOT NULL` key instead of storing a value
you did not choose.

The package's own tests generate a client from this schema and type-check these calls against it.

Every identifier for one entity type has the same length — the prefix, the separator, and a
26-character ULID or 32-character UUIDv7 — so `@db.VarChar(30)` suits a three-letter prefix with
ULIDs, on PostgreSQL and MySQL.

---

## Models

`models` maps each model that gets identifiers to where they come from. Models not listed are left
alone. A model may be named as the client spells it (`auditLog`) or as the schema does (`AuditLog`).

| Value | Identifiers |
| --- | --- |
| `User` | `ids.generate(User)`: the prefix the entity type is registered with |
| `'aud'` | `ids.generateWithPrefix('aud')`: for models with no class of their own to register, which in a Prisma project is most of them |
| `{ entity: User, field: 'publicId' }` | the same, in a field other than `id` |
| `{ prefix: 'ses', field: 'token' }` | the same, in a field other than `id` |

Everything is checked when the extension is created: an unregistered entity type throws
`EntityPrefixNotRegisteredError` and an invalid prefix `InvalidEntityPrefixError`, so a mistake
stops start-up rather than the first insert.

---

## What is filled in

| Operation | Filled in |
| --- | --- |
| `create` | `data` |
| `createMany`, `createManyAndReturn` | every record in `data` |
| `upsert` | `create` — `update` is left alone |

Only a field that is missing — `undefined`, or `Prisma.skip` with `strictUndefinedChecks`, as in
`id: suppliedId ?? Prisma.skip` — is filled. An identifier the query supplies is kept, and the
arguments you pass are never modified: the query receives a copy.

Prisma does not run query extensions for records created by a **nested write**, such as
`posts: { create: [...] }` inside a `user.create`. Give those an identifier yourself, with
`ids.generate(Post)`.

---

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
