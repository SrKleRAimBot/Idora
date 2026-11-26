# Idora.Rust

Entity identifiers that tell you what they are, and sort by when they were made.

```text
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, and by default **no dependencies at all** — not even dev-dependencies.

`idora` is the Rust member of the [Idora](https://github.com/SrKleRAimBot/Idora#readme) family.
Every member implements the same
[specification](https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md) and passes
the same [conformance vectors](https://github.com/SrKleRAimBot/Idora/blob/main/spec/test-vectors.json),
so an identifier minted here can be parsed, sorted and validated by any of the others:

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

Edition 2024, MSRV **1.85**. No dependencies and no build script. Two optional features, both off
by default, integrate with the crates that need to see inside an identifier:

| Feature | Adds | Needs |
| --- | --- | --- |
| `serde` | `Serialize` / `Deserialize` for `Id` and `TypedId`, as strings, validated on the way in | `serde` 1 |
| `sqlx` | `Type` / `Encode` / `Decode` for `Id` and `TypedId`, for every sqlx database | `sqlx` 0.9, Rust 1.94 |

```bash
cargo add idora --features serde,sqlx
```

And one companion crate, in this repository:

| Crate | What it does |
| --- | --- |
| [`idora-axum`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Rust/crates/idora-axum) | axum extractors: `IdFor<User>` parses `/users/{id}` and checks it is a user's identifier, answering `400` or `404` otherwise; Idora request ids for tower-http |

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

If you would rather have the parts than the text, `generate_id::<T>()` and
`generate_id_with_prefix(prefix)` return an [`Id`](#reading-identifiers-back) instead of a
`String`. An `Id` lives on the stack, so these cost **no allocation at all**.

## Reading prefixes back

The registry is readable, for when you need the prefix itself rather than a whole identifier:

```rust
use idora::Idora;

struct Order;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder().register::<Order>("ord")?.build();
    assert_eq!(ids.prefix_of::<Order>()?, "ord");
    assert_eq!(ids.get_prefix::<String>(), None);
    Ok(())
}
```

| Method | Returns |
| --- | --- |
| `prefix_of::<T>()` | `Result<&str, PrefixNotRegisteredError>` |
| `get_prefix::<T>()` | `Option<&str>` |
| `get_prefix_by_type_id(TypeId)` | `Option<&str>`, for code that only has the type at run time |
| `registrations()` | `&[Registration]` in registration order: `type_id()`, `type_name()`, `prefix()` |

Returned prefixes never include the separator. To check that an identifier belongs to an entity,
don't compare prefixes by hand: `ids.is_id_for::<Order>(text)` does it, and checks the rest of the
identifier too.

---

## Reading identifiers back

Identifiers come back — in a route, a message, a foreign key — and need checking, reading and
ordering. `Id` is a parsed identifier: it holds the prefix, the value, the format and the timestamp
the value begins with, and can be built from any identifier any Idora package mints, in either
format, whichever strategy your own generator uses.

```rust
use std::time::{Duration, UNIX_EPOCH};
use idora::{Id, Strategy};

