# Idora.Go

Entity identifiers that tell you what they are, and sort by when they were made.

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, and **no dependencies** outside the standard library, tests included.

Idora.Go is the Go member of the [Idora](../README.md) family. Every package implements the same
[specification](../spec/SPECIFICATION.md) and passes the same
[conformance vectors](../spec/test-vectors.json), so an identifier minted here can be parsed,
sorted and validated by any of the others:

| Package | Language |
| --- | --- |
| [Idora.NET](../Idora.NET) | C# / .NET |
| [Idora.JS](../Idora.JS) | JavaScript / TypeScript |
| [Idora.Java](../Idora.Java) | Java |
| **Idora.Go** | Go |
| [Idora.Rust](../Idora.Rust) | Rust |

---

## Why not just use a random UUID?

There are two reasons, and each makes the other worse.

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` turns up in
a log line, a panic trace or a customer's email, you can't tell whether it's a user, an order or
an invoice without going to look it up. `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` tells you straight away,
and it tells everyone else on the team too. This is why Stripe, GitHub and others put prefixes on
their public identifiers.

**A random identifier is hostile to your database.** A version 4 UUID is scattered uniformly
across the keyspace, so every insert lands in a different page of the index. Pages split, the
index fragments, and the working set grows until it no longer fits in memory. ULID and UUIDv7 both
begin with a millisecond timestamp, so consecutive inserts land next to each other at the end of
the index. That's the access pattern an auto-incrementing integer gives you, but the database
doesn't have to hand out the numbers.

You also get creation-time ordering for free. Sort the identifiers as plain strings and the rows
come out in the order they were created, with no separate timestamp column and no secondary index.

---

## Install

```bash
go get github.com/SrKleRAimBot/Idora/Idora.Go@latest
```

```go
import "github.com/SrKleRAimBot/Idora/Idora.Go"
```

The package name is `idora`, so the import is used as `idora.New`, `idora.Generate` and so on.
Requires Go 1.23 or later (it uses `iter.Seq2` and `reflect.TypeFor`).

The module lives in a subdirectory of the Idora repository, so Go requires its release tags to
carry the directory as a prefix: versions are tagged `Idora.Go/vX.Y.Z` (the first is
`Idora.Go/v1.0.0`), and `go get …@v1.0.0` resolves to that tag.

---

## Quick start

Build a generator once, at start-up:

```go
gen, err := idora.New(
    idora.WithEntity[User]("usr"),
    idora.WithEntity[Order]("ord"),
    idora.WithEntity[Invoice]("inv"),
)
if err != nil {
    log.Fatal(err) // a registration mistake: refuse to start
}
```

You can also declare the prefix on the entity and list the types (see
[Declaratively](#declaratively-with-an-idprefix-method)):

```go
type User struct{ ID string }

func (User) IDPrefix() string { return "usr" }

gen, err := idora.New(idora.WithEntities(User{}, Order{}, Invoice{}))
```

Then share the generator anywhere you create an entity:

```go
func (s *OrderService) PlaceOrder(cart Cart) (Order, error) {
    id, err := idora.Generate[Order](s.ids) // "ord_01k2f9v8tb9p5r2xnc7fkw3m0d"
    if err != nil {
        return Order{}, err
    }
    return Order{ID: id, Lines: cart.Lines}, nil
}
```

That covers the common case. A `*Generator` is safe for concurrent use, so you can keep one in a
package variable or a struct field and call it from any goroutine.

---

## Configuration

`New` takes functional options and applies them **in order**. The first option that fails stops
construction: `New` returns that error and a nil generator. `MustNew` panics instead, which suits
package-level variables and tests.

### Registering entity prefixes

There are two ways to say that a `User` gets `usr`. They do the same thing, you can mix them in one
generator, and both are read once, inside `New`, and never again.

Either way, a prefix must be **1 to 16 ASCII letters or digits**. The underscore isn't allowed
because it separates the prefix from the value: if a prefix could contain one, you couldn't split
an identifier back apart unambiguously. Each registration is validated as it's made, so a mistake
stops the process at start-up instead of turning up later as a wrong prefix in your database. See
[Errors](#errors).

#### Explicitly, with `WithEntity`

`WithEntity[T]` maps a type to the prefix its identifiers will carry. The type is used purely as a
key. It needs no methods, no embedded base and no particular shape, so a struct, a named string or
an `int` all work.

```go
gen, err := idora.New(
    idora.WithEntity[User]("usr"),
    idora.WithEntity[Order]("ord"),
)
```

`WithEntityType(reflect.Type, string)` does the same for a type that's only known at run time,
for configuration driven by reflection.

#### Declaratively, with an `IDPrefix` method

Once you have more than a handful of entities, that list starts to read as a wall rather than a
single source of truth. Implementing `idora.Entity` puts the prefix next to the type it describes:

```go
type User struct {
    ID string
}

