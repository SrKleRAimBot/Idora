# Idora.NET

Entity identifiers that tell you what they are, and sort by when they were made.

> Part of [**Idora**](https://github.com/SrKleRAimBot/Idora) — the same identifiers, with the same
> rules, for [.NET](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET), [JavaScript/TypeScript](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS),
> [Java](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Java), [Go](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Go) and [Rust](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Rust). Formerly published as `UserFriendlyIds`; see
> [Migrating from UserFriendlyIds](#migrating-from-userfriendlyids).

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, no dependencies beyond the DI abstractions. When identifiers come back — in a
route, a message, a foreign key — `IdoraId` parses, validates and orders them, whichever Idora
package minted them.

---

## Why not just use a GUID?

Two reasons, and they compound.

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` turns up in
a log line, a stack trace or a customer's email, you cannot tell whether it is a user, an order or
an invoice. You go and look. With `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` you already know, and so does
everybody else on the team. This is why Stripe, GitHub and others put prefixes on their public
identifiers.

**A random identifier is hostile to your database.** `Guid.NewGuid()` produces values scattered
uniformly across the keyspace, so every insert lands in a different page of the index. Pages split,
the index fragments, and the working set grows until it stops fitting in memory. ULID and UUIDv7
both begin with a millisecond timestamp, so consecutive inserts land next to each other at the end
of the index — the same access pattern an auto-incrementing integer gives you, without needing the
database to hand out the numbers.

You also get creation-time ordering for free: sorting the identifiers as plain strings puts the rows
in the order they were created, with no separate timestamp column and no secondary index.

---

## Install

```bash
dotnet add package Idora.NET
```

Targets `net8.0`, `net9.0` and `net10.0`. The only dependency is
`Microsoft.Extensions.DependencyInjection.Abstractions`, so that `AddIdora` can extend
`IServiceCollection`; nothing else is pulled in.

The package is `Idora.NET`; the namespace and assembly are plain `Idora`.

Two companion packages, versioned and released with it, plug identifiers into the frameworks you
are most likely to store and serve them with:

| Package | Adds |
| --- | --- |
| [`Idora.NET.EntityFrameworkCore`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET/src/Idora.EntityFrameworkCore) | Keys generated on insert with the entity's prefix, `IdoraId` columns, and creation-time range queries that use the primary key's index |
| [`Idora.NET.AspNetCore`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET/src/Idora.AspNetCore) | An `{id:idora(usr)}` route constraint and `[IdoraId]` / `[IdoraIdFor<User>]` validation attributes |

Neither is needed to bind an `IdoraId` in ASP.NET Core or to serialise one as JSON; the core package
does both on its own. See [Reading identifiers back](#reading-identifiers-back).

---

## Quick start

Register your entity prefixes once, at start-up:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddIdora(options =>
{
    options.RegisterEntity<User>("usr");
    options.RegisterEntity<Order>("ord");
    options.RegisterEntity<Invoice>("inv");
});
```

Or declare the prefix on the entity and collect them in one line — see
[With an attribute](#with-an-attribute):

```csharp
using Idora;

[EntityPrefix("usr")]
public sealed class User { /* ... */ }

builder.Services.AddIdora(options => options.RegisterEntitiesFromAssemblyContaining<User>());
```

Then inject `IIdoraGenerator` wherever you create an entity:

```csharp
using Idora;

public sealed class OrderService(IIdoraGenerator ids)
{
    public Order PlaceOrder(Cart cart) => new()
    {
        Id = ids.Generate<Order>(),   // "ord_01k2f9v8tb9p5r2xnc7fkw3m0d"
        Lines = cart.Lines,
    };
}
```

That is the whole API for the common case. The generator is a thread-safe singleton, so you can
inject it anywhere and call it from anywhere.

---

## Configuration

### Registering entity prefixes

There are two ways to say that a `User` gets `usr`. They do the same thing, they can be mixed in one
application, and both are read once at start-up and never again.

Whichever you use, a prefix must be **1 to 16 ASCII letters or digits**. The underscore is excluded
because it separates the prefix from the value; allowing one inside a prefix would make an
identifier impossible to split back apart unambiguously. Registration is validated as it happens, so
a mistake stops the process at start-up rather than surfacing later as a wrong prefix in your
database. See [Error handling](#error-handling).

#### Fluently

`RegisterEntity<TEntity>` maps a type to the prefix its identifiers will carry. The type is used
purely as a key — it needs no base class and no interface.

```csharp
services.AddIdora(options =>
{
    options.RegisterEntity<User>("usr");
    options.RegisterEntity<Order>("ord");
});
```

There is also a non-generic overload, `RegisterEntity(Type, string)`, for configuration driven by
reflection.

#### With an attribute

Past a handful of entities, that list stops reading as a single source of truth and starts reading
as a wall. `[EntityPrefix]` puts the prefix next to the type it describes:

```csharp
[EntityPrefix("usr")]
public sealed class User
{
    public required string Id { get; init; }
}
```

and you name the assemblies to collect them from:

```csharp
services.AddIdora(options => options.RegisterEntitiesFromAssemblyContaining<User>());
```

| Method | Scans |
| --- | --- |
| `RegisterEntitiesFromAssemblyContaining<TMarker>()` | The assembly that declares `TMarker`. Point it at one of your entities. |
| `RegisterEntitiesFromAssembly(assembly)` | One assembly. |
| `RegisterEntitiesFromAssemblies(a, b, …)` | Several, in the order given. |

The scan happens once, during start-up, and what it finds goes into the same frozen lookup the
fluent registrations do. **Nothing reflects over anything once the application is running**, so
`Generate<T>()` costs exactly what it did before.

`internal` and nested types are found — an entity does not have to be public to have a prefix. The
attribute works on a class, a struct or an interface, and it is **not inherited**, so a subclass
never quietly takes its base's prefix.

#### Why you name the assemblies

The obvious alternative is for the library to walk `AppDomain.CurrentDomain.GetAssemblies()` and
find your entities for you. It is one line shorter and considerably worse:

- **It sees only what is already loaded.** Assemblies load lazily, so an entity assembly nothing has
  touched by the time `AddIdora` runs is skipped in silence. You do not find out at
  start-up; you find out from an `EntityPrefixNotRegisteredException` on whichever request first
  creates that kind of entity — which may well be in production, on a code path that happened to run
  in a different order locally.
- **It walks everything.** Every type in every transitive package and in the BCL, on every boot, for
  a feature whose entire premise is that it costs nothing at run time.
- **It is not your registry any more.** Any package that references this library could register a
  prefix into your application, or collide with one of yours and stop it starting.

Naming the assemblies turns all three into a problem you cannot have. It also keeps the reflective
surface small enough to be honest about — see [Trimming and AOT](#trimming-and-aot).

#### Mixing the two

Both styles write into the same registry, so you can attribute the entities you own and register the
rest fluently:

```csharp
services.AddIdora(options =>
{
    options.RegisterEntitiesFromAssemblyContaining<User>();  // your domain
    options.RegisterEntity<ThirdPartyThing>("tpt");          // a type you cannot annotate
});
```

The collision rules do not care where a registration came from. Two attributes claiming `usr`, an
attribute and a `RegisterEntity` call claiming `usr`, or the same type declared both ways all fail
at start-up with a `DuplicateEntityPrefixException` — the last one **even if both agree on the
prefix**, because one type with two sources of truth is a mistake worth hearing about.

### Choosing the value format

ULID is the default. Call `UseUuidV7()` to switch:

```csharp
services.AddIdora(options =>
{
    options.UseUuidV7();
    options.RegisterEntity<User>("usr");
});
```

|  | **ULID** (default) | **UUIDv7** |
| --- | --- | --- |
| Length | 26 characters | 32 characters |
| Alphabet | Crockford Base32 (`0-9`, `a-z` less `i l o u`) | hexadecimal (`0-9`, `a-f`) |
| Example | `01k2f9v8ta7m3x0qbrn4dzeh5c` | `0195e7b4a1c87f3d9e2b6c4a8f01d5e3` |
| Entropy | 80 bits | 74 bits |
| Standardised as | a community spec | RFC 9562 |
| Parses as a `Guid` | no | yes |
| Ordering within one millisecond | strictly increasing | unordered |

**Use the ULID default unless something forces your hand.** It holds the same 128 bits in six fewer
characters, which matters once the identifier is a primary key repeated across every foreign key,
index and log line. Its alphabet also omits `i`, `l`, `o` and `u`, the characters people
mis-transcribe when reading an identifier aloud or copying it off a screenshot.

**Choose UUIDv7 when something outside your control expects a real UUID**: a `uuid`-typed database
column, a schema that validates the format, or another service that parses the value. The value
round-trips through `Guid.Parse` despite having no hyphens.

---

## The two `Generate` methods

```csharp
string Generate<TEntity>();          // uses the prefix TEntity was registered with
string Generate(string prefix);      // uses the prefix you pass in
```

**Prefer `Generate<TEntity>()`.** The prefix lives in one place — your start-up configuration —
instead of being repeated as a string literal at every call site, which means a typo is impossible
and renaming a prefix is a one-line change. Calling it for a type you forgot to register throws
immediately rather than quietly producing something wrong:

```csharp
ids.Generate<User>();       // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
ids.Generate<Comment>();    // throws EntityPrefixNotRegisteredException — Comment was never registered
```

`Generate(string)` exists for the cases where the prefix genuinely is not known until run time — a
plugin's own types, or a prefix read from configuration:

```csharp
ids.Generate("tmp");        // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c"
```

It validates the prefix on every call, which costs a few nanoseconds. The generic overload validates
once, at start-up.

### Using it without a container

If a container would be overkill — a console tool, a migration script, a test — construct the
generator directly:

```csharp
var ids = new IdoraGenerator(options => options.RegisterEntity<User>("usr"));
string id = ids.Generate<User>();
```

### Reading prefixes back

`IEntityPrefixRegistry` is registered alongside the generator, for when you need the prefix itself
rather than a whole identifier — building a filter over stored identifiers, for instance:

```csharp
string orderPrefix = prefixes.GetPrefix<Order>();   // "ord"
```

It also offers `TryGetPrefix<TEntity>(out string?)` and a `Registrations` dictionary of everything
that was registered. To check that an identifier belongs to a particular entity, don't compare
prefixes by hand — use `prefixes.IsIdFor<Order>(id)`, which also checks that the rest of it is well
formed. See [Checking it is the right entity's](#checking-it-is-the-right-entitys).

---

## Reading identifiers back

Generation is half of the job. Identifiers come back — in a route, a message body, a foreign key
from another service — and `IdoraId` is the type that checks them, reads them and puts them in
order, so nobody re-implements the format with `Split('_')`.

```csharp
IdoraId id = IdoraId.Parse("usr_01k226e0g0008j4ct4ank7f24s");

id.Prefix;                  // "usr"
id.Value;                   // "01k226e0g0008j4ct4ank7f24s"
id.Format;                  // IdGenerationStrategy.Ulid
id.Timestamp;               // 2025-08-07T12:00:00.000+00:00
id.TimestampMilliseconds;   // 1754568000000
id.ToString();              // "usr_01k226e0g0008j4ct4ank7f24s", the very string that was parsed
```

It reads any conforming identifier — ULID or UUIDv7, whichever strategy your own generator uses,
minted by this package or by Idora for JavaScript, Java, Go or Rust. The timestamp comes out of the
identifier itself, so it is the same on every machine, in UTC, whatever the reader's clock or time
zone.

`IdoraId` is a small readonly struct that wraps the original string. **Parsing a string allocates
nothing**, and `ToString()` and the implicit conversion to `string` hand back the very same
instance, so an `IdoraId` can go anywhere a string identifier went before.

### Parsing

| Method | On bad input |
| --- | --- |
| `IdoraId.Parse(text)` | throws `InvalidIdException` |
| `IdoraId.TryParse(text, out IdoraId id)` | returns `false` |
| `IdoraId.IsValid(text)` | returns `false`, and allocates nothing |

Each takes an optional expected prefix — `IdoraId.Parse(text, "usr")` — and has an overload for
`ReadOnlySpan<char>`. `IdoraId` also implements `IParsable<T>` and `ISpanParsable<T>`, so generic
parsing code and framework binding find it without help.

Parsing is **strict**. It accepts exactly what a generator emits and nothing else: no trimming, no
case folding, no hyphens. That is deliberate. An identifier is a key, and a lenient parser that
accepted `USR_01K226…` or a value with a trailing space would hand you a value that matches nothing
in the database — or, worse, matches when it shouldn't. The rules, in the order they are checked:

| `InvalidIdReason` | The text… |
| --- | --- |
| `NoSeparator` | contains no `_` at all — often a bare ULID or UUID that has lost its prefix |
| `Prefix` | has a part before the first `_` that is not a valid prefix (the `InvalidEntityPrefixException` explaining why is the `InnerException`) |
| `Value` | has a part after it that is neither a 26-character lowercase ULID nor a 32-character lowercase UUIDv7 |
| `PrefixMismatch` | is well formed, but its prefix is not exactly the one you asked for, case included |

The exception carries the `Text`, the `Reason` and the `ExpectedPrefix`, and the message says
precisely what is wrong — *"it has the uppercase character 'K' at index 2"*, *"it is 25 characters
long, but a ULID is 26 characters and a UUIDv7 32"* — and what to do about it. A malformed
identifier is reported as malformed even if you also asked for a prefix: the mismatch is only
reported for an identifier that is otherwise valid.

### Checking it is the right entity's

The expected prefix is where most of the value is: an identifier that parses but belongs to the
wrong kind of entity is the bug a prefix exists to catch. Rather than repeat the prefix as a string,
ask the registry:

```csharp
public sealed class OrdersEndpoint(IEntityPrefixRegistry prefixes)
{
    public IResult Get(string id) =>
        prefixes.TryParseIdFor<Order>(id, out IdoraId orderId)
            ? Results.Ok(Load(orderId))
            : Results.NotFound();
}
```

`IsIdFor<TEntity>`, `ParseIdFor<TEntity>` and `TryParseIdFor<TEntity>` each have a `Type` overload
too. For a type with no registered prefix they throw `EntityPrefixNotRegisteredException` — even the
`Try` and `Is` forms — because then it is the configuration that is wrong, not the identifier.

`ids.GenerateId<User>()` is the other direction: a new identifier, as an `IdoraId`.

### Ordering

Two identifiers are equal when their text is ordinally equal. Their **natural order** is
chronological — by timestamp, then by value, then by prefix — and every comparison operator,
`CompareTo`, `List<IdoraId>.Sort()` and LINQ's `OrderBy` use it:

```csharp
IdoraId user  = ids.GenerateId<User>();    // usr_…
IdoraId order = ids.GenerateId<Order>();   // ord_…, a moment later

order > user;               // true: created later, even though "ord" < "usr" as text
order.IsAfter(user);        // the same thing, as a method
```

Ordering by timestamp first, rather than as plain text, is what lets identifiers of different
entity types and different formats interleave in the order they were made. Within one format the
value agrees with the timestamp, so ULIDs generated on one thread sort in exactly the order they
were generated.

For questions about time rather than order:

| Member | Answers |
| --- | --- |
| `a.CompareTimestamp(b)` | which was minted in the earlier millisecond, with `0` for the same one |
| `a.IsSameMillisecond(b)` | whether they were minted in the same millisecond |
| `id.IsCreatedBefore(instant)` | whether it was minted strictly before an instant, at millisecond precision |
| `id.IsCreatedAfter(instant)` | whether it was minted strictly after one |
| `id.HasPrefix("usr")` | whether the prefix is exactly `usr`, case included, without allocating |

### Querying by creation time

Because a value starts with its timestamp, every identifier minted for one prefix over a span of
time falls in one contiguous range of strings. `IdoraId.LowerBound` and `IdoraId.UpperBound` give
you its ends:

```csharp
DateTimeOffset day = new(2025, 8, 7, 0, 0, 0, TimeSpan.Zero);

string from = IdoraId.LowerBound("ord", day);                           // ord_01k20x7n000000000000000000
string to   = IdoraId.UpperBound("ord", day.AddDays(1).AddTicks(-1));   // ord_01k23fmbzzzzzzzzzzzzzzzzzz

// WHERE Id >= @from AND Id <= @to — answered from the primary key's index.
```

That selects every order created that day with two plain string comparisons — no `CreatedAt`
column, no secondary index, and no row parsed. The bounds are inclusive, cover the whole of each
end's millisecond, and are themselves valid identifiers. They compare correctly against identifiers
of the **same prefix and format**, which is exactly what a key column holds. The
[Entity Framework Core package](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET/src/Idora.EntityFrameworkCore)
wraps this as `WhereCreatedBetween(order => order.Id, "ord", from, to)`.

The database compares them with the column's collation, so give the column a binary or `C`
collation. A linguistic collation is safe only if it orders the identifier's characters exactly as
a byte comparison does, and many do not: Czech and Slovak treat `ch` as one letter, Danish `aa`,
Lithuanian sorts `y` before `j`, Estonian `z` before `t`, and a numeric ("natural sort")
collation compares runs of digits as numbers. Any of them can put a bound on the wrong side of a
row, so the range silently misses it. See
[§6.4 of the specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md#64-range-bounds).

`IdoraId.GetLength("usr", IdGenerationStrategy.Ulid)` is the exact length of every such identifier
(30), and `IdoraId.GetMaxLength(...)` the longest any prefix can produce (43 for ULIDs, 49 for
UUIDv7s) — the numbers to size a column with.

### JSON, binding and configuration

`IdoraId` carries a `System.Text.Json` converter and a `TypeConverter`, both in the box, so with no
setup at all it:

- serialises as a plain JSON string, `"usr_01k226e0g0008j4ct4ank7f24s"`, and as a dictionary key —
  with the reflection serializer and with source-generated `JsonSerializerContext`s alike;
- fails deserialisation of a malformed identifier with a `JsonException` that carries the
  `InvalidIdException` and the JSON path;
- binds from routes, query strings and headers in **minimal APIs** (through `IParsable<T>`) and
  **MVC** (through `IParsable<T>` and the `TypeConverter`), answering malformed input with a 400;
- binds from **configuration**: `configuration.Get<Settings>()` fills `IdoraId` properties.

```csharp
app.MapGet("/users/{id}", (IdoraId id) => ...);   // /users/nope is a 400, not a 500
```

### The empty identifier

`default(IdoraId)` — what an unassigned field holds — is the *empty* identifier, and never the
result of a successful parse. `IsEmpty` is `true`; `Prefix`, `Value` and `ToString()` are empty
strings; the timestamp is the Unix epoch and the format `Ulid`. It equals only itself and sorts
before every real identifier. In JSON it is written as `null`, and `null` reads back as it; use
`IdoraId?` where "absent" and "empty" need telling apart.

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
Since every identifier for a given entity is exactly the same length, a fixed-width column is the
right choice — `char(30)` on PostgreSQL, `char(30)` on SQL Server (or `varchar(30)` if you prefer;
the storage difference is a byte). The characters are all ASCII, so use a non-Unicode column type:
`char`, not `nchar`.

If you would rather store UUIDv7 identifiers in a native `uuid` column, strip the prefix and
`Guid.Parse` the remainder — the prefix is a property of the type, so you already know it from the
column you are reading.

---

## Error handling

Every exception derives from `IdoraException`, so you can catch that one type if you do
not care which went wrong.

| Exception | Thrown when | Thrown at | Fix |
| --- | --- | --- | --- |
| `InvalidEntityPrefixException` | A prefix is empty, longer than 16 characters, or contains something other than an ASCII letter or digit | Start-up, or on the `Generate(string)` call | Use 1–16 letters and digits, no underscore |
| `DuplicateEntityPrefixException` | The same prefix is registered for two entity types, or one entity type is registered twice — whether by two `RegisterEntity` calls, one of each style, or the same assembly being scanned twice | Start-up | Give each entity type its own prefix, and register it exactly once |
| `EntityPrefixNotRegisteredException` | `Generate<TEntity>()`, or a registry lookup such as `IsIdFor<TEntity>`, is called for a type with no registered prefix | On the call | Register the type, or use `Generate(string)` |
| `InvalidIdException` | Text that should be an identifier is not one, or has another entity's prefix; `Reason` says which rule failed | On the `Parse` call | Usually bad input — answer it with a 400, or use `TryParse` |

The first two are start-up failures by design; `InvalidIdException` is the opposite, a report about
input that is expected to be wrong now and then. A duplicate prefix defeats the whole point — you
could no longer tell from `acc_01k2f9…` whether you were looking at an `Account` or an `Accrual` —
so the process refuses to start rather than let it through. Prefixes are compared **ignoring case**,
so `usr` and `USR` count as a collision.

When a bad prefix comes from an `[EntityPrefix]` attribute, the message names the type carrying it.
A fluent registration leaves a stack trace pointing at the line you wrote; an attribute leaves
nothing, and *"contains the character '!' at index 3"* is not much help across two hundred classes.

Each exception carries the details as properties, not just in the message:

```csharp
try
{
    services.AddIdora(options =>
    {
        options.RegisterEntity<Account>("acc");
        options.RegisterEntity<Accrual>("acc");
    });
}
catch (DuplicateEntityPrefixException ex)
{
    ex.Prefix;                  // "acc"
    ex.EntityType;              // typeof(Accrual)
    ex.ConflictingEntityType;   // typeof(Account)
}
```

---

## Performance

Generating an identifier costs **one allocation** — the string that comes back — and about **30
nanoseconds**. Measured with BenchmarkDotNet on .NET 10, Apple M3 Max, against
`Guid.NewGuid().ToString()` as the baseline:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `Guid.NewGuid().ToString()` *(baseline)* | 212.3 ns | 1.00 | 96 B |
| `"cus_" + Guid.CreateVersion7().ToString("N")` | 237.3 ns | 1.12 | 184 B |
| **`Generate<T>()` — ULID** | **29.1 ns** | **0.14** | **88 B** |
| **`Generate(prefix)` — ULID** | **28.2 ns** | **0.13** | **88 B** |
| **`Generate<T>()` — UUIDv7** | **41.3 ns** | **0.19** | **96 B** |
| **`Generate(prefix)` — UUIDv7** | **39.5 ns** | **0.19** | **96 B** |

Reproduce them yourself with `dotnet run -c Release --project tests/Idora.Benchmarks -- --filter '*'`.

Three things get it there:

- **One allocation, always.** The prefix and the value are written straight into the buffer of the
  final string via `string.Create`. There is no `StringBuilder`, no concatenation and no
  intermediate array — the 88 bytes above are the string itself and nothing else, and there is a
  test that fails if that ever changes.
- **Buffered entropy.** Asking the operating system for random bytes costs around 240ns, which
  would dominate everything else put together. Each thread instead keeps a 1 KiB block of
  cryptographically strong entropy and draws from it, refilling roughly every hundred identifiers.
  This is also why UUIDv7 generation here is about 35 times faster than the framework's own
  `Guid.CreateVersion7()`, while producing a value that is conformant and indistinguishable from it.
- **A frozen lookup.** Entity prefixes go into a `FrozenDictionary` once at start-up, so resolving
  `Generate<User>()` to `"usr_"` is a couple of nanoseconds. Prefixes declared with
  `[EntityPrefix]` land in the same dictionary — the assembly scan happens during start-up and
  the numbers above are unchanged by it.

### Parsing and comparing

Parsing is as cheap, and allocates **nothing**: an `IdoraId` wraps the string it was parsed from.
Same machine and settings, against `Guid.TryParse` as the baseline:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `Guid.TryParse` *(baseline)* | 9.7 ns | 1.00 | - |
| **`IdoraId.Parse` — ULID** | **17.2 ns** | **1.77** | **-** |
| **`IdoraId.Parse` — UUIDv7** | **24.6 ns** | **2.53** | **-** |
| **`IdoraId.IsValid(text, prefix)`** | **17.1 ns** | **1.76** | **-** |
| **`IdoraId.CompareTo`** | **3.1 ns** | **0.32** | **-** |

That is a full check of the prefix and every character of the value, plus decoding the timestamp, in
under twenty nanoseconds — cheap enough to run on every request in a route constraint. Reproduce
with `dotnet run -c Release --project tests/Idora.Benchmarks -- --filter '*IdParsingBenchmarks*'`.

**Thread safety.** The generator is immutable and registered as a singleton; call it from as many
threads as you like. There are no locks anywhere — the monotonic counter and the entropy buffer are
both thread-local — so throughput scales with cores rather than contending.

---

## Trimming and AOT

The package is marked `IsTrimmable` and `IsAotCompatible`, and everything in it is trim-safe **except
the three assembly-scanning methods**, which are annotated `[RequiresUnreferencedCode]`. The trimmer
cannot see a type that is only ever found by reflection, so it is free to remove one — and a removed
entity type is a prefix that silently goes missing.

That annotation is the whole reason assemblies are named explicitly rather than discovered: it keeps
the reflective surface down to three methods you opted into, instead of spreading it across the
package.

In practice:

- **If you do not publish trimmed or AOT** — the overwhelming majority of applications — this
  changes nothing and you will never see a warning.
- **If you do**, register with `RegisterEntity<TEntity>` and you stay completely clean. Use a scan
  and you get one `IL2026` warning, on your own line, telling you which method caused it.

```
warning IL2026: Using member 'RegisterEntitiesFromAssemblyContaining<TMarker>()' which has
'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code.
```

There is no third option where scanning is both convenient and trim-safe. Reflection over types the
compiler never sees is exactly what trimming cannot support.

---

## Questions you might reasonably have

**How strong is the ordering guarantee?**
Identifiers are ordered by the millisecond they were created in, globally, across threads and
processes. Within a single millisecond, ULIDs generated on one thread are *strictly* increasing —
the random field is incremented rather than redrawn, per the ULID spec's monotonic mode. ULIDs from
different threads within the same millisecond, and UUIDv7s within the same millisecond, are in no
particular order relative to each other. Guaranteeing more than that would need a process-wide lock,
which would cost more than the ordering is worth.

**Do identifiers stay in order across servers in different timezones?**
Yes. Timezones are irrelevant here: both ULID and UUIDv7 define their timestamp as milliseconds
since the Unix epoch, which is an absolute instant with no zone attached, and this package reads it
from `DateTime.UtcNow`. A server in Sydney and a server in Los Angeles that agree on the instant
produce identifiers that interleave in true creation order, and that order survives being sorted in
a database that knows nothing about where the rows came from. There is nothing to configure, and no
`TimeZoneInfo` anywhere in the library.

What *does* affect cross-server ordering is **clock skew** — servers disagreeing about what the
instant is. Ordering between two machines is only as accurate as the gap between their clocks:
with typical NTP synchronisation that is single-digit milliseconds, so identifiers created seconds
apart on different hosts order correctly, while two created within a few milliseconds of each other
may not. This is a property of distributed timestamps in general, not of this package; if you need
a total order across machines that does not depend on their clocks, you need a sequencer, not an
identifier format. Note that ordering *within* one process is always exact regardless of skew,
because the monotonic counter never regresses.

**What if the clock goes backwards?**
An NTP correction or a resumed VM snapshot will not produce an out-of-order ULID. The generator
keeps the previous millisecond rather than accepting the earlier reading, so the sequence never
regresses; identifiers made during that window carry a timestamp a few milliseconds ahead of the
wall clock, which is the lesser evil.

**Could two identifiers collide?**
Not in any practical sense. Within a millisecond on one thread, collision is impossible by
construction — the counter increments. Across threads, two identifiers collide only if 80 bits
(ULID) or 74 bits (UUIDv7) of cryptographically strong randomness coincide inside the same
millisecond. You would need to be generating on the order of a billion identifiers per millisecond
before it became worth thinking about.

**Are they unguessable? Can I use one as a security token?**
The random portion is cryptographically strong, so an identifier cannot be predicted from the ones
around it. But the timestamp portion is plainly readable, and identifiers are meant to appear in
logs, URLs and support tickets. **Treat them as public.** Authorise every request properly; never
use an identifier as a bearer token or a password-reset secret.

**How do I split an identifier back apart?**
Parse it: `IdoraId.Parse(id)` gives you `Prefix`, `Value`, `Format` and `Timestamp`, and checks the
identifier is well formed while it is at it. See [Reading identifiers back](#reading-identifiers-back).
The format is simple enough to split on the first underscore by hand — the prefix cannot contain
one — but then nothing tells you the rest was valid.

**Can I use these as Entity Framework Core keys?**
Yes. Add [`Idora.NET.EntityFrameworkCore`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET/src/Idora.EntityFrameworkCore)
and write `modelBuilder.Entity<User>().HasIdoraKey(user => user.Id)`: EF Core fills the key in on
insert with the entity's registered prefix, in a correctly sized ASCII column, and `IdoraId`-typed
keys work too. Without it they are still ordinary strings you can assign yourself. Either way the
values are time-ordered, so clustered index inserts stay at the end of the index, which is exactly
the behaviour you want and the reason not to use `Guid.NewGuid()`.

**Are identifiers case-sensitive?**
Yes; `IdoraId` compares them ordinally, and as strings compare them with `StringComparer.Ordinal`. Generated values are always lowercase, and neither
alphabet contains two characters that differ only by case, so the value half is effectively
case-insensitive anyway. Your prefix is emitted exactly as you registered it, so registering `USR`
gives you `USR_01k2f9…`.

**Why is the maximum prefix 16 characters?**
Long enough for a readable abbreviation, short enough that the prefix never dominates the
identifier. If you find yourself wanting more, the prefix is probably trying to be a description.

**Can I register prefixes with an attribute instead?**
Yes — put `[EntityPrefix("usr")]` on the type and call
`options.RegisterEntitiesFromAssemblyContaining<User>()` at start-up. See
[Registering entity prefixes](#registering-entity-prefixes). The scan runs once during start-up, so
identifier generation is no slower than the fluent route; you name the assemblies rather than the
library hunting for them, for the reasons set out there.

**Which registration style should I use?**
Fluent if you have a handful of entities, or if you publish trimmed or AOT. Attributes once the
registration list is long enough that keeping it in step with the domain is its own chore. There is
no penalty for mixing them, and no run-time difference between them at all — both end up as entries
in the same frozen dictionary before your first request is served.

**Can I override an attributed prefix with a `RegisterEntity` call?**
No, that throws. It would mean a type whose prefix depends on the order two lines ran in, and the
usual reason to want it — a copy-paste slip — is exactly what the exception is there to catch. If
you need a different prefix, change the attribute.

---

## Migrating from UserFriendlyIds

`Idora.NET` 1.0.0 is `UserFriendlyIds` 1.1.0 under its new name; everything since is additive. Identifiers are byte-for-byte
the same format, so everything you have already stored stays valid, and the rename is mechanical:

| UserFriendlyIds | Idora.NET |
| --- | --- |
| `dotnet add package UserFriendlyIds` | `dotnet add package Idora.NET` |
| `using UserFriendlyIds;` | `using Idora;` |
| `services.AddUserFriendlyIds(...)` | `services.AddIdora(...)` |
| `IUserFriendlyIdGenerator` | `IIdoraGenerator` |
| `UserFriendlyIdGenerator` | `IdoraGenerator` |
| `UserFriendlyIdOptions` | `IdoraOptions` |
| `UserFriendlyIdsException` | `IdoraException` |

`EntityPrefixAttribute`, `IEntityPrefixRegistry`, `IdGenerationStrategy` and the other exception
types keep their names and their behaviour.

---

## Contributing

From the `Idora.NET` folder:

```bash
dotnet build -c Release          # all three target frameworks, warnings as errors
dotnet test  -c Release          # the full suite
dotnet run   -c Release --project tests/Idora.Benchmarks -- --filter '*'
```

Tests are written Arrange-Act-Assert and cover the success, failure and edge cases of every public
member, plus the encoders, the monotonic counter and the allocation behaviour directly. The
Entity Framework Core package is tested against a real SQLite database, and the ASP.NET Core
package against an in-memory test server.
`ConformanceTests` runs the shared cross-language vectors in
[`spec/test-vectors.json`](https://github.com/SrKleRAimBot/Idora/blob/main/spec/test-vectors.json),
which every Idora package must reproduce exactly; the behaviour they pin down is set out in the
[specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md).

## Releasing

Releases are driven by tags. Each Idora language is versioned and released on its own, so the tag
names it: pushing an `Idora.NET/v*` tag builds all three target frameworks, runs the full suite,
packs **every** .NET package — `Idora.NET`, `Idora.NET.EntityFrameworkCore` and
`Idora.NET.AspNetCore`, all at the tag's version — verifies each package's contents, publishes them
to nuget.org and opens a GitHub release:

```bash
git tag Idora.NET/v1.1.0
git push origin Idora.NET/v1.1.0
```

The published version comes from the tag, not from the `csproj`, so the two can never disagree. A
tag that is not a valid semantic version fails the workflow before anything is published — which
matters, because **a version published to nuget.org can never be replaced**, only unlisted.

To rehearse a release without publishing anything, run the `Release Idora.NET` workflow manually
from the Actions tab. A manual run builds, tests, packs and verifies exactly as a tag push does, and
attaches the resulting packages to the run as an artifact, but the publishing steps are gated on a
tag push and are skipped. Publishing is deliberately unreachable from a branch or a hand-typed
version.

Publishing uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
the workflow exchanges a short-lived GitHub OIDC token for a one-hour nuget.org API key, so there is
no long-lived secret to store or rotate. The nuget.org trusted-publishing policy must cover every
package ID the workflow pushes.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