fn main() -> Result<(), idora::InvalidIdError> {
    let id: Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;

    assert_eq!(id.prefix(), "usr");
    assert_eq!(id.value(), "01k226e0g0008j4ct4ank7f24s");
    assert_eq!(id.format(), Strategy::Ulid);
    assert_eq!(id.timestamp_ms(), 1_754_568_000_000);               // 2025-08-07T12:00:00Z
    assert_eq!(id.created_at(), UNIX_EPOCH + Duration::from_millis(1_754_568_000_000));
    assert_eq!(id.as_str(), "usr_01k226e0g0008j4ct4ank7f24s");      // also Display, AsRef<str>
    Ok(())
}
```

An `Id` is 64 bytes on the stack — the text inline, beside the decoded timestamp — so it is `Copy`,
and parsing, copying and comparing it never touch the heap.

### Parsing is strict

`Id::parse` accepts exactly what a conforming generator emits and nothing else: no trimming, no
case folding, no hyphenated UUIDs. An identifier that only works because something was lenient
about it is a bug waiting for the one consumer that isn't. The first failing check is reported:

| Reason | When |
| --- | --- |
| `InvalidIdReason::NoSeparator` | there is no `_` |
| `InvalidIdReason::Prefix(InvalidPrefixError)` | the text before the first `_` is not a valid prefix (the prefix error is also the `source()`) |
| `InvalidIdReason::Value` | the text after it is not a 26-character lowercase ULID or a 32-character lowercase UUIDv7 |
| `InvalidIdReason::PrefixMismatch { expected }` | it is well formed, but not with the prefix you asked for |

| Method | Returns |
| --- | --- |
| `Id::parse(text)`, `text.parse::<Id>()`, `Id::try_from(text)` | `Result<Id, InvalidIdError>` |
| `Id::parse_with_prefix(text, "usr")` | `Result<Id, InvalidIdError>`, the prefix compared exactly, case included |
| `Id::is_valid(text)`, `Id::is_valid_with_prefix(text, "usr")` | `bool`, and never allocates |
| `ids.parse::<User>(text)` | `Result<Id, idora::Error>`, with the prefix registered for `User` |
| `ids.is_id_for::<User>(text)` | `Result<bool, PrefixNotRegisteredError>` |

The registry-aware pair is the one to reach for when an identifier arrives that is supposed to name
a particular entity. An unregistered type fails with `PrefixNotRegistered` rather than "invalid
identifier": the identifier is not at fault.

```rust
use idora::{Error, Idora, InvalidIdReason};

struct User;
struct Order;

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder()
        .register::<User>("usr")?
        .register::<Order>("ord")?
        .build();

    let user = ids.parse::<User>("usr_01k226e0g0008j4ct4ank7f24s")?;
    assert_eq!(user.prefix(), "usr");

    match ids.parse::<Order>("usr_01k226e0g0008j4ct4ank7f24s") {
        Err(Error::InvalidId(e)) => {
            assert!(matches!(e.reason(), InvalidIdReason::PrefixMismatch { .. }));
            println!("{e}");
            // The identifier 'usr_01k226e0g0008j4ct4ank7f24s' has the prefix 'usr', but an
            // identifier with the prefix 'ord' was expected. Prefixes are compared exactly, case
            // included; check that the identifier refers to the right kind of entity.
        }
        other => panic!("unexpected: {other:?}"),
    }
    Ok(())
}
```

The text being parsed usually comes from outside, so messages quote it with control characters
escaped and anything past 64 characters cut off. `InvalidIdError::text()` has it untouched.

### Ordering and comparing

Two `Id`s are equal when their text is equal. Their order is **chronological**, not textual: by
timestamp, then value, then prefix. That is what makes identifiers of different entities and
different formats interleave in the order they were made — an order created after a user sorts
after that user, even though `ord` < `usr`:

```rust
use std::time::{Duration, UNIX_EPOCH};
use idora::Id;

fn main() -> Result<(), idora::InvalidIdError> {
    let user: Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
    let order: Id = "ord_01k226e0g1zzqdvk5vnacrgxv6".parse()?;     // a millisecond later
    let invoice: Id = "inv_0198846702007011a233445566778899".parse()?; // UUIDv7, same ms as user

    let mut all = vec![order, user, invoice];
    all.sort();
    assert_eq!(all, [invoice, user, order]);

    assert!(user.is_before(&order) && order.is_after(&user));
    assert!(user.same_millisecond(&invoice));           // cmp_timestamp is Ordering::Equal
    assert!(user.has_prefix("usr") && !user.has_prefix("USR"));

    let minted = UNIX_EPOCH + Duration::from_millis(1_754_568_000_000);
    assert!(user.created_before(minted + Duration::from_millis(1)));
    assert!(!user.created_after(minted + Duration::from_micros(500))); // same millisecond
    Ok(())
}
```

`created_before` and `created_after` are strict and compare at millisecond precision. For ULIDs from
one thread the natural order is exactly generation order. Because it differs from string order,
`Id` deliberately does not implement `Borrow<str>`; compare it with a string directly
(`id == "usr_…"`) or through `as_str()`.

### Time-window queries without parsing

A value starts with its timestamp, so every identifier one prefix gets during a span of time is a
contiguous range of strings. `Id::lower_bound` and `Id::upper_bound` give you its ends:

```rust
use std::time::{Duration, SystemTime};
use idora::{Id, Strategy};