func (User) IDPrefix() string { return "usr" }
```

and you list the types to collect:

```go
gen, err := idora.New(idora.WithEntities(User{}, &Shipment{}, Tag("")))
```

Only the type of each value matters. `User{}`, `&User{}` and `(*User)(nil)` are equivalent, and a
pointer receiver works too. `IDPrefix` is called once, inside `New`, on a fresh zero value. It must
therefore return a constant rather than read the receiver's fields, and it's never called on a nil
pointer you passed in.

`WithEntities` registers the types in **ordinal order of their fully qualified name**
(`PkgPath + "." + Name`), whatever order you list them in. A clash between two of them is
therefore reported the same way round on every run.

After `New` returns, nothing reflects over anything: declared prefixes go into the same immutable
map as explicit ones, so `Generate[T]` costs the same either way.

#### Why you list the types

Go can't enumerate a package's types at run time, so a package-scanning shortcut isn't even
possible. But the [specification](../spec/SPECIFICATION.md#42-declarative-registration) rules out
implicit discovery in every language, for reasons that apply here too:

- **Discovery sees only what's loaded.** Anything that relies on `init` side effects or a global
  registry fills in only for packages something has already imported. A missing entity doesn't
  fail at start-up. It fails with `ErrPrefixNotRegistered` on whichever request first creates that
  kind of entity, possibly in production.
- **It isn't your registry any more.** Any dependency could register a prefix into your
  application, or collide with one of yours and stop it starting.

With an explicit list, neither problem can happen. The list is also where you read off every
prefix your application issues.

#### Embedding does not inherit a prefix

Go **promotes** the methods of embedded fields, so this compiles and `Admin` satisfies
`idora.Entity`:

```go
type Admin struct {
    User          // Admin now has User's IDPrefix method, returning "usr"
    Permissions []string
}
```

Registering it would make every admin indistinguishable from a user. Idora prefix declarations
are **not inherited**, so `WithEntities(Admin{})` fails with a `*PromotedPrefixError`:

```
idora: main.Admin has an IDPrefix method only because it is promoted from its embedded field User,
and prefix declarations are not inherited; declare IDPrefix on main.Admin itself, or register it
explicitly with idora.WithEntity[Admin]("abc")
```

Either fix works:

```go
func (Admin) IDPrefix() string { return "adm" }   // declare it on Admin itself, which shadows User's

