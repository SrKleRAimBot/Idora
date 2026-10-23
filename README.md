# Idora

**Entity identifiers that tell you what they are, and sort by when they were made.**

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

Idora generates prefixed, time-ordered identifiers: a short prefix you register once per entity
type, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). The same identifiers, under the same rules, are
available for five ecosystems, each package written in the idiom of its own language and none of
them pulling in third-party dependencies.

## Packages

| Package | Language | Install | Registry |
| --- | --- | --- | --- |
| [**Idora.NET**](Idora.NET) | C# / .NET 8+ | `dotnet add package Idora.NET` | [NuGet](https://www.nuget.org/packages/Idora.NET) |
| [**Idora.JS**](Idora.JS) | TypeScript / JavaScript | `npm install idora.js` | [npm](https://www.npmjs.com/package/idora.js) |
| [**Idora.Java**](Idora.Java) | Java 17+ | `io.github.srkleraimbot:idora` | [Maven Central](https://central.sonatype.com/artifact/io.github.srkleraimbot/idora) |
| [**Idora.Go**](Idora.Go) | Go | `go get github.com/SrKleRAimBot/Idora/Idora.Go` | [pkg.go.dev](https://pkg.go.dev/github.com/SrKleRAimBot/Idora/Idora.Go) |
| [**Idora.Rust**](Idora.Rust) | Rust | `cargo add idora` | [crates.io](https://crates.io/crates/idora) |

Each package's README covers installation, the full API, error handling, performance figures and
the questions people usually ask.

## Why

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` appears in a
log line, a stack trace or a customer's email, you cannot tell whether it is a user, an order or an
invoice. `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` tells you, and everyone else, at a glance — the reason
Stripe, GitHub and others prefix their public identifiers.

**A random identifier is hostile to your database.** A version 4 UUID lands every insert in a
different page of the index, so the index fragments and its working set grows. ULID and UUIDv7 begin
with a millisecond timestamp, so consecutive inserts land together at the end of the index — the
access pattern of an auto-incrementing integer, without the database handing out numbers. Sorting
the identifiers as plain strings also puts them in creation order, for free.

## How it works

### The identifier

```
    usr _ 01k2f9v8ta 7m3x0qbrn4dzeh5c
    └┬┘ ┬ └────┬───┘ └───────┬───────┘
     │  │      │             └─ 80 bits of cryptographically strong randomness, 16 characters
     │  │      └─ 48-bit UTC millisecond timestamp, 10 characters
     │  └─ separator, always a single underscore
     └─ the prefix registered for the entity type, 1–16 ASCII letters or digits
```

- **The prefix** is registered once, at start-up, against an entity type, so call sites ask for "an
  identifier for a `User`" and never repeat the string. A prefix is 1–16 ASCII letters or digits;
  it can never contain the underscore, so splitting on the first `_` always recovers both parts.
- **The value** is a 26-character Crockford Base32 ULID by default — the same 128 bits as a UUID in
  six fewer characters, in an alphabet without the easily confused `i`, `l`, `o` and `u` — or, when
  something insists on a real UUID, a 32-character hyphen-free RFC 9562 UUIDv7.
- **The timestamp comes first** and is always read in UTC, so identifiers sort by creation time as
  plain strings, on any server in any time zone.

### Registration

A registry maps each entity type to its prefix. It is built once and is immutable afterwards.
Prefixes can be registered explicitly, or declared on the type itself with the language's native
mechanism, and the two can be mixed freely:

| Package | Explicit | Declared on the type |
| --- | --- | --- |
| Idora.NET | `options.RegisterEntity<User>("usr")` | `[EntityPrefix("usr")]` attribute |
| Idora.JS | `entities: [[User, 'usr']]` | `@entityPrefix('usr')` decorator |
| Idora.Java | `.registerEntity(User.class, "usr")` | `@EntityPrefix("usr")` annotation |
| Idora.Go | `idora.WithEntity[User]("usr")` | an `IDPrefix() string` method |
| Idora.Rust | `.register::<User>("usr")` | `impl Entity for User { const PREFIX … }` |

Declarations are collected only from places you name — an assembly, a module, a package, a list of
types. Idora never goes hunting through everything loaded into the process, because that sees only
what happens to be loaded, slows every start-up, and lets unrelated code register prefixes into your
application.

Mistakes fail at start-up, not in production. A malformed prefix, one prefix claimed by two types
(compared ignoring case, so `usr` and `USR` collide), or one type registered twice all stop the
generator from being built, with a message that says what is wrong and how to fix it. Asking for an
identifier for a type that was never registered is an error too — Idora never guesses a prefix from
a type name, because names change and prefixes must not.

### Ordering and uniqueness

Identifiers are ordered by the millisecond they were made in, across threads, processes and
machines (to within the machines' clock skew). Within one millisecond, ULIDs from the same stream are
**strictly increasing**: the random field is incremented rather than redrawn. If the clock steps
backwards, the stream keeps its last millisecond rather than going back in time, so a sequence never
regresses. Each package documents the exact scope of its stream — a thread, a realm, a generator —
chosen so that generation never contends on a process-wide lock.

Randomness comes from the operating system's cryptographically secure generator, drawn in 1 KiB
blocks so that the cost of asking for it is shared across a hundred or so identifiers. Collisions
would need 80 bits (ULID) or 74 bits (UUIDv7) of that randomness to coincide within the same
millisecond.

Identifiers are unpredictable but not secret: the timestamp is readable by anyone. Treat them as
public, and never use one as a bearer token.

### One specification, five implementations

Every package implements the same [specification](spec/SPECIFICATION.md) and passes the same
[conformance vectors](spec/test-vectors.json) — exact ULID and UUIDv7 encodings, monotonic-step
behaviour, and the prefixes that must be accepted, rejected and treated as colliding. An identifier
minted by Idora.Go can be sorted, split and validated by Idora.JS without either knowing where it
came from.

## Repository layout

```
Idora.NET/    the .NET package          Idora.Go/     the Go module
Idora.JS/     the npm package           Idora.Rust/   the Rust crate
Idora.Java/   the Maven artifact        spec/         the shared specification and vectors
```

Each package builds, tests, benchmarks and releases independently, from its own folder and its own
GitHub Actions workflows. Releases are cut by pushing a tag named after the package, such as
`Idora.NET/v1.0.0` or `Idora.Go/v1.0.0`.

## History

Idora began as **UserFriendlyIds**, a .NET-only package. `Idora.NET` is its continuation under the
new name and produces byte-for-byte the same identifiers; see
[Migrating from UserFriendlyIds](Idora.NET/README.md#migrating-from-userfriendlyids).

## License

MIT — see [LICENSE](LICENSE).
