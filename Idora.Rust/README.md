# Idora.Rust

Entity identifiers that tell you what they are, and sort by when they were made.

```text
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, and **no dependencies at all** — not even dev-dependencies.

`idora` is the Rust member of the [Idora](https://github.com/SrKleRAimBot/Idora#readme) family.
Every member implements the same
[specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md) and passes
the same [conformance vectors](https://github.com/SrKleRAimBot/Idora/blob/main/spec/test-vectors.json),
so an identifier minted here can be sorted, split and validated by any of the others:

| Package | Language |
| --- | --- |
| [Idora.NET](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET) | C# / .NET |
| [Idora.JS](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.JS) | JavaScript / TypeScript |
| [Idora.Java](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Java) | Java |
| [Idora.Go](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Go) | Go |
| **Idora.Rust** (this crate) | Rust |

---

## Why not just use a UUID?

Two reasons, and they compound.

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` turns up in
a log line, a panic message or a customer's email, you cannot tell whether it is a user, an order or
an invoice. You go and look. With `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` you already know, and so does
everybody else on the team. This is why Stripe, GitHub and others put prefixes on their public
identifiers.

**A random identifier is hostile to your database.** A UUIDv4 is scattered uniformly across the
keyspace, so every insert lands in a different page of the index. Pages split, the index fragments,
and the working set grows until it stops fitting in memory. ULID and UUIDv7 both begin with a
millisecond timestamp, so consecutive inserts land next to each other at the end of the index —
the access pattern an auto-incrementing integer gives you, without the database handing out the
numbers.

You also get creation-time ordering for free: sorting the identifiers as plain strings puts the rows
in the order they were created.

---

## Install

```bash
cargo add idora
```

Edition 2024, MSRV **1.85**. No dependencies, no features, no build script.

---

## Quick start

Build a generator once, at start-up:

```rust
use idora::{Entity, Idora};

struct User;
struct Order;

// A type can declare its own prefix...
struct Invoice;
impl Entity for Invoice {
    const PREFIX: &'static str = "inv";
}

fn ids() -> Result<Idora, idora::Error> {
    Ok(Idora::builder()
        .register::<User>("usr")?        // ...or you give it one.
        .register::<Order>("ord")?
        .register_entity::<Invoice>()?
        .build())
}

fn main() -> Result<(), idora::Error> {
    let ids = ids()?;

    let user_id = ids.generate::<User>()?;    // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
    let order_id = ids.generate::<Order>()?;  // "ord_01k2f9v8tb9p5r2xnc7fkw3m0d"

    assert!(user_id.starts_with("usr_") && order_id.starts_with("ord_"));
    Ok(())
}
```

That is the whole API for the common case. An `Idora` is immutable, `Send + Sync`, and cloning it
is a reference-count increment, so put it in your application state (or a `static` with
`std::sync::LazyLock`) and call it from anywhere.

```rust
use std::sync::LazyLock;
use idora::Idora;

struct User;

static IDS: LazyLock<Idora> = LazyLock::new(|| {
    Idora::builder()
        .register::<User>("usr")
        .expect("prefixes are valid and unique")
        .build()
});

fn new_user_id() -> String {
    IDS.generate::<User>().expect("User is registered")
}

fn main() {
    assert!(new_user_id().starts_with("usr_"));
}
```

---

## Registering prefixes

There are two ways to say that a `User` gets `usr`. They do the same thing, they can be mixed in
one builder, and both are read once at start-up and never again.