idora.WithEntity[Admin]("adm")                    // or register it explicitly
```

Detection covers value and pointer receivers, embedded pointers (`struct{ *User }`), multiple
levels of embedding, embedded interfaces and generic types. An `IDPrefix` that `Admin` declares
itself, with either receiver kind, shadows the embedded one and is accepted. See
[How promoted methods are detected](#how-promoted-methods-are-detected).

#### Mixing the two

Both styles write into the same registry, so you can declare prefixes on the entities you own and
register the rest explicitly:

```go
gen, err := idora.New(
    idora.WithEntities(User{}, Order{}),        // your domain
    idora.WithEntity[thirdparty.Thing]("tpt"),  // a type you can't add a method to
)
```

The collision rules don't care where a registration came from. Two declarations claiming `usr`, a
declaration and a `WithEntity` claiming `usr`, or the same type registered both ways all fail with
a `*DuplicatePrefixError`. That last case fails **even if both agree on the prefix**, because one
type with two sources of truth is a mistake worth hearing about.

#### Pointer types

The registry keys a pointer type by its element type. `WithEntity[*User]` registers `User`, and
`Generate[*User]`, `PrefixOf[*User]` and `LookupPrefix(reflect.TypeFor[*User]())` all find it.
Registering both `User` and `*User` is therefore a duplicate. Code that works with pointers to
entities never has to dereference a type parameter to get an ID.

### Choosing the value format

ULID is the default. Pass `idora.WithUUIDv7()` to switch (or `idora.WithStrategy(idora.UUIDv7)`;
if you give several, the last one wins):

```go
gen, err := idora.New(idora.WithUUIDv7(), idora.WithEntity[User]("usr"))
```

|  | **ULID** (default) | **UUIDv7** |
| --- | --- | --- |
| Length | 26 characters | 32 characters |
| Alphabet | Crockford Base32 (`0-9`, `a-z` less `i l o u`) | hexadecimal (`0-9`, `a-f`) |
| Example | `01k2f9v8ta7m3x0qbrn4dzeh5c` | `0195e7b4a1c87f3d9e2b6c4a8f01d5e3` |
| Entropy | 80 bits | 74 bits |
| Standardised as | a community spec | RFC 9562 |
| Accepted by a `uuid` column | no | yes |
| Ordering within one millisecond | strictly increasing | unordered |

**Use the ULID default unless something forces your hand.** It holds the same 128 bits in six fewer
characters. That matters once the identifier is a primary key repeated across every foreign key,
index and log line. Its alphabet also leaves out `i`, `l`, `o` and `u`, the characters people
mis-transcribe when reading an identifier aloud or copying it off a screenshot.

**Choose UUIDv7 when something outside your control expects a real UUID**: a `uuid`-typed database
column, a schema that validates the format, or another service that parses the value. The value is
a conformant RFC 9562 version 7 UUID written without hyphens.

---

## The generate functions

```go
func Generate[T any](g *Generator) (string, error)      // the prefix T was registered with
func MustGenerate[T any](g *Generator) string           // the same, but panics if T is unregistered
func (g *Generator) GenerateFor(t reflect.Type) (string, error)
func (g *Generator) GenerateWithPrefix(prefix string) (string, error)  // the prefix you pass in
```

Go methods can't have type parameters, which is why the generic forms are package functions that
take the generator.

**Prefer `Generate[T]`.** The prefix lives in one place, your start-up configuration, instead of
being repeated as a string literal at every call site. A typo can't happen, and renaming a prefix
is a one-line change. Calling it for a type you forgot to register returns an error straight away
instead of quietly producing something wrong. It never makes up a prefix from the type's name:

```go
idora.Generate[User](gen)     // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c", nil
idora.Generate[Comment](gen)  // "", *PrefixNotRegisteredError: Comment was never registered
```

`MustGenerate[T]` is for code where an unregistered type is a programming error you'd rather
crash on, such as initialisation or tests. `GenerateFor` takes a `reflect.Type` for code that only
has one at run time.

`GenerateWithPrefix` is for cases where the prefix really isn't known until run time, such as a
plugin's own types or a prefix read from configuration:

```go
gen.GenerateWithPrefix("tmp") // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c", nil
gen.GenerateWithPrefix("t_1") // "", *InvalidPrefixError
```

It validates the prefix on every call, which costs a few nanoseconds and doesn't allocate. The
generic forms validated theirs once, inside `New`.

### Without any configuration

`idora.New()` with no options, or even the zero value `var gen idora.Generator`, is a working ULID
generator with no registered types, ready for `GenerateWithPrefix`. That's convenient for a
script or a test. Don't copy a `Generator` after first use (`go vet` catches it, because it
contains a mutex). Share a pointer instead.

### Reading prefixes back

Sometimes you need the prefix itself rather than a whole identifier, for example to check that an
identifier belongs to the entity a route expects:

```go
func isOrderID(gen *idora.Generator, id string) bool {
    prefix, err := idora.PrefixOf[Order](gen)
    return err == nil && strings.HasPrefix(id, prefix+"_")
}
```

| Function | Returns |
| --- | --- |
| `idora.PrefixOf[T](gen)` | `(string, error)`, failing with `*PrefixNotRegisteredError` |
| `gen.Prefix(reflect.Type)` | the same, for a run-time type |
| `gen.LookupPrefix(reflect.Type)` | `(string, bool)`, never failing |
| `gen.Registrations()` | an `iter.Seq2[reflect.Type, string]` of every registration |

Returned prefixes never include the trailing separator. `Registrations` yields in registration
order: option order, and within one `WithEntities` call, name order. The registry is immutable,
so every iteration yields the same sequence. Use `maps.Collect(gen.Registrations())` when you'd
rather have a map.

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

The timestamp comes first, which is why the identifiers sort by creation time as plain strings.
UUIDv7 has the same shape: 48 bits of timestamp, then randomness, printed as 32 hexadecimal
characters instead of 26 Base32 ones. Every character is ASCII, so `len(id)` is the length in
characters as well as bytes.

To split an identifier back apart, cut on the **first** underscore. A prefix can't contain one,
so the split is unambiguous:

```go
prefix, value, ok := strings.Cut(id, "_") // "usr", "01k2f9v8ta7m3x0qbrn4dzeh5c", true
```

### Sizing your database column

| | ULID | UUIDv7 |
| --- | --- | --- |
| Value | 26 | 32 |
| Separator | 1 | 1 |
| Prefix | 1–16 | 1–16 |
| **Total** | **28–43** | **34–49** |

With a typical three-character prefix, that's **30 characters** with ULID and **36** with UUIDv7.
Every identifier for a given entity is exactly the same length, so a fixed-width column is the
right choice, for example `char(30)` on PostgreSQL or SQL Server (`varchar(30)` works too; the
storage difference is a byte). The characters are all ASCII, so use a non-Unicode column type:
`char`, not `nchar`.

If you'd rather store UUIDv7 identifiers in a native `uuid` column, strip the prefix and store the
remainder. PostgreSQL accepts the 32 hex digits without hyphens as `uuid` input, and
`hex.DecodeString` gives you the 16 bytes for any driver that wants them. You don't lose the
prefix: it's a property of the type, so you already know it from the column you're reading.

---

## Errors

Failures are values. Every error the package returns matches exactly one sentinel under
`errors.Is`, and unpacks with `errors.As` into a typed error that carries the details as fields.
Messages start with `idora:`, are lower-case with no trailing punctuation, and say how to fix the
problem.

| Sentinel | Typed error (fields) | Returned when | Returned by | Fix |
| --- | --- | --- | --- | --- |
| `ErrInvalidPrefix` | `*InvalidPrefixError` (`Prefix`, `Reason`, `Index`, `Char`, `Length`, `EntityType`) | A prefix is empty, longer than 16 characters, or contains anything other than an ASCII letter or digit | `New`, or the `GenerateWithPrefix` call | Use 1–16 letters and digits, no underscore |
| `ErrDuplicatePrefix` | `*DuplicatePrefixError` (`Prefix`, `ExistingPrefix`, `EntityType`, `ConflictingEntityType`) | Two entity types register the same prefix (ignoring case), or one type is registered twice, whether by two `WithEntity` options, one of each style, or the same type listed twice | `New` | Give each type its own prefix, and register it exactly once |
| `ErrPrefixNotRegistered` | `*PrefixNotRegisteredError` (`EntityType`) | `Generate`, `PrefixOf` and friends are asked about a type with no prefix | The call | Register the type, or use `GenerateWithPrefix` |
| `ErrPromotedPrefix` | `*PromotedPrefixError` (`EntityType`, `Field`) | A type given to `WithEntities` only has `IDPrefix` through an embedded field | `New` | Declare `IDPrefix` on the type itself, or use `WithEntity` |

The first two and the last one are start-up failures by design. A duplicate prefix defeats the
whole point: you could no longer tell from `acc_01k2f9…` whether you were looking at an `Account`
or an `Accrual`, so the process refuses to start rather than let that through. Prefixes are
compared **ignoring ASCII case**, so `usr` and `USR` count as a collision.

`InvalidPrefixError.Reason` is one of `ReasonEmpty`, `ReasonTooLong`, `ReasonSeparator` or
`ReasonCharacter`. For the last two, `Index` and `Char` locate the offending character. When the
bad prefix came from an `IDPrefix` method, `EntityType` names the type that declared it, and so
does the message. An explicit `WithEntity` has a line of your code to point at; a declaration
doesn't, and *"contains the character '!' at index 3"* isn't much help across two hundred types.

The "same type registered twice" error has two wordings, because they're different mistakes with
different fixes. If both registrations agree, the message says the type *"cannot be registered a
second time"*. If they disagree, it says the type *"cannot also be registered with"* the second
prefix.

```go
_, err := idora.New(
    idora.WithEntity[Account]("acc"),
    idora.WithEntity[Accrual]("ACC"),
)