fn main() -> Result<(), idora::InvalidPrefixError> {
    let to = SystemTime::now();
    let from = to - Duration::from_secs(24 * 60 * 60);

    let lower = Id::lower_bound("usr", from, Strategy::Ulid)?; // usr_01k2…0000000000000000
    let upper = Id::upper_bound("usr", to, Strategy::Ulid)?;   // usr_01k2…zzzzzzzzzzzzzzzz

    // SELECT * FROM users WHERE id >= $1 AND id <= $2   -- an index range scan
    let _ = (lower.as_str(), upper.as_str());
    Ok(())
}
```

The bounds are the lowest and highest identifiers any conforming generator can mint in those
milliseconds, and are valid identifiers themselves. They compare correctly only as plain strings,
against identifiers of the same prefix and format — the shape of a database column.
`Id::lower_bound_value` and `upper_bound_value` give the bare values, for a column that stores
UUIDv7s without their prefix. A time before 1970 or after the year 10889 is out of range and
panics; the prefix is validated, as for `generate_with_prefix`.

### Typed identifiers

`TypedId<E>` is an `Id` known, in the type system, to carry `E::PREFIX`. A function that takes a
`TypedId<User>` cannot be handed an order's identifier, and has nothing left to check:

```rust
use idora::{Entity, TypedId};

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

fn deactivate(user: TypedId<User>) -> String {
    format!("UPDATE users SET active = false WHERE id = '{user}'")
}

fn main() -> Result<(), idora::InvalidIdError> {
    let user: TypedId<User> = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
    assert_eq!(user.timestamp_ms(), 1_754_568_000_000); // Deref<Target = Id>
    deactivate(user);

    assert!("ord_01k226e0g1zzqdvk5vnacrgxv6".parse::<TypedId<User>>().is_err());
    Ok(())
}
```

It costs what an `Id` costs, is `Copy + Send + Sync` whatever `E` is, and converts to and from `Id`
(`TypedId::from_id`, `Id::from`). The prefix is the one `E` *declares*, so it needs no generator;
for types registered with `register::<T>("…")`, check against the registry with `ids.parse::<T>()`.

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

The prefix can never contain an underscore, so splitting on the **first** one always recovers the
two parts. `Id::parse` does that and checks both; by hand it is:

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
| `PrefixNotRegisteredError` | A type with no registered prefix | `generate::<T>`, `prefix_of::<T>`, `parse::<T>`, `is_id_for::<T>` | Register the type, or use `generate_with_prefix` |
| `InvalidIdError` | Text that is not an identifier, or is one with the wrong prefix | `Id::parse*`, `TypedId::parse`, `str::parse`, `parse::<T>` (inside `Error::InvalidId`) | Pass the identifier exactly as generated; check it names the right entity |

`idora::Error` is a `#[non_exhaustive]` enum with one variant per error, and every specific error
converts into it. (`Error::InvalidId` is new in 1.1; because the enum is `#[non_exhaustive]`, adding
it breaks no `match`.) All of them implement `Display` and `std::error::Error`, and messages say what
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
and **about 50 nanoseconds**. As an `Id`, it costs no allocation at all, and parsing one back costs
about 15. Measured with the crate's own benchmark on rustc 1.99.1, Apple
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

| Reading and `Id` | Median |
| --- | ---: |
| `generate_id::<T>()` — ULID, no allocation | 40.3 ns |
| `Id::parse` — ULID | 14.7 ns |
| `Id::parse` — UUIDv7 | 25.2 ns |
| `Id::is_valid` on a malformed identifier | 5.6 ns |
| `ids.parse::<T>()` — registered prefix | 20.2 ns |

Parsing is one pass over at most 49 bytes with a lookup table per alphabet, and never allocates
unless it fails — a counting-allocator test holds `Id::parse`, `is_valid`, `ids.parse::<T>()`,
comparisons and `generate_id` to zero allocations.

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

**Why does `generate` return a `String` and not an `Id`?**
Because identifiers cross every boundary — JSON, SQL, logs, other languages — as strings, and a
`String` works with every crate you already use without a feature flag. `generate_id` returns an
`Id` for when you want the parts, and `Id` and `TypedId<E>` are the types to take *in*, where text
from outside needs checking.

