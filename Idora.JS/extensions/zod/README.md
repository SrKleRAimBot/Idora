# idora.js-zod

[Zod](https://zod.dev) schemas for [Idora](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS)
identifiers such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.

```ts
import { idoraId } from 'idora.js-zod';
import * as z from 'zod';

const Params = z.object({ userId: idoraId({ prefix: 'usr' }) });

Params.parse({ userId: 'usr_01k226e0g0008j4ct4ank7f24s' }); // { userId: Id<'usr'> }
Params.parse({ userId: 'ord_01k226e0g0008j4ct4ank7f24s' });
// ZodError: Invalid identifier: expected the prefix 'usr', received 'ord'
```

One line instead of a hand-written regular expression that is almost right: the schema applies
exactly the rules every Idora package parses by, says precisely what is wrong with a rejected
identifier, and types what it accepts as a checked `Id<'usr'>` rather than a bare `string`.

---

## Install

```bash
npm install idora.js-zod idora.js zod
```

`idora.js` and `zod` (4.1 or later) are peer dependencies — you already have them, and this package
adds no others. It is an ES module with TypeScript declarations included, and runs wherever Zod and
`idora.js` do.

---

## Schemas

### `idoraId(options?)`

A string schema that accepts Idora identifiers. Its output is the input string, unchanged —
parsing is strict, so a valid identifier never needs rewriting — typed as
[`Id`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS#a-branded-type-for-checked-identifiers).

```ts
idoraId();                                // any well-formed identifier       → Id
idoraId({ prefix: 'usr' });               // exactly the prefix usr           → Id<'usr'>
idoraId({ entity: Order, registry: ids }); // the prefix Order is registered with → Id
```

`registry` is your `Idora` generator, or its `registry`. The prefix is looked up when the schema is
built, so naming an unregistered entity type throws `EntityPrefixNotRegisteredError` at start-up
rather than on the first request; an invalid `prefix` throws `InvalidEntityPrefixError` the same
way.

### `parsedIdoraId(options?)`

The same checks, as a [codec](https://zod.dev/codecs) that decodes into a parsed `IdoraId` — with
its `prefix`, `createdAt` and natural order — and encodes one back into its text:

```ts
import { parsedIdoraId } from 'idora.js-zod';

const Query = z.object({ after: parsedIdoraId({ prefix: 'evt' }) });

const { after } = Query.parse(request.query);
after.createdAt;                                // when the event was minted

z.encode(Query, { after });                     // { after: 'evt_01k2…' }
```

Encoding checks the text on its way out, so an `IdoraId` with the wrong prefix is refused in that
direction too.

---

## Issues

A rejected identifier raises one `custom` issue. Its message is short and meant for whoever sent
the identifier; its `params` say why, for code that handles a `ZodError`:

| `params.reason` | Default message |
| --- | --- |
| `noSeparator` | Invalid identifier: expected a prefix and a value separated by '_', such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c |
| `prefix` | Invalid identifier: the prefix before '_' must be 1 to 16 ASCII letters or digits |
| `value` | Invalid identifier: the value after '_' must be a lowercase ULID or UUIDv7 |
| `prefixMismatch` | Invalid identifier: expected the prefix 'usr', received 'ord' |

`params.expectedPrefix` is the prefix the schema expected, if any. The reasons are those of
`InvalidIdError` in `idora.js`, and are checked in that order. Anything that is not a string is
rejected by `z.string()` in the usual way, before any of this.

Replace the message with `error`, either fixed or built from the `InvalidIdError`:

```ts
idoraId({ prefix: 'usr', error: 'That is not a user' });
idoraId({ entity: User, registry: ids, error: (error) => `Not a user identifier (${error.reason})` });
```

---

## JSON Schema and OpenAPI

`idoraId()` is a plain string schema with a check attached, so `z.toJSONSchema` describes it as
`{ "type": "string" }`. `parsedIdoraId()` has a string as its input and an `IdoraId` as its output,
so describe it with `{ io: 'input' }`.

---

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
