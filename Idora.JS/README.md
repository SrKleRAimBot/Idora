# Idora.JS

Entity identifiers that tell you what they are, and sort by when they were made.

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, and **no dependencies at all** — not one package in your `node_modules` that
you did not already have.

> Part of [**Idora**](https://github.com/SrKleRAimBot/Idora) — the same identifiers, with the same
> rules, for [.NET](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET),
> [JavaScript/TypeScript](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS),
> [Java](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Java),
> [Go](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Go) and
> [Rust](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Rust). An identifier minted by any
> of them can be sorted, split and validated by any other.

---

## Why not just use `crypto.randomUUID()`?

Two reasons, and they compound.

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` turns up in
a log line, a stack trace or a customer's email, you cannot tell whether it is a user, an order or
an invoice. You go and look. With `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` you already know, and so does
everybody else on the team. This is why Stripe, GitHub and others put prefixes on their public
identifiers.

**A random identifier is hostile to your database.** `crypto.randomUUID()` produces version 4
UUIDs, scattered uniformly across the keyspace, so every insert lands in a different page of the
index. Pages split, the index fragments, and the working set grows until it stops fitting in
memory. ULID and UUIDv7 both begin with a millisecond timestamp, so consecutive inserts land next to
each other at the end of the index — the same access pattern an auto-incrementing integer gives
you, without needing the database to hand out the numbers.

You also get creation-time ordering for free: sorting the identifiers as plain strings puts the rows
in the order they were created, with no separate timestamp column and no secondary index.

---

## Install

```bash
npm install idora.js
```

The package is `idora.js` on npm (plain `idora` belongs to someone else); you import it as
`'idora.js'`.

- **Zero runtime dependencies.** Randomness comes from the Web Crypto API
  (`crypto.getRandomValues`) that every modern runtime already has.
- **Runs anywhere JavaScript does:** Node.js 20+, Deno, Bun, browsers, Cloudflare Workers and other
  edge runtimes. Nothing in the library imports a `node:` module.
- **ES module with TypeScript declarations included.** On Node.js 20.19+ and 22.12+ CommonJS code can
  `require('idora.js')` too.

---

## Quick start

Create one generator at start-up and share it:

```ts
// ids.ts
import { Idora } from 'idora.js';
import { Invoice, Order, User } from './entities.js';

export const ids = new Idora({
  entities: [
    [User, 'usr'],
    [Order, 'ord'],
    [Invoice, 'inv'],
  ],
});
```

Or declare the prefix on the class and name the module that holds them — see
[With a decorator](#with-a-decorator):

```ts
// entities.ts
import { entityPrefix } from 'idora.js';

@entityPrefix('usr')
export class User { /* ... */ }

// ids.ts
import { Idora } from 'idora.js';
import * as entities from './entities.js';

export const ids = new Idora({ modules: [entities] });
```

Then call it wherever you create an entity:

```ts
import { ids } from './ids.js';

export function placeOrder(cart: Cart): Order {
  return {
    id: ids.generate(Order), // 'ord_01k2f9v8tb9p5r2xnc7fkw3m0d'
    lines: cart.lines,
  };
}
```

That is the whole API for the common case. A generator is immutable once built, so export it from a
module, register it with your DI container as a singleton, and call it from anywhere.

---

## Configuration

Everything is passed to the `Idora` constructor once:

```ts
interface IdoraOptions {
  strategy?: 'ulid' | 'uuidv7';                         // default 'ulid'
  entities?: Iterable<[EntityType, string] | EntityType>; // explicit pairs, or decorated classes
  modules?: Iterable<EntityModule>;                     // module namespaces to collect decorated classes from
}
```

The constructor validates every registration as it goes and **throws straight away** on a mistake,
so a misconfiguration stops your process at start-up rather than surfacing later as a wrong prefix
in your database. See [Error handling](#error-handling).

### Registering entity prefixes

There are two ways to say that a `User` gets `usr`. They do the same thing, they can be mixed in one
generator, and both are read once, by the constructor, and never again.

Whichever you use, a prefix must be **1 to 16 ASCII letters or digits**. The underscore is excluded
because it separates the prefix from the value; allowing one inside a prefix would make an
identifier impossible to split back apart unambiguously.

The class is used purely as a key: Idora never constructs it and asks nothing of its shape, so it
can be an entity class, a plain data class or an abstract base. TypeScript interfaces and type
aliases do not exist at run time, so they cannot be keys — use the class that implements them, or
[`generateWithPrefix`](#the-two-generate-methods).

#### Explicitly

List `[Class, prefix]` pairs in `entities`. Any iterable works, a `Map` included:

```ts
const ids = new Idora({
  entities: [
    [User, 'usr'],
    [Order, 'ord'],
  ],
});
```

#### With a decorator

Past a handful of entities, that list stops reading as a single source of truth and starts reading
as a wall. `@entityPrefix` puts the prefix next to the class it describes:

```ts
import { entityPrefix } from 'idora.js';

@entityPrefix('usr')
export class User {
  constructor(readonly id: string) {}
}
```

Then either list the decorated classes on their own, or name the modules they live in:

| Option | Registers |
| --- | --- |
| `entities: [User, Order]` | Exactly the classes listed, taking each prefix from its decorator. |
| `modules: [entities]` | Every decorated class the module namespace exports — `import * as entities from './entities.js'`, or `await import('./entities.js')`. |
| `modules: [a, b, …]` | Several modules, in the order given. |

What the decorator declares goes into the same lookup as an explicit pair. **Nothing inspects your
classes once the generator is built**, so `generate(User)` costs exactly the same either way.

The decorator works with both standard (TC39) decorators — TypeScript 5+ with no flags — and
TypeScript's older `experimentalDecorators`. Without a compiler, call it as a function to the same
effect:

```js
class User {}
entityPrefix('usr')(User);
```

The declaration is **not inherited**: a subclass of `User` does not quietly take `usr` (which would
collide with `User` the moment both were registered). Decorate the subclass itself if it needs a
prefix of its own. Decorating one class twice throws on the spot.

Within one module, decorated classes are registered in ordinal order of their names, so a clash
between two of them is reported the same way round on every run, whatever order the exports happen
to be in. A class exported under two names — a named export and the default, say — is registered
once.

#### Why you name the modules

The obvious alternative is for the decorator to register the class into a global list the moment
the module loads, and have the generator read that list. It is one line shorter and considerably
worse:

- **It sees only what has already been imported.** Bundlers tree-shake, frameworks lazy-load, and
  module evaluation order changes with an unrelated refactor. An entity module nothing has imported
  by the time the generator is built is skipped in silence. You do not find out at start-up; you
  find out from an `EntityPrefixNotRegisteredError` on whichever request first creates that kind of
  entity — in production, on a code path that happened to load in a different order locally.
- **It is not your registry any more.** Any package that uses `idora.js` could register a prefix into
  your application, or collide with one of yours and stop it starting.
- **It is global state.** Two generators — two services in one process, or two tests running side by
  side — could no longer have different registrations.

Naming the modules turns all three into problems you cannot have, and it keeps the decorator itself
free of side effects.

#### Mixing the two

Both styles write into the same registry, so you can decorate the classes you own and register the
rest explicitly:

```ts
const ids = new Idora({
  modules: [entities],                    // your domain
  entities: [[ThirdPartyThing, 'tpt']],   // a class you cannot decorate
});
```

Explicit `entities` are registered first, then `modules`. The collision rules do not care where a
registration came from. Two decorators claiming `usr`, a decorator and a pair claiming `usr`, or the
same class registered both ways all throw a `DuplicateEntityPrefixError` — the last one **even if
both agree on the prefix**, because one class with two sources of truth is a mistake worth hearing
about.

### Choosing the value format

ULID is the default. Pass `strategy: 'uuidv7'` to switch:

```ts
const ids = new Idora({
  strategy: 'uuidv7',
  entities: [[User, 'usr']],
});
```

|  | **ULID** (default) | **UUIDv7** |
| --- | --- | --- |
| Length | 26 characters | 32 characters |
| Alphabet | Crockford Base32 (`0-9`, `a-z` less `i l o u`) | hexadecimal (`0-9`, `a-f`) |
| Example | `01k2f9v8ta7m3x0qbrn4dzeh5c` | `0195e7b4a1c87f3d9e2b6c4a8f01d5e3` |
| Entropy | 80 bits | 74 bits |
| Standardised as | a community spec | RFC 9562 |
| Fits a `uuid` column | no | yes |
| Ordering within one millisecond | strictly increasing | unordered |

**Use the ULID default unless something forces your hand.** It holds the same 128 bits in six fewer
characters, which matters once the identifier is a primary key repeated across every foreign key,
index and log line. Its alphabet also omits `i`, `l`, `o` and `u`, the characters people
mis-transcribe when reading an identifier aloud or copying it off a screenshot.

**Choose UUIDv7 when something outside your control expects a real UUID**: a `uuid`-typed database
column, a schema that validates the format, or another service that parses the value. The value is
a conformant version 7 UUID printed without hyphens; add them back at positions 8, 12, 16 and 20 if
something insists on the canonical form.

---

## The two `generate` methods

```ts
ids.generate(User);           // uses the prefix User was registered with
ids.generateWithPrefix('tmp'); // uses the prefix you pass in
```

**Prefer `generate(Class)`.** The prefix lives in one place — your start-up configuration — instead
of being repeated as a string literal at every call site, which means a typo is impossible and
renaming a prefix is a one-line change. Calling it for a class you forgot to register throws
immediately rather than quietly producing something wrong:

```ts
ids.generate(User);    // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
ids.generate(Comment); // throws EntityPrefixNotRegisteredError — Comment was never registered
```

Idora never guesses a prefix from the class name. A name is not stable — a refactor renames it, a
minifier mangles it — and an identifier's prefix must outlive both.

`generateWithPrefix(string)` exists for the cases where the prefix genuinely is not known until run
time — a plugin's own types, or a prefix read from configuration:

```ts
ids.generateWithPrefix('tmp'); // 'tmp_01k2f9v8ta7m3x0qbrn4dzeh5c'
```

It validates the prefix on every call, which costs a few nanoseconds. `generate` validated once, at
start-up.

### Reading prefixes back

`ids.registry` exposes what was registered, for when you need the prefix itself rather than a
whole identifier:

```ts
ids.registry.getPrefix(Order); // 'ord'
```

To check that an incoming identifier belongs to the entity a route expects, use
[`ids.isIdFor(Order, id)`](#reading-identifiers-back) rather than comparing prefixes by hand: it
checks the whole identifier, not just its first few characters.

| Member | Returns |
| --- | --- |
| `registry.getPrefix(Class)` | The prefix, or throws `EntityPrefixNotRegisteredError` |
| `registry.findPrefix(Class)` | The prefix, or `undefined` |
| `registry.has(Class)` | Whether the class is registered |
| `registry.registrations` | A read-only `Map` of every class and its prefix, in registration order |
| `registry.parse(Class, text)`, `tryParse`, `isIdFor` | The same as [`ids.parse`](#for-a-registered-entity-type) and friends |

The `registrations` view has no mutators at all — not just in its type, but at run time — so the
registry cannot be changed once built.

---

## Reading identifiers back

Identifiers come back — in a route, a message, a foreign key — and need checking, reading and
sorting. `IdoraId` does all three without you re-implementing the format:

```ts
import { IdoraId } from 'idora.js';

const id = IdoraId.parse('usr_01k226e0g0008j4ct4ank7f24s');

id.prefix;      // 'usr'
id.value;       // '01k226e0g0008j4ct4ank7f24s'
id.format;      // 'ulid'
id.timestampMs; // 1754568000000
id.createdAt;   // 2025-08-07T12:00:00.000Z
id.text;        // 'usr_01k226e0g0008j4ct4ank7f24s', which is also what String(id) and JSON give
```

Any identifier any Idora package minted parses, in either format, whatever strategy your own
generator uses. A parsed identifier is an immutable value; `createdAt` is a fresh `Date` on every
read, because a `Date` can be changed and an `IdoraId` cannot.

### Parsing

| Method | On a bad identifier |
| --- | --- |
| `IdoraId.parse(text, expectedPrefix?)` | Throws `InvalidIdError` |
| `IdoraId.tryParse(text, expectedPrefix?)` | Returns `undefined` |
| `IdoraId.isValid(text, expectedPrefix?)` | Returns `false` |

With an `expectedPrefix`, an identifier with any other prefix is rejected too. Prefixes are
compared **exactly**, case included, so `USR_01k2…` is not a `usr` identifier.

Parsing is **strict**: it accepts exactly the text a generator emits and nothing else. There is no
trimming, no case folding and no tolerance for UUID hyphens, so `' usr_01k2…'`,
`'usr_01K226E0G0008J4CT4ANK7F24S'` and `'inv_01988467-0200-7011-a233-445566778899'` are all
rejected. A value that a lenient parser would quietly normalise is a value that is stored in two
spellings somewhere, and two spellings of one identifier are two identifiers to an index or a
`Map`. `tryParse` and `isValid` treat anything that is not a string as invalid; `parse` throws a
`TypeError` for it.

#### For a registered entity type

The generator knows each entity type's prefix, so it can check an identifier against it:

```ts
const user = ids.parse(User, request.params.id); // throws unless it is a usr_ identifier
ids.tryParse(User, request.params.id);           // or undefined
ids.isIdFor(User, request.params.id);            // or false
```

Asking about a class that was never registered throws `EntityPrefixNotRegisteredError`, from all
three — the mistake is in your code, not in the identifier, so it should not read as one.

#### A branded type for checked identifiers

`Id<'usr'>` is a string that has been checked: a well-formed identifier with the prefix `usr`. It
is an ordinary string at run time, so it costs nothing and serialises as itself, but the type
checker will not let an unchecked string stand in for it:

```ts
import { type Id, IdoraId } from 'idora.js';

function loadUser(id: Id<'usr'>) { /* ... */ }

loadUser(request.params.id);                           // compile error: just a string
if (IdoraId.isValid(request.params.id, 'usr')) {
  loadUser(request.params.id);                         // fine: narrowed to Id<'usr'>
}
loadUser(IdoraId.parse(request.params.id, 'usr').text); // also fine
```

`isValid` and `isIdFor` are type guards, and the `text` of an `IdoraId<'usr'>` is an `Id<'usr'>`.
Plain `Id` means a checked identifier with any prefix, which is what `ids.isIdFor(User, …)`
narrows to: the prefix registered for `User` is only known at run time.

### Ordering and comparing

Parsed identifiers have a natural order that is **chronological**: by timestamp, then by value,
then by prefix. That puts identifiers of every entity type, in both formats, in the order they were
made — an order created after a user sorts after that user, although `ord` sorts before `usr` as
text. For the ULIDs one generator mints, it is exactly generation order.

```ts
events.sort(IdoraId.compare);      // oldest first; hand it straight to sort
order.id.isAfter(user.id);         // strictly later in the natural order
a.equals(b);                       // the same identifier
['ord_…', 'usr_…'].sort(compareIds); // the same order, for strings you have not parsed
```

| Member | Answers |
| --- | --- |
| `IdoraId.compare(a, b)`, `a.compareTo(b)` | Negative, zero or positive, in the natural order |
| `a.isBefore(b)`, `a.isAfter(b)` | Strictly before or after, in the natural order |
| `a.equals(b)` | Whether the texts are identical; there is no other notion of equality |
| `a.compareTimestamp(b)` | The millisecond only: zero for two minted in the same millisecond |
| `a.isSameMillisecond(b)` | Whether both were minted in the same millisecond |
| `a.isCreatedBefore(instant)`, `a.isCreatedAfter(instant)` | Strictly before or after a `Date` or epoch milliseconds, to the millisecond |
| `a.hasPrefix('usr')` | Whether the prefix is exactly `usr`, case included; narrows the type |
| `compareIds(a, b)` | `IdoraId.compare` for two strings, parsing both on every call |

`a < b` deliberately does not work: it would compare the texts, which is not the natural order as
soon as prefixes or formats differ, so converting an `IdoraId` to a number throws a `TypeError`
pointing you at `compare`. To key a `Map` or `Set` by identifier, use its `text`.

### Querying a span of time

An identifier starts with the time it was minted, so every identifier minted for one entity type
during one span of time is a contiguous range of strings. The bounds of that range select it
without parsing a row, and with the primary key's index:

```ts
const from = ids.lowerBound(User, lastMonday); // the lowest identifier any generator could mint
const to = ids.upperBound(User, now);          // the highest
// SELECT * FROM users WHERE id >= $1 AND id <= $2   — with from.text and to.text
```

`ids.lowerBound` and `ids.upperBound` use the generator's own strategy. For another prefix or
format there are `IdoraId.lowerBound(prefix, time, format?)` and `IdoraId.upperBound(...)`, and
`IdoraId.lowerBoundValue(time, format?)` and `upperBoundValue` for a column that stores values
without their prefix. The bounds are themselves valid identifiers, inclusive at both ends, and
compare correctly against identifiers of the **same prefix and format** — the shape of a database
column, which holds one entity type in one format. A time before 1970 or after the year 10889,
the range a 48-bit timestamp covers, throws a `RangeError`.

The database compares them with the column's collation, which must order the digits before the lowercase
letters character by character: binary and ordinary linguistic collations do, but a numeric
("natural sort") collation does not, and returns nothing for the range. See
[§6.4 of the specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md#64-range-bounds).

The [Drizzle integration](#integrations) wraps this up as `createdBetween(users.id, from, to)`.

---

## What an identifier is made of

```
    usr _ 01k2f9v8ta 7m3x0qbrn4dzeh5c
    └┬┘ ┬ └────┬───┘ └───────┬───────┘
     │  │      │             │
     │  │      │             └─ 80 bits of randomness, 16 characters
     │  │      └─ 48-bit millisecond timestamp, 10 characters
     │  └─ separator, always a single underscore
     └─ the prefix you registered, 1–16 characters
```

The timestamp comes first, which is what makes the identifiers sort by creation time as plain
strings. UUIDv7 has the same shape: 48 bits of timestamp, then randomness, printed as 32 hexadecimal
characters instead of 26 Base32 ones.

### Sizing your database column

| | ULID | UUIDv7 |
| --- | --- | --- |
| Value | 26 | 32 |
| Separator | 1 | 1 |
| Prefix | 1–16 | 1–16 |
| **Total** | **28–43** | **34–49** |

For a typical three-character prefix that is **30 characters** with ULID and **36** with UUIDv7.
Every identifier for a given entity is exactly the same length, so a fixed-width column is the right
choice — `char(30)` on PostgreSQL or MySQL. The characters are all ASCII, so a single-byte character
set is enough. `ids.valueLength` gives you the 26 or 32 programmatically.

---

## Error handling

Every error Idora throws derives from `IdoraError`, so you can catch that one class if you do not
care which went wrong.

| Error | Thrown when | Thrown at | Fix |
| --- | --- | --- | --- |
| `InvalidEntityPrefixError` | A prefix is empty, longer than 16 characters, or contains something other than an ASCII letter or digit | Construction, or the `generateWithPrefix` call | Use 1–16 letters and digits, no underscore |
| `DuplicateEntityPrefixError` | The same prefix is registered for two classes, or one class is registered twice — by two pairs, a pair and a decorator, or the same module listed twice | Construction | Give each class its own prefix, and register it exactly once |
| `EntityPrefixNotRegisteredError` | `generate(Class)`, `registry.getPrefix(Class)` or a registry-aware parse is called for a class with no registered prefix | The call | Register the class, or use `generateWithPrefix` or `IdoraId.parse` with an explicit prefix |
| `InvalidIdError` | `IdoraId.parse` or `ids.parse` is given text that is not a well-formed identifier, or has the wrong prefix | The call | Validate input with `tryParse` or `isValid` where it may be malformed |

A prefix that is not a string at all — `undefined` from plain JavaScript, say — throws a
`TypeError`, as any JavaScript API does for an argument of the wrong type.

The first two are start-up failures by design. A duplicate prefix defeats the whole point — you
could no longer tell from `acc_01k2f9…` whether you were looking at an `Account` or an `Accrual` —
so the generator refuses to be built rather than let it through. Prefixes are compared **ignoring
case**, so `usr` and `USR` count as a collision.

When a bad prefix comes from an `@entityPrefix` decorator, the message names the class carrying it.
An explicit pair leaves a stack trace pointing at the line you wrote; a decorator leaves nothing,
and *"contains the character '!' at index 3"* is not much help across two hundred classes.

Each error carries the details as properties, not just in the message:

```ts
import { DuplicateEntityPrefixError, IdoraError, InvalidEntityPrefixError, InvalidIdError } from 'idora.js';

try {
  new Idora({ entities: [[Account, 'acc'], [Accrual, 'acc']] });
} catch (error) {
  if (error instanceof DuplicateEntityPrefixError) {
    error.prefix;                // 'acc'
    error.entityType;            // Accrual
    error.conflictingEntityType; // Account
  }
}

try {
  ids.generateWithPrefix('user_account');
} catch (error) {
  if (error instanceof InvalidEntityPrefixError) {
    error.reason; // 'separator'  ('empty' | 'tooLong' | 'separator' | 'character')
    error.index;  // 4
  }
}

try {
  ids.parse(User, 'ord_01k226e0g0008j4ct4ank7f24s');
} catch (error) {
  if (error instanceof InvalidIdError) {
    error.reason;         // 'prefixMismatch'  ('noSeparator' | 'prefix' | 'value' | 'prefixMismatch')
    error.text;           // 'ord_01k226e0g0008j4ct4ank7f24s'
    error.expectedPrefix; // 'usr'
    error.entityType;     // User
  }
}
```

An `InvalidIdError` for the `prefix` reason carries the `InvalidEntityPrefixError` describing what
is wrong with the prefix as its standard `cause`. Its message quotes at most the first 64
characters of the text, since the text is usually somebody else's input.

---

## Performance

Generating an identifier costs about **50 nanoseconds** — half what `crypto.randomUUID()` costs,
for an identifier that carries a prefix and sorts by time. Measured on Node.js 22, Apple M3 Max:

| Method | Mean | Ratio |
| --- | ---: | ---: |
| `crypto.randomUUID()` *(baseline)* | 95.9 ns | 1.00 |
| `'cus_' + randomUUID().replaceAll('-', '')` | 251.6 ns | 2.62 |
| **`generate(Customer)` — ULID** | **48.0 ns** | **0.50** |
| **`generateWithPrefix('cus')` — ULID** | **49.4 ns** | **0.51** |
| **`generate(Customer)` — UUIDv7** | **80.3 ns** | **0.84** |
| **`generateWithPrefix('cus')` — UUIDv7** | **83.3 ns** | **0.87** |

Reproduce them with `npm run bench`. The harness is a page of code in
[`bench/generation.ts`](bench/generation.ts) — warm-up, fifteen samples of 200,000 calls, median
reported — rather than a benchmarking dependency.

Three things get it there:

- **Buffered entropy.** Asking the operating system for random bytes is the expensive part of
  making an identifier. Idora fills a 1 KiB block from `crypto.getRandomValues` at a time and hands
  out ten-byte slices of it, refilling roughly every hundred identifiers. The bytes come from the
  same CSPRNG either way; only the cost of fetching them is amortised.
- **Flat strings, built once.** The random field is kept as sixteen 5-bit digits and written out
  with a single `String.fromCharCode` call; the timestamp's ten characters are encoded once per
  millisecond and reused. There is no per-character concatenation and no intermediate array.
- **A precomputed lookup.** Each registered prefix is stored with its separator already appended,
  so `generate(User)` is one `Map` lookup and one concatenation.

**Concurrency.** JavaScript runs one thread per realm, so the generator needs no locks. Worker
threads, web workers and isolates each load their own copy of the module, with their own monotonic
stream and entropy block, so they never contend with one another.

---

## Questions you might reasonably have

**How strong is the ordering guarantee?**
Identifiers are ordered by the millisecond they were created in, globally, across processes and
machines. Within a single millisecond, every ULID created in one realm — one Node.js process's main
thread, one worker, one browser tab — is *strictly* increasing, whichever generator made it: the
random field is incremented rather than redrawn, per the ULID spec's monotonic mode. ULIDs from
different workers within the same millisecond, and UUIDv7s within the same millisecond, are in no
particular order relative to each other.

**Do identifiers stay in order across servers in different timezones?**
Yes. Both ULID and UUIDv7 define their timestamp as milliseconds since the Unix epoch, an absolute
instant with no zone attached, and Idora reads it from `Date.now()`, which is UTC by definition. A
server in Sydney and one in Los Angeles that agree on the instant produce identifiers that
interleave in true creation order. What *does* affect cross-server ordering is **clock skew**:
ordering between two machines is only as accurate as the gap between their clocks, typically
single-digit milliseconds under NTP. Ordering *within* one realm is always exact, because the
monotonic counter never regresses.

**What if the clock goes backwards?**
An NTP correction, a laptop waking from sleep or a resumed VM snapshot will not produce an
out-of-order ULID. The generator keeps the previous millisecond rather than accepting the earlier
reading, so the sequence never regresses; identifiers made during that window carry a timestamp a
few milliseconds ahead of the wall clock, which is the lesser evil.

**Could two identifiers collide?**
Not in any practical sense. Within a realm, collision is impossible by construction — the counter
increments. Across workers or machines, two identifiers collide only if 80 bits (ULID) or 74 bits
(UUIDv7) of cryptographically strong randomness coincide inside the same millisecond.

**Are they unguessable? Can I use one as a security token?**
The random portion is cryptographically strong, so an identifier cannot be predicted from the ones
around it. But the timestamp portion is plainly readable, and identifiers are meant to appear in
logs, URLs and support tickets. **Treat them as public.** Authorise every request properly; never use
an identifier as a bearer token or a password-reset secret.

**How do I split an identifier back apart?**
Parse it: `IdoraId.parse(id)` gives you the `prefix`, the `value` and the time it was minted, and
checks the identifier on the way — see [Reading identifiers back](#reading-identifiers-back). By
hand, split on the first underscore; the prefix contains no underscores, by rule, so this is
unambiguous.

**Does it work with minified or bundled code?**
Yes. Registration is keyed by the class object itself, never by its name, so minification cannot
change a prefix. Class names appear only in error messages. The package is marked side-effect free,
so bundlers drop whatever you do not import.

**Can I use these as keys in Prisma, Drizzle, TypeORM or MikroORM?**
Yes — they are ordinary strings. Assign one when you create the entity rather than letting the
database generate the key. Because the values are time-ordered, B-tree inserts stay at the end of
the index, which is exactly the behaviour you want and the reason not to use `randomUUID()`. For
Drizzle and Prisma there are [packages that do the assigning for you](#integrations).

**Are identifiers case-sensitive?**
Compare them exactly, with `===`. Generated values are always lowercase, and neither alphabet
contains two characters that differ only by case, so the value half is effectively
case-insensitive anyway. Your prefix is emitted exactly as you registered it, so registering `USR`
gives you `USR_01k2f9…`.

**Why is the maximum prefix 16 characters?**
Long enough for a readable abbreviation, short enough that the prefix never dominates the
identifier. If you find yourself wanting more, the prefix is probably trying to be a description.

**Which registration style should I use?**
Explicit pairs if you have a handful of entities, or if your build does not support decorators.
`@entityPrefix` once the list is long enough that keeping it in step with the domain is its own
chore. There is no penalty for mixing them, and no run-time difference between them at all — both
end up as entries in the same `Map` before your first identifier is generated.

**Can I override a decorated prefix with an explicit pair?**
No, that throws. It would mean a class whose prefix depends on the order two lines ran in, and the
usual reason to want it — a copy-paste slip — is exactly what the error is there to catch. If you
need a different prefix, change the decorator.

---

## Integrations

Separate packages, so that `idora.js` itself stays free of dependencies. Each one is released with
`idora.js` at the same version and takes both it and the library it integrates as peer
dependencies, so your application never carries a second copy of either.

| Package | For | What it gives you |
| --- | --- | --- |
| [`idora.js-zod`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS/extensions/zod) | Zod 4.1+ | `idoraId({ prefix: 'usr' })`, a schema that checks identifiers with a precise message and outputs `Id<'usr'>`; `parsedIdoraId()`, a codec to and from `IdoraId` |
| [`idora.js-drizzle`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS/extensions/drizzle) | Drizzle ORM 0.44+ | `idoraPrimaryKey(ids, User)`, a correctly sized key column that fills itself in, for PostgreSQL, MySQL and SQLite; `createdBetween(users.id, from, to)` and friends, filters that use the index |
| [`idora.js-prisma`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS/extensions/prisma) | Prisma 6 and 7 | `idoraExtension({ ids, models: { user: User } })`, a client extension that fills in a missing `id` on every create |

```bash
npm install idora.js idora.js-zod zod
```

---

## Contributing

From the `Idora.JS` folder:

```bash
npm ci                          # installs the dev dependencies, the extensions' included
npm run build                   # compiles src/ to dist/, with declarations
npm test                        # compiles and runs the suite with node:test
npm run bench                   # the benchmark above

npm run build --workspaces      # the extension packages in extensions/, against the dist/ above
npm test --workspaces           # and their suites
```

The packages in `extensions/` are npm workspaces of this folder, linked to the `idora.js` being
built here rather than to a published one, so a change to the core and the extensions that use it
are tested together. None of them is part of the `idora.js` package itself, which still contains
only `dist/`, this README and the licence.

The suite uses Node.js's built-in test runner and assertion library, so it adds nothing to the
dependency tree. Tests are written Arrange-Act-Assert and cover the success, failure and edge
cases of every public member, plus the encoders, the monotonic stream and the entropy pool
directly. `test/conformance.test.ts` runs the shared cross-language vectors in
[`spec/test-vectors.json`](https://github.com/SrKleRAimBot/Idora/blob/main/spec/test-vectors.json),
which every Idora package must reproduce exactly; the behaviour they pin down is set out in the
[specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md). CI runs the
suite with `TZ=Australia/Sydney`, so a local-time bug cannot hide behind a runner set to UTC.

## Releasing

Pushing an `Idora.JS/v*` tag builds, tests, verifies the package contents and publishes
`idora.js` and every extension package to npm, at the tag's version, with [provenance](https://docs.npmjs.com/generating-provenance-statements), using npm's
[trusted publishing](https://docs.npmjs.com/trusted-publishers) — the workflow authenticates with a
short-lived GitHub OIDC token, so there is no long-lived npm token to store or rotate:

```bash
git tag Idora.JS/v1.0.0
git push origin Idora.JS/v1.0.0
```

The published version comes from the tag, not from `package.json`, and each extension's peer
dependency on `idora.js` is set to `^` that version, since that is the one it was tested with. A
version already on npm is skipped, so a release that stopped part-way can be finished by running
the workflow again. Running it manually from the Actions tab is a dry run: everything except the
publish.

A prerelease is published under the `next` dist-tag. A stable version becomes `latest` unless the
package's `latest` is already newer — a fix released on an older line — in which case it goes under
`maintenance-<major>.<minor>`, so a fresh `npm install` never moves backwards.

### Releasing a new package

npm configures trusted publishing per package, on the package's own settings page, which exists only
once the package does. So a new package — when this was written, `idora.js-zod`,
`idora.js-drizzle` and `idora.js-prisma` — has its first version published by hand. The workflow
checks every package before it publishes anything, and stops with nothing published if one has
never been released:

1. Push the tag. The workflow stops at the check, with nothing published.
2. From the tagged commit, with `npm login` as an owner, run `npm ci`. If the `package.json`
   versions are not already the tag's, stamp them as the workflow's "Stamp the version from the
   tag" step does, which also sets each extension's `idora.js` peer range. Then build with
   `npm run build && npm run build --workspaces`, and publish each new package with
   `npm publish -w extensions/<name> --access public`. That first version has no provenance.
3. On npmjs.com, open each new package's settings and add a trusted publisher: GitHub Actions,
   repository `SrKleRAimBot/Idora`, workflow `release-js.yml`.
4. Re-run the workflow. It skips what is already published, publishes the rest, and every later
   release runs from the tag alone.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