**Does it support serde / sqlx / diesel?**
serde and sqlx through the [`serde` and `sqlx` features](#cargo-features), and axum through
[`idora-axum`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Rust/crates/idora-axum). For
anything else, the generated `String` already works everywhere, and an `Id` converts from it with
`parse` and back with `as_str`. diesel's `ToSql`/`FromSql` could only live in this crate too (the
orphan rule); it is not there yet because a `String` column plus `Id::parse` covers it.

**Are identifiers case-sensitive?**
Yes. Compare them as ordinary strings, or as `Id`s. Generated values are always lowercase, and neither alphabet has
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

## Cargo features

Both are off by default. They live in this crate because Rust's orphan rule allows a foreign trait
to be implemented for `Id` only here or in the trait's own crate.

### `serde`

`Id` and `TypedId<E>` serialize as their text and deserialize from it, through the strict parse:
a malformed identifier, or another entity's in a `TypedId<E>`, fails at the boundary instead of
becoming a value that fails later.

```rust,ignore
use idora::{Entity, Id, TypedId};
use serde::{Deserialize, Serialize};

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

#[derive(Serialize, Deserialize)]
struct Order {
    id: Id,
    customer: TypedId<User>,
}

let order: Order = serde_json::from_str(
    r#"{ "id": "ord_01k226e0g1zzqdvk5vnacrgxv6", "customer": "usr_01k226e0g0008j4ct4ank7f24s" }"#,
)?;
assert!(serde_json::from_str::<Order>(
    r#"{ "id": "ord_01k226e0g1zzqdvk5vnacrgxv6", "customer": "ord_01k226e0g1zzqdvk5vnacrgxv6" }"#,
).is_err());
```

### `sqlx`

`Id` and `TypedId<E>` implement sqlx 0.9's `Type`, `Encode` and `Decode` for **every** database,
by deferring to the driver's own implementations for `str`. They bind as text parameters and
decode from any column a `&str` decodes from (`TEXT`, `VARCHAR`, `CHAR(n)`, …), through the strict
parse:

```rust,ignore
use idora::{Id, TypedId};

#[derive(sqlx::FromRow)]
struct UserRow {
    id: TypedId<User>,   // a column holding an order's identifier is a decode error
    referrer: Option<Id>,
}

sqlx::query("INSERT INTO users (id, referrer) VALUES ($1, $2)")
    .bind(ids.generate_id::<User>()?)
    .bind(None::<Id>)
    .execute(&pool)
    .await?;

let recent: Vec<UserRow> = sqlx::query_as("SELECT * FROM users WHERE id >= $1 ORDER BY id")
    .bind(Id::lower_bound("usr", since, Strategy::Ulid)?)
    .fetch_all(&pool)
    .await?;
```

The feature depends on `sqlx` itself with no default features, not on the semver-exempt
`sqlx-core`, and needs Rust 1.94 because sqlx 0.9 does; the rest of the crate keeps its 1.85 MSRV.
It tracks sqlx 0.9; a later sqlx will arrive under a feature of its own, so upgrading Idora never
breaks a build.

Both examples are marked `ignore` here only because the core crate's doctests cannot depend on
`serde_json` or a database; the same code is compiled and run, against serde_json and an in-memory
SQLite, in `crates/idora-tests`.

---

## Contributing

`Idora.Rust` is a Cargo workspace: the `idora` crate at the root, `idora-axum` in `crates/`, and an
unpublished `idora-tests` crate holding the tests for the optional features.

```bash
cargo fmt --all --check
cargo clippy --workspace --all-targets --all-features -- -D warnings
cargo test -p idora                       # the core, dependency-free: unit, integration,
                                          # conformance, allocation and doc tests
cargo test --workspace --all-features     # plus serde, sqlx and idora-axum
cargo doc --workspace --no-deps --all-features
cargo bench -p idora
```

The conformance suite reads the shared vectors from `../spec/test-vectors.json` in the
[Idora repository](https://github.com/SrKleRAimBot/Idora), so run the tests from a full checkout.
The core's test and benchmark harnesses — including the JSON parser the conformance suite uses —
are hand-written on `std`, so `cargo test -p idora` builds nothing but `idora`. The tests that need
`serde_json`, `sqlx` or `tokio` live in `crates/idora-tests` instead, because a crate's
dev-dependencies are always compiled for its tests.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/Idora.Rust/LICENSE).