Whichever you use, a prefix must be **1 to 16 ASCII letters or digits**. The underscore is excluded
because it separates the prefix from the value; allowing one inside a prefix would make an
identifier impossible to split back apart unambiguously. Every registration is validated as it is
made, so a mistake fails at start-up rather than surfacing later as a wrong prefix in your
database. See [Errors](#errors).

### Explicitly

`register::<T>(prefix)` maps a type to the prefix its identifiers carry. The type is used purely as
a key — any `'static` type works, including enums, marker types and `dyn Trait`. It needs no trait
and no derive.

```rust
use idora::Idora;

struct User;
struct Order;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder()
        .register::<User>("usr")?
        .register::<Order>("ord")?
        .build();
    assert_eq!(ids.prefix_of::<Order>()?, "ord");
    Ok(())
}
```

### Declaratively, with the `Entity` trait

Past a handful of entities, that list stops reading as a single source of truth and starts reading
as a wall. `Entity` puts the prefix next to the type it describes:

```rust
use idora::{Entity, Idora};

pub struct User { /* ... */ }
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

pub struct Order { /* ... */ }
impl Entity for Order {
    const PREFIX: &'static str = "ord";
}

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder()
        .register_entities::<(User, Order)>()?   // a tuple of up to 16 types
        .build();
    assert!(ids.generate::<User>()?.starts_with("usr_"));
    Ok(())
}
```

| Method | Registers |
| --- | --- |
| `register_entity::<T>()` | One `Entity` type. |
| `register_entities::<(A, B, C)>()` | Every type in a tuple of up to 16, in ordinal order of their `std::any::type_name`. Call it again for more. |

The `entity!` macro implements the trait **and checks the prefix at compile time**, so a bad prefix
is a build error instead of a start-up error:

```rust
pub struct User;
pub struct Order;

idora::entity! {
    User => "usr",
    Order => "ord",
}

fn main() {
    use idora::Entity;
    assert_eq!(User::PREFIX, "usr");
}
```

```text
error[E0080]: evaluation panicked: invalid Idora prefix for `User`: a prefix must be 1 to 16 ASCII
              letters or digits, with no `_`
```

`idora::is_valid_prefix` is a `const fn` if you want the same check in your own `const` items.

Rust has no inheritance, so a declaration is never inherited: each type implements `Entity`
itself, and a wrapper such as `Audited<User>` has no prefix until it declares one.

### Why you list the types

Other Idora packages scan an assembly, a module or a package for declarations. Rust has no run-time
reflection to scan with, and even if it did, discovering types implicitly would be worse:

- **It would see only what happens to be linked in**, and a type nobody had touched would be
  skipped in silence — to fail later, on whichever request first created that kind of entity.
- **It would cost time at every start-up**, for a feature whose premise is that it costs nothing at
  run time.
- **It would not be your registry any more.** Any crate in your dependency graph could register a
  prefix into your application, or collide with one of yours and stop it starting.

Naming the types turns all three into a problem you cannot have. The list is also checked by the
compiler: a type that does not implement `Entity` is a type error, not a missing prefix.

The tuple is registered in ordinal order of type name rather than the order you wrote it, so a clash
between two of its members is reported the same way round however the tuple is written.

### Mixing the two

Both styles write into the same registry, so you can declare the entities you own and register the
rest explicitly:

```rust
use idora::{Entity, Idora};

struct User;
impl Entity for User { const PREFIX: &'static str = "usr"; }

// Stands in for a type from another crate: the orphan rule stops you implementing Entity for it.
struct ThirdPartyThing;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder()
        .register_entity::<User>()?
        .register::<ThirdPartyThing>("tpt")?
        .build();
    assert_eq!(ids.registrations().len(), 2);
    Ok(())
}
```

The collision rules do not care where a registration came from. Two declarations claiming `usr`, a
declaration and a `register` call claiming `usr`, or the same type registered both ways all fail
with `Error::DuplicatePrefix` — the last one **even if both agree on the prefix**, because one type
with two sources of truth is a mistake worth hearing about.

### Builder ergonomics

The builder methods take `&mut self` and return `&mut Self`, like `std::process::Command`. They
chain in one expression, and they also work one statement at a time, which is what you want for
conditional registration. A failed registration changes nothing: everything before it is kept.

```rust
use idora::Idora;

struct User;
struct Invoice;

fn main() -> Result<(), idora::Error> {
    let billing_enabled = true;

    let mut builder = Idora::builder();
    builder.register::<User>("usr")?;
    if billing_enabled {
        builder.register::<Invoice>("inv")?;
    }
    let ids = builder.build();
    assert_eq!(ids.registrations().len(), 2);
    Ok(())
}
```

---

## Choosing the value format

ULID is the default. Call `.uuid_v7()` (or `.strategy(Strategy::UuidV7)`) to switch:

```rust
use idora::{Idora, Strategy};

struct User;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder()
        .uuid_v7()
        .register::<User>("usr")?
        .build();
    assert_eq!(ids.strategy(), Strategy::UuidV7);
    assert_eq!(ids.generate::<User>()?.len(), 3 + 1 + 32);
    Ok(())
}
```

|  | **ULID** (default) | **UUIDv7** |
| --- | --- | --- |
| Length | 26 characters | 32 characters |
| Alphabet | Crockford Base32 (`0-9`, `a-z` less `i l o u`) | hexadecimal (`0-9`, `a-f`) |
| Example | `01k2f9v8ta7m3x0qbrn4dzeh5c` | `0195e7b4a1c87f3d9e2b6c4a8f01d5e3` |
| Random bits | 80 | 74 |
| Standardised as | a community spec | RFC 9562 |
| Parses as a UUID | no | yes |
| Ordering within one millisecond | strictly increasing per thread | unordered |

**Use the ULID default unless something forces your hand.** It holds the same 128 bits in six fewer
characters, which matters once the identifier is a primary key repeated across every foreign key,
index and log line. Its alphabet also omits `i`, `l`, `o` and `u`, the characters people
mis-transcribe when reading an identifier aloud or copying it off a screenshot.

**Choose UUIDv7 when something outside your control expects a real UUID**: a `uuid` column, a
schema that validates the format, or another service that parses the value. The value is a
conformant version 7 UUID printed without hyphens; `uuid::Uuid::parse_str` and PostgreSQL's `uuid`
type both accept that form.

---

## The two generate methods

```rust,ignore
fn generate<T: ?Sized + 'static>(&self) -> Result<String, PrefixNotRegisteredError>;
fn generate_with_prefix(&self, prefix: &str) -> Result<String, InvalidPrefixError>;
```

**Prefer `generate::<T>()`.** The prefix lives in one place — your start-up code — instead of being
repeated as a string literal at every call site, so a typo is impossible and renaming a prefix is a
one-line change. Calling it for a type you forgot to register fails immediately rather than quietly
producing something wrong, and Idora never invents a prefix from the type's name:

```rust
use idora::Idora;

struct User;
struct Comment;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder().register::<User>("usr")?.build();

    assert!(ids.generate::<User>().is_ok());      // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
    assert!(ids.generate::<Comment>().is_err());  // Comment was never registered
    Ok(())
}
```

`generate_with_prefix` exists for prefixes that genuinely are not known until run time — a
plugin's own types, or a prefix read from configuration. It validates the prefix on every call,
which costs a few nanoseconds; `generate::<T>()` validated once, at start-up. `Idora::new()` (or
`Idora::default()`) gives you a generator with nothing registered, for exactly this:

```rust
fn main() -> Result<(), idora::InvalidPrefixError> {
    let ids = idora::Idora::new();
    let id = ids.generate_with_prefix("tmp")?;   // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c"
    assert!(id.starts_with("tmp_"));
    Ok(())
}
```

Each method returns the one error it can produce, and both convert into `idora::Error` with `?`.

## Reading prefixes back

The registry is readable, for when you need the prefix itself rather than a whole identifier —
checking that an identifier belongs to the entity a route expects, for instance:

```rust
use idora::Idora;

struct Order;

fn is_an_order_id(ids: &Idora, id: &str) -> bool {
    match (ids.get_prefix::<Order>(), id.split_once('_')) {
        (Some(expected), Some((prefix, _))) => prefix == expected,
        _ => false,
    }
}

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder().register::<Order>("ord")?.build();
    assert!(is_an_order_id(&ids, &ids.generate::<Order>()?));
    assert!(!is_an_order_id(&ids, "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"));
    Ok(())
}
```

| Method | Returns |
| --- | --- |
| `prefix_of::<T>()` | `Result<&str, PrefixNotRegisteredError>` |
| `get_prefix::<T>()` | `Option<&str>` |
| `get_prefix_by_type_id(TypeId)` | `Option<&str>`, for code that only has the type at run time |
| `registrations()` | `&[Registration]` in registration order: `type_id()`, `type_name()`, `prefix()` |

Returned prefixes never include the separator.

---

## What an identifier is made of

```text
    usr _ 01k2f9v8ta 7m3x0qbrn4dzeh5c
    └┬┘ ┬ └────┬───┘ └───────┬───────┘
     │  │      │             │
     │  │      │             └─ 80 bits of randomness, 16 characters
     │  │      └─ 48-bit millisecond timestamp, 10 characters
     │  └─ separator, always a single underscore
     └─ the prefix you registered, 1–16 characters
```

The timestamp comes first, which is what makes the identifiers sort by creation time as plain
strings. UUIDv7 has the same shape: 48 bits of timestamp, the version and variant bits, then 74
bits of randomness, printed as 32 hexadecimal characters instead of 26 Base32 ones.

To split one apart, split on the **first** underscore; the prefix can never contain one:

```rust
fn main() {
    let id = "usr_01k2f9v8ta7m3x0qbrn4dzeh5c";
    let (prefix, value) = id.split_once('_').unwrap();
    assert_eq!((prefix, value), ("usr", "01k2f9v8ta7m3x0qbrn4dzeh5c"));
}
```

### Sizing your database column

| | ULID | UUIDv7 |
| --- | --- | --- |
| Value | 26 | 32 |
| Separator | 1 | 1 |
| Prefix | 1–16 | 1–16 |
| **Total** | **28–43** | **34–49** |

For a typical three-character prefix that is **30 characters** with ULID and **36** with UUIDv7
(`Strategy::value_len()` gives you the value part). Every identifier for a given entity is the same
length, so a fixed-width column is the right choice — `char(30)` on PostgreSQL, or `varchar(30)` if
you prefer. Every character is ASCII, so use a non-Unicode column type where your database makes
the distinction.

If you would rather store UUIDv7 identifiers in a native `uuid` column, strip the prefix and store
the remainder — the prefix is a property of the type, so you already know it from the column you
are reading.

---

## Errors

| Error | Returned when | Returned by | Fix |
| --- | --- | --- | --- |
| `InvalidPrefixError` | A prefix is empty, longer than 16 characters, or contains something other than an ASCII letter or digit | `register*` (start-up), `generate_with_prefix`, `validate_prefix` | Use 1–16 letters and digits, no underscore |
| `DuplicatePrefixError` | One prefix for two types (ignoring ASCII case), or one type registered twice — by two `register` calls, an `Entity` declaration and a `register` call, or two `register_entities` calls | `register*` (start-up) | Give each type its own prefix, and register it exactly once |
| `PrefixNotRegisteredError` | A type with no registered prefix | `generate::<T>`, `prefix_of::<T>` | Register the type, or use `generate_with_prefix` |

`idora::Error` is a `#[non_exhaustive]` enum with one variant per error, and every specific error
converts into it. All of them implement `Display` and `std::error::Error`, and messages say what
went wrong **and how to fix it**. Each one also carries the details as accessors, not just in the
message:

```rust
use idora::{DuplicateKind, Error, Idora, InvalidPrefixReason};

struct Account;
struct Accrual;

fn main() {
    let mut builder = Idora::builder();
    builder.register::<Account>("acc").unwrap();

    match builder.register::<Accrual>("ACC") {
        Err(Error::DuplicatePrefix(e)) => {
            assert_eq!(e.kind(), DuplicateKind::PrefixTaken);
            assert_eq!(e.prefix(), "ACC");
            assert_eq!(e.existing_prefix(), "acc");
            assert!(e.entity_type_name().ends_with("Accrual"));
            assert!(e.conflicting_type_name().ends_with("Account"));
        }
        other => panic!("unexpected: {other:?}"),
    }

    match builder.register::<Accrual>("user_account") {
        Err(Error::InvalidPrefix(e)) => {
            assert_eq!(e.reason(), InvalidPrefixReason::Separator { index: 4 });
            println!("{e}");
            // The entity prefix 'user_account' contains the separator '_' at index 4. The
            // separator divides the prefix from the value, so a prefix cannot contain one.
        }
        other => panic!("unexpected: {other:?}"),
    }
}
```

The first two are start-up failures by design. A duplicate prefix defeats the whole point — you
could no longer tell from `acc_01k2f9…` whether you were looking at an `Account` or an `Accrual` —
so the builder refuses it rather than let it through. Registering one type twice says *"registered
a second time"* when both registrations agree on the prefix and *"cannot also be registered with
…"* when they disagree, because those are different mistakes with different fixes
(`DuplicateKind::TypeRegisteredTwice` and `TypeRegisteredWithDifferentPrefix`).

When a bad prefix comes from an `Entity::PREFIX`, the message names the type that declared it
(`InvalidPrefixError::declaring_type_name`), since there is no line of your code to point at.

Lengths and indexes in `InvalidPrefixReason` count characters (Unicode scalar values), so the bad
character in `"café"` is at index 3.

---

## Performance

Generating an identifier costs **one allocation** — the `String` that comes back, sized exactly —
and **about 50 nanoseconds**. Measured with the crate's own benchmark on rustc 1.99.1, Apple
M-series (14 cores), macOS:

| Case | Median | Ratio |
| --- | ---: | ---: |
| naive UUIDv4: 16 OS-random bytes + `format!` *(baseline)* | 387.5 ns | 1.00 |
| naive prefixed UUIDv7: clock + 10 OS-random bytes + `format!` | 177.8 ns | 0.46 |
| **`generate::<T>()` — ULID** | **47.0 ns** | **0.12** |
| **`generate_with_prefix("cus")` — ULID** | **47.5 ns** | **0.12** |
| **`generate::<T>()` — UUIDv7** | **52.0 ns** | **0.13** |
| **`generate_with_prefix("cus")` — UUIDv7** | **51.7 ns** | **0.13** |

| Multi-threaded `generate::<T>()` — ULID | Aggregate | Throughput |
| --- | ---: | ---: |
| 1 thread | 45.8 ns/id | 22 M ids/s |
| 4 threads | 19.9 ns/id | 50 M ids/s |
| 14 threads | 7.4 ns/id | 135 M ids/s |

Multi-threaded numbers are wall-clock time divided by identifiers produced across all threads, and
are noisier than the single-threaded ones: past a few threads the system allocator dominates.

The baselines are what you would write by hand without a crate: there is no UUID in `std`, and
Idora does not depend on the `uuid` crate even to benchmark against it. Reproduce the numbers with:

```bash
cargo bench
```

How it gets there:

- **One allocation, always.** The prefix (stored with its separator already appended) and the
  value are written into a stack buffer, and copied once into a `String` of exactly the right
  capacity. No `format!`, no intermediate `String`, no `Vec` — and a test with a counting global
  allocator fails if that ever changes, for both formats and both generate methods.
- **Buffered entropy, straight from the OS.** Asking the operating system for random bytes costs
  hundreds of nanoseconds per call, which would dominate everything else. Each thread keeps a 1 KiB
  block of OS entropy and hands out slices of it, refilling roughly every hundred identifiers. The
  bytes come from the same source either way; buffering changes the cost, not the quality. ULIDs
  draw new randomness only on a new millisecond, and count within one.
- **Zero-dependency entropy.** There is no `getrandom` crate in the graph: the crate calls the
  operating system itself, in one small module that holds all of its `unsafe` code
  (`CCRandomGenerateBytes` on Apple platforms, `getentropy(2)` on other Unix, `ProcessPrng` on
  Windows). Every other module is `#![forbid(unsafe_code)]`.
- **A pass-through `TypeId` map.** `TypeId` is already a compiler-generated hash, so the registry's
  `HashMap` uses a hasher that passes it straight through instead of running SipHash over it.
  Resolving `generate::<User>()` to `"usr_"` is a couple of nanoseconds, which is why the registered
  and ad-hoc rows are indistinguishable.

What remains is mostly the platform: on macOS, reading the wall clock (`SystemTime::now`) is about
20 ns and the system allocator about 20 ns of each identifier.

---

## Threads, ordering and monotonicity

An `Idora` is immutable once built, `Send + Sync`, and cheap to `clone` (an `Arc` inside). Call it
from as many threads as you like. **Generation takes no lock**: the entropy buffer and the ULID
monotonic state are thread-local, each padded to its own cache lines, so throughput scales with
cores instead of contending.

The ULID monotonic stream is **per thread**, shared by every `Idora` on that thread:

- ULIDs generated on one thread are **strictly increasing**, even within one millisecond (the
  random field is incremented rather than redrawn) and even across different `Idora` instances.
- If the wall clock steps backwards (an NTP correction, a resumed VM), the stream keeps the last
  millisecond and keeps counting, so it never goes backwards. Identifiers made in that window carry
  a timestamp slightly ahead of the wall clock, which is the lesser evil.
- ULIDs from different threads within the same millisecond, and UUIDv7s within the same
  millisecond, are in no particular order relative to each other. Guaranteeing more would need a
  process-wide lock, which would cost more than the ordering is worth.

---

## Supported platforms

| Platform | Entropy source |
| --- | --- |
| macOS, iOS, tvOS, watchOS, visionOS | `CCRandomGenerateBytes` (libSystem), as `std` uses |
| Linux (glibc 2.25+, musl), Android (API 28+) | `getentropy(2)` |
| FreeBSD, OpenBSD, NetBSD, DragonFly BSD, illumos, Solaris | `getentropy(2)` |
| Windows 10+ | `ProcessPrng` (`bcryptprimitives.dll`, linked with `raw-dylib`), as `std` uses |

Any other target — including `wasm32` — fails to compile with a message saying so, rather than
falling back to something weaker. `std` is required (for thread-locals and the clock); the crate is
not `no_std`.

---

## Questions you might reasonably have

**Do identifiers stay in order across servers in different time zones?**
Yes. Time zones are irrelevant here: both ULID and UUIDv7 define their timestamp as milliseconds
since the Unix epoch, an absolute instant with no zone attached, and this crate reads it from
`SystemTime::now()`, which has no zone either. There is nothing to configure and no time zone code
anywhere in the crate; a test re-runs itself under `TZ=Pacific/Kiritimati` and `TZ=Etc/GMT+12` to
prove it.

What *does* affect cross-server ordering is **clock skew** — servers disagreeing about what the
instant is. With typical NTP synchronisation that is single-digit milliseconds, so identifiers
created seconds apart on different hosts order correctly, while two created within a few
milliseconds of each other may not. That is a property of distributed timestamps in general; if
you need a total order across machines that does not depend on their clocks, you need a sequencer,
not an identifier format.

**Could two identifiers collide?**
Not in any practical sense. Within a millisecond on one thread, collision is impossible by
construction — the counter increments. Across threads and processes, two identifiers collide only
if 80 bits (ULID) or 74 bits (UUIDv7) of cryptographically strong randomness coincide inside the
same millisecond.

**What about `fork()`?**
A process that calls `fork()` without `exec` gives the child a copy of the forking thread's entropy
buffer and ULID state, so parent and child could hand out the same random bits until their buffers
refill. `std::process::Command` is unaffected (it `exec`s). If you fork worker processes yourself,
generate identifiers only after the fork, or on threads created after it.

**Are they unguessable? Can I use one as a security token?**
The random portion is cryptographically strong, so an identifier cannot be predicted from the ones
around it. But the timestamp is plainly readable, and identifiers are meant to appear in logs, URLs
and support tickets. **Treat them as public.** Never use one as a bearer token or a password-reset
secret.

**Why `String` and not an `Id<User>` newtype?**
Because identifiers cross every boundary — JSON, SQL, logs, other languages — as strings, and a
`String` works with every crate you already use (serde, sqlx, diesel, axum extractors) with no
integration features for this crate to maintain. If you want a typed wrapper, a one-line newtype
around the `String` in your own code is the right place for it.

**Does it support serde / sqlx / diesel?**
There is nothing to support: the output is a `String`. That is also how Idora stays at zero
dependencies.

**Are identifiers case-sensitive?**
Compare them as ordinary strings. Generated values are always lowercase, and neither alphabet has
two characters that differ only by case. Your prefix is emitted exactly as you registered it, so
registering `USR` gives you `USR_01k2f9…` — and `usr` and `USR` cannot both be registered.

**Why is the maximum prefix 16 characters?**
Long enough for a readable abbreviation, short enough that the prefix never dominates the
identifier. If you find yourself wanting more, the prefix is probably trying to be a description.

**Which registration style should I use?**
Explicit `register` for a handful of entities or for types you do not own; `Entity` (ideally via
`entity!`, for the compile-time check) once keeping a list in step with the domain is its own
chore. There is no run-time difference: both end up as entries in the same map before your first
identifier is generated.

**Can I override a declared prefix with a `register` call?**
No, that is an error. It would mean a type whose prefix depends on the order two lines ran in, and
the usual reason to want it — a copy-paste slip — is exactly what the error is there to catch. If
you need a different prefix, change the declaration.

**Can I generate an identifier for a type only known at run time?**
Look its prefix up with `get_prefix_by_type_id` and pass it to `generate_with_prefix`, or keep the
prefix alongside whatever run-time handle you have.

---

## Contributing

```bash
cargo fmt --check
cargo clippy --all-targets -- -D warnings
cargo test                 # unit, integration, conformance, allocation and doc tests
cargo doc --no-deps
cargo bench
```

The conformance suite reads the shared vectors from `../spec/test-vectors.json` in the
[Idora repository](https://github.com/SrKleRAimBot/Idora), so run the tests from a full checkout.
The test and benchmark harnesses — including the JSON parser the conformance suite uses — are
hand-written on `std`, to keep the dependency count at zero.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/Idora.Rust/LICENSE).