errors.Is(err, idora.ErrDuplicatePrefix) // true

var dup *idora.DuplicatePrefixError
if errors.As(err, &dup) {
    dup.Prefix                // "ACC"
    dup.ExistingPrefix        // "acc"
    dup.EntityType            // reflect.TypeFor[Accrual]()
    dup.ConflictingEntityType // reflect.TypeFor[Account]()
}
```

Two more failures are plain errors, because they're arguments no correct program passes: an
undefined `Strategy` value and a nil `reflect.Type` or nil `Entity`. Both are reported by `New`.

---

## Performance

Generating an identifier costs **one allocation** (the string that comes back) and **50–80
nanoseconds** on one goroutine. Measured with `go test -bench` on Go 1.27.3, darwin/arm64, Apple
M-series (M3 Max, 14 cores), median of five runs.

There's no UUID type in the Go standard library, so the baseline is the nearest fair stand-in:
16 bytes from `crypto/rand.Read` with the version 4 bits set, hex-encoded. That's what most
hand-rolled Go UUIDs do.

| Benchmark | ns/op | vs baseline | B/op | allocs/op |
| --- | ---: | ---: | ---: | ---: |
| Baseline: UUIDv4 via `crypto/rand` + hex *(stand-in)* | 224.9 | 1.00 | 32 | 1 |
| **`Generate[T]`, ULID** | **60.2** | **0.27** | **32** | **1** |
| **`GenerateWithPrefix`, ULID** | **48.5** | **0.22** | **32** | **1** |
| **`Generate[T]`, UUIDv7** | **77.0** | **0.34** | **48** | **1** |
| **`GenerateWithPrefix`, UUIDv7** | **66.5** | **0.30** | **48** | **1** |

The parallel variants share one generator across 14 goroutines with `b.RunParallel`. Their ns/op
is wall-clock time divided by the total number of operations, so lower means more throughput:

| Benchmark (parallel, 14 goroutines) | ns/op | Throughput | B/op | allocs/op |
| --- | ---: | ---: | ---: | ---: |
| Baseline: UUIDv4 via `crypto/rand` + hex *(stand-in)* | 515.4 | 1.9 M/s | 32 | 1 |
| `Generate[T]`, ULID | 212.3 | 4.7 M/s | 32 | 1 |
| `GenerateWithPrefix`, ULID | 203.6 | 4.9 M/s | 32 | 1 |
| `Generate[T]`, UUIDv7 | 27.1 | 36.9 M/s | 48 | 1 |
| `GenerateWithPrefix`, UUIDv7 | 25.9 | 38.6 M/s | 48 | 1 |

Reproduce them with:

```bash
go test -run '^$' -bench . -benchmem ./...
```

Four things make this possible:

- **One allocation, always.** The prefix, separator and value are written into a fixed-size array
  on the stack and converted to a string once. There's no `strings.Builder`, no concatenation and
  no intermediate slice, so the bytes above are the string itself and nothing else. A test
  (`testing.AllocsPerRun`) fails if that ever changes, for both formats and for both registered
  and ad-hoc prefixes.
- **Buffered entropy.** Asking the operating system for ten random bytes per identifier would cost
  more than everything else put together; the baseline above is mostly that cost. Instead, Idora
  draws 1 KiB blocks from `crypto/rand` and hands out slices, refilling about once per hundred
  identifiers. The bytes come from the same source, so the quality is the same and only the cost
  changes.
- **No lock for UUIDv7.** UUIDv7 has no ordering state, so its entropy blocks live in a
  `sync.Pool`, which keeps one per logical processor. That's the closest thing Go has to
  thread-local storage, and it's why UUIDv7 scales almost linearly with cores.
- **A short critical section for ULID.** The monotonic state has to be shared to stay monotonic,
  so a ULID takes a per-generator `sync.Mutex`, held only long enough to compare a timestamp and
  increment a counter. The clock is read and the text encoded outside it.

The ULID lock is the one thing that doesn't scale, and that's a deliberate trade. Contention from
14 goroutines hammering one generator brings ULID throughput down to roughly single-goroutine
speed. It's still more than double the baseline, at close to five million identifiers a second. A
lock-free design was measured: even a bare contended atomic add costs 70–85 ns/op on this
machine, so it would at best halve that figure, at the price of epoch objects and real
correctness risk. If you need more ULID throughput than that, give each worker its own
`Generator`. Each one is its own monotonic stream (see below).

---

## Concurrency and monotonicity

A `*Generator` is immutable once `New` returns and safe to share across every goroutine. There's
no process-wide lock anywhere.

Go has no thread-local storage, so where Idora.NET keeps one ULID stream per thread, Idora.Go keeps
**one ULID stream per `Generator`**, behind a short critical section. That gives a *stronger*
guarantee than per-thread state does:

- **Every ULID from one generator sorts strictly after every ULID it returned before**, whichever
  goroutines made the calls. If one call returns before another starts, on any two goroutines, the
  second identifier is greater. (Calls that overlap in time are ordered by which acquired the
  lock first.) The test suite checks this directly by serialising many goroutines through one
  generator.
- **Separate generators are separate streams.** Two generators are ordered relative to each other
  by millisecond only, like two processes.
- **UUIDv7 values are ordered by millisecond only**, within a generator or across them. That's
  what RFC 9562 specifies.

---

## Questions you might reasonably have

**How strong is the ordering guarantee?**
Identifiers are ordered by the millisecond they were created in, across goroutines, generators
and processes. Within one millisecond, ULIDs from one `Generator` are *strictly* increasing: the
random field is incremented rather than redrawn, as the ULID spec's monotonic mode describes. See
[Concurrency and monotonicity](#concurrency-and-monotonicity) for exactly where the line falls.

**Do identifiers stay in order across servers in different time zones?**
Yes. Time zones play no part here. Both formats define their timestamp as milliseconds since the
Unix epoch, an absolute instant with no zone attached, and this package reads it with
`time.Now().UnixMilli()`, which is defined in UTC and ignores `time.Local` and `TZ` completely. A
server in Sydney and a server in Los Angeles that agree on the instant produce identifiers that
interleave in true creation order. The test suite checks this with a non-UTC local zone; run it
with `TZ=Australia/Sydney` to use a real zone database entry.

Cross-server ordering does depend on **clock skew**, meaning servers that disagree about what
the instant is. Ordering between two machines is only as accurate as the gap between their clocks.
With typical NTP synchronisation that's a few milliseconds, so identifiers created seconds apart on
different hosts order correctly, while two created within a few milliseconds of each other may
not. That's true of distributed timestamps in general, not something specific to this package. If
you need a total order across machines that doesn't depend on their clocks, you need a sequencer,
not an identifier format.

**What if the clock goes backwards?**
An NTP correction or a resumed VM snapshot won't produce an out-of-order ULID. The generator
keeps the previous millisecond rather than accepting the earlier reading, so the sequence never
goes backwards. Identifiers made during that window carry a timestamp a few milliseconds ahead of
the wall clock, which is the lesser evil. A clock reading before 1970 is clamped to zero.

**Could two identifiers collide?**
Not in any practical sense. Within one generator, ULIDs can't collide at all, because the counter
increments. Across generators, two identifiers collide only if 80 bits (ULID) or 74 bits (UUIDv7)
of cryptographically strong randomness coincide inside the same millisecond. You'd need to be
generating something like a billion identifiers per millisecond before that was worth thinking
about.

**Are they unguessable? Can I use one as a security token?**
The random portion is cryptographically strong, so an identifier can't be predicted from the ones
around it. But the timestamp is plainly readable, and identifiers are meant to appear in logs, URLs
and support tickets. **Treat them as public.** Authorise every request properly, and never use an
identifier as a bearer token or a password-reset secret.

**What happens if `crypto/rand` fails?**
From Go 1.24 it can't: `crypto/rand.Read` crashes the program rather than return an error. On Go
1.23 an error means the operating system can't supply entropy, and Idora panics. Handing out
predictable identifiers would be worse.

**Can I use these as primary keys with `database/sql`, GORM, sqlc or ent?**
Yes, they're ordinary strings. Assign one when you construct the entity, or in a `BeforeCreate`
hook, rather than letting the database generate the key. Because the values are time-ordered,
B-tree inserts stay at the end of the index. That's exactly the behaviour you want, and it's the
reason not to use a random UUID.

**Are identifiers case-sensitive?**
Yes. Compare them with `==`. Generated values are always lowercase, and neither alphabet contains
two characters that differ only by case. Your prefix is emitted exactly as you registered it, so
registering `USR` gives you `USR_01k2f9…`. Registering `usr` *and* `USR` is a collision.

**Why is the maximum prefix 16 characters?**
It's long enough for a readable abbreviation and short enough that the prefix never dominates the
identifier. If you find yourself wanting more, the prefix is probably trying to be a description.

**Why is `Generate` a function and not a method?**
Go methods can't have type parameters. `idora.Generate[User](gen)` is the closest Go gets to
`gen.Generate<User>()`. `gen.GenerateFor(reflect.TypeFor[User]())` is the method form if you
prefer one.

**Which registration style should I use?**
Use `WithEntity` if you have a handful of entities, or for types you can't add a method to. Switch
to `IDPrefix` plus `WithEntities` once keeping a separate list in step with the domain becomes a
chore of its own. Mixing them costs nothing, and they make no difference at run time: both end up
as entries in the same map before `New` returns.

**Can I override a declared prefix with `WithEntity`?**
No, that's a `*DuplicatePrefixError`. Allowing it would give a type whose prefix depends on the
order two options ran in, and the usual reason to want it, a copy-paste slip, is exactly what the
error is there to catch. If you need a different prefix, change the method. Note that
`WithEntity` alone is fine on a type that has an `IDPrefix` method: the method is only consulted
when you pass the type to `WithEntities`.

### How promoted methods are detected

Go compiles every promoted method into a small wrapper function, and the runtime reports that
wrapper's source position as `<autogenerated>`. A method you declared reports its real file.
`WithEntities` looks up the `IDPrefix` in the type's own method set if there is one (a value
receiver, or a value-receiver method promoted from an embedded field), and otherwise in the
pointer type's (a pointer receiver). It then asks `runtime.FuncForPC(...).FileLine` where that
function lives. It checks the value method set first because `*T`'s copy of a value-receiver
method is also an autogenerated wrapper.

That runtime check is confirmed structurally. A method can only be promoted if the type is a struct
with an embedded field that provides `IDPrefix`, so a type without such a field is accepted
whatever the runtime says, so a change in how a future compiler positions its wrappers could at
worst make a type with an embedded provider fail loudly at start-up, never let one through
silently. The test suite covers generic types too, and detection behaves the same with
`-trimpath`, `-ldflags=-s` and inlining disabled.

---

## Contributing

From this directory:

```bash
gofmt -l .                                     # must print nothing
go vet ./...
go test -race ./...                            # the full suite, including the shared conformance vectors
TZ=Australia/Sydney go test -run 'UTC|Zone' ./...
go test -run '^$' -bench . -benchmem ./...     # the performance table above
```

The suite uses only the standard `testing` package. It covers the success, failure and edge cases
of every exported identifier, plus the encoders, the monotonic step function, the entropy buffer,
the promotion detector and the one-allocation guarantee directly. It also loads
[`../spec/test-vectors.json`](../spec/test-vectors.json) and reproduces every vector. A copy of the
module fetched through the Go proxy doesn't contain the spec directory, so that one test skips
there, but inside the repository a missing file is a failure.

## Releasing

Go modules are released by tag. Because this module is in the `Idora.Go` subdirectory, the tag
carries that prefix:

```bash
git tag Idora.Go/v1.0.0
git push origin Idora.Go/v1.0.0
```

pkg.go.dev picks the version up on the first `go get` or proxy request. A published version is
cached by the Go module proxy forever, so a bad release can be retracted (with a `retract`
directive in a later `go.mod`) but never replaced.

## License

MIT. See [LICENSE](LICENSE) (a copy of the repository's [LICENSE](../LICENSE), kept inside the
module so pkg.go.dev displays the documentation).
