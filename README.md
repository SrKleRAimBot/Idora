# UserFriendlyIds

Entity identifiers that tell you what they are, and sort by when they were made.

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, no dependencies beyond the DI abstractions.

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
dotnet add package UserFriendlyIds
```

Targets `net8.0`, `net9.0` and `net10.0`. The only dependency is
`Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Quick start

Register your entity prefixes once, at start-up:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddUserFriendlyIds(options =>
{
    options.RegisterEntity<User>("usr");
    options.RegisterEntity<Order>("ord");
    options.RegisterEntity<Invoice>("inv");
});
```

Then inject `IUserFriendlyIdGenerator` wherever you create an entity:

```csharp
public sealed class OrderService(IUserFriendlyIdGenerator ids)
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

`RegisterEntity<TEntity>` maps a type to the prefix its identifiers will carry. The type is used
purely as a key — it needs no base class, no interface and no attribute.

```csharp
services.AddUserFriendlyIds(options =>
{
    options.RegisterEntity<User>("usr");
    options.RegisterEntity<Order>("ord");
});
```

A prefix must be **1 to 16 ASCII letters or digits**. The underscore is excluded because it
separates the prefix from the value; allowing one inside a prefix would make an identifier
impossible to split back apart unambiguously.

Registration is validated as it happens, so a mistake stops the process at start-up rather than
surfacing later as a wrong prefix in your database. See [Error handling](#error-handling).

There is also a non-generic overload, `RegisterEntity(Type, string)`, for configuration driven by
reflection.

### Choosing the value format

ULID is the default. Call `UseUuidV7()` to switch:

```csharp
services.AddUserFriendlyIds(options =>
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
var ids = new UserFriendlyIdGenerator(options => options.RegisterEntity<User>("usr"));
string id = ids.Generate<User>();
```

### Reading prefixes back

`IEntityPrefixRegistry` is registered alongside the generator, for when you need the prefix itself
rather than a whole identifier — checking that an identifier belongs to the entity a route expects,
for instance:

```csharp
public sealed class OrderEndpoint(IEntityPrefixRegistry prefixes)
{
    public bool IsAnOrderId(string id) => id.StartsWith(prefixes.GetPrefix<Order>() + "_");
}
```

It also offers `TryGetPrefix<TEntity>(out string?)` and a `Registrations` dictionary of everything
that was registered.

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

Every exception derives from `UserFriendlyIdsException`, so you can catch that one type if you do
not care which went wrong.

| Exception | Thrown when | Thrown at | Fix |
| --- | --- | --- | --- |
| `InvalidEntityPrefixException` | A prefix is empty, longer than 16 characters, or contains something other than an ASCII letter or digit | Start-up, or on the `Generate(string)` call | Use 1–16 letters and digits, no underscore |
| `DuplicateEntityPrefixException` | The same prefix is registered for two entity types, or one entity type is registered twice | Start-up | Give each entity type its own prefix, and register it once |
| `EntityPrefixNotRegisteredException` | `Generate<TEntity>()` is called for a type with no registered prefix | On the call | Register the type, or use `Generate(string)` |

The first two are start-up failures by design. A duplicate prefix defeats the whole point — you
could no longer tell from `acc_01k2f9…` whether you were looking at an `Account` or an `Accrual` —
so the process refuses to start rather than let it through. Prefixes are compared **ignoring case**,
so `usr` and `USR` count as a collision.

Each exception carries the details as properties, not just in the message:

```csharp
try
{
    services.AddUserFriendlyIds(options =>
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
nanoseconds**. Measured with BenchmarkDotNet on .NET 10, Apple M-series, against
`Guid.NewGuid().ToString()` as the baseline:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `Guid.NewGuid().ToString()` *(baseline)* | 226.0 ns | 1.00 | 96 B |
| `"cus_" + Guid.CreateVersion7().ToString("N")` | 254.4 ns | 1.13 | 184 B |
| **`Generate<T>()` — ULID** | **29.8 ns** | **0.13** | **88 B** |
| **`Generate(prefix)` — ULID** | **28.4 ns** | **0.13** | **88 B** |
| **`Generate<T>()` — UUIDv7** | **45.6 ns** | **0.20** | **96 B** |
| **`Generate(prefix)` — UUIDv7** | **43.4 ns** | **0.19** | **96 B** |

Reproduce them yourself with `dotnet run -c Release --project tests/UserFriendlyIds.Benchmarks`.

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
  `Generate<User>()` to `"usr_"` is a couple of nanoseconds.

**Thread safety.** The generator is immutable and registered as a singleton; call it from as many
threads as you like. There are no locks anywhere — the monotonic counter and the entropy buffer are
both thread-local — so throughput scales with cores rather than contending.

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
Split on the first underscore. The prefix contains no underscores, by rule, so this is unambiguous:

```csharp
int separator = id.IndexOf('_');
string prefix = id[..separator];    // "usr"
string value  = id[(separator + 1)..];  // "01k2f9v8ta7m3x0qbrn4dzeh5c"
```

**Can I use these as Entity Framework Core keys?**
Yes — they are ordinary strings. Assign one in your constructor or in a `SaveChanges` interceptor
rather than letting the database generate the key. Because the values are time-ordered, clustered
index inserts stay at the end of the index, which is exactly the behaviour you want and the reason
not to use `Guid.NewGuid()`.

**Are identifiers case-sensitive?**
Yes, compare them with `StringComparer.Ordinal`. Generated values are always lowercase, and neither
alphabet contains two characters that differ only by case, so the value half is effectively
case-insensitive anyway. Your prefix is emitted exactly as you registered it, so registering `USR`
gives you `USR_01k2f9…`.

**Why is the maximum prefix 16 characters?**
Long enough for a readable abbreviation, short enough that the prefix never dominates the
identifier. If you find yourself wanting more, the prefix is probably trying to be a description.

**Can I register prefixes with an attribute instead?**
Not currently. Fluent registration keeps a single source of truth and validates everything at
start-up without reflection or assembly scanning.

---

## Contributing

```bash
dotnet build -c Release          # all three target frameworks, warnings as errors
dotnet test  -c Release          # the full suite
dotnet run   -c Release --project tests/UserFriendlyIds.Benchmarks
```

Tests are written Arrange-Act-Assert and cover the success, failure and edge cases of every public
member, plus the encoders, the monotonic counter and the allocation behaviour directly.

## Releasing

Releases are driven by tags. Pushing a `v*` tag builds all three target frameworks, runs the full
suite, packs, verifies the package contents, publishes to nuget.org and opens a GitHub release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The published version comes from the tag, not from the `csproj`, so the two can never disagree. A
tag that is not a valid semantic version fails the workflow before anything is published — which
matters, because **a version published to nuget.org can never be replaced**, only unlisted.

Publishing uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
the workflow exchanges a short-lived GitHub OIDC token for a one-hour nuget.org API key, so there is
no long-lived secret to store or rotate.

## License

MIT — see [LICENSE](LICENSE).
