# The Idora specification

This document is the contract every Idora package implements. The packages are written separately,
in the idiom of their own language, but a conforming package produces identifiers that are
indistinguishable from any other package's — same format, same ordering, same validation rules,
same failure modes. An identifier minted by Idora.Go can be parsed, sorted and validated by
Idora.JS without either side knowing where it came from.

[`test-vectors.json`](test-vectors.json) holds the shared conformance vectors. Every package's test
suite loads that file and reproduces it exactly.

The key words **must**, **must not**, **should** and **may** are used in the RFC 2119 sense.

---

## 1. The identifier

```
<prefix>_<value>
```

| Part | Rule |
| --- | --- |
| prefix | 1–16 ASCII letters or digits (`[A-Za-z0-9]{1,16}`), emitted exactly as registered |
| separator | a single underscore, `_` |
| value | a 26-character lowercase ULID (the default) or a 32-character lowercase UUIDv7 |

Every character of an identifier is ASCII. The prefix cannot contain the separator, so splitting on
the **first** underscore always recovers the two parts unambiguously.

## 2. Prefix validation

A prefix is checked in this order, and the first failing rule is reported:

1. **Missing** — a null/nil/undefined prefix, where the language can express one. Report it the way
   the language reports a missing argument (`ArgumentNullException`, `TypeError`,
   `NullPointerException`). Languages whose type system rules it out need no check.
2. **Empty** — length zero.
3. **Too long** — longer than 16 characters.
4. **Bad character** — the first character that is not `A–Z`, `a–z` or `0–9`. The report **must**
   include that character and its zero-based index, and **must** use a distinct message when the
   character is the separator `_`, explaining that the separator divides the prefix from the value.

Non-ASCII letters and digits (`é`, `١`) are bad characters. Use an explicit ASCII range check, never
the language's Unicode-aware "is letter" predicate.

Lengths and indexes are measured in the language's native string unit — UTF-16 code units in .NET,
Java and JavaScript, Unicode code points in Go and Rust. For any prefix that could be valid the
choice makes no difference, because every valid prefix is ASCII, and the index of the first bad
character is the same in every unit, because everything before it is ASCII. The only visible
difference is which rule rejects an over-long string of characters outside the Basic Multilingual
Plane, such as emoji — "too long" in one package, "bad character at index 0" in another. Both
reject it, which is what matters; the conformance vectors deliberately avoid that case.

Validation runs once per registration at start-up, and on every ad-hoc generation call (§5.2). It
**must not** allocate on the success path where the language makes that practical.

## 3. Values

Both formats begin with a 48-bit count of milliseconds since the Unix epoch, read from the wall
clock **in UTC**. Time zones play no part anywhere; a package **must not** read local time.

### 3.1 ULID (default)

128 bits: a 48-bit timestamp followed by an 80-bit random field, written as 26 characters of
lowercase [Crockford Base32](https://www.crockford.com/base32.html):

```
0123456789abcdefghjkmnpqrstvwxyz
```

The encoding treats the 128 bits as one big-endian number, right-aligned in 130 bits, so the first
character never exceeds `7`. Characters 0–9 carry the timestamp and characters 10–25 the random
field.

**Monotonicity.** A ULID stream keeps the last timestamp it used (`last`) and the last random field.
For each new value, given the current clock reading `now`:

- `now > last`: set `last = now` and draw a fresh 80-bit random field.
- otherwise (the same millisecond, or a clock that has stepped backwards): keep `last` and increment
  the 80-bit random field by one, carrying from the low bits into the high bits.
- if the increment overflows 80 bits: set `last = last + 1` and draw a fresh random field.

The consequence is that every value from one stream sorts strictly after the one before it, and a
backwards clock step never produces an out-of-order value.

The scope of a stream is chosen per language to avoid contention (§7), and must be documented.

### 3.2 UUIDv7

A conformant [RFC 9562](https://www.rfc-editor.org/rfc/rfc9562) version 7 UUID, written as 32
lowercase hexadecimal characters **without hyphens**:

| Octets | Content |
| --- | --- |
| 0–5 | the 48-bit timestamp, big-endian |
| 6 | `0x70 \| (random & 0x0F)` — version 7 |
| 7 | random |
| 8 | `0x80 \| (random & 0x3F)` — RFC variant |
| 9–15 | random |

That leaves 74 random bits. UUIDv7 values are not monotonic within a millisecond; only their
millisecond is ordered.

### 3.3 Entropy

Random bits **must** come from the operating system's cryptographically secure generator, or the
language's standard wrapper around it. Packages **should** draw entropy in blocks (1 KiB is the
reference size) and hand out slices of the block, because asking the operating system for ten bytes
at a time dominates the cost of generating an identifier. Buffering changes the cost, not the
quality: the bytes still come from the same source.

## 4. Registration

A **registry** maps entity types to prefixes. It is built once, during start-up, and is immutable
afterwards.

"Entity type" means whatever the language uses to name a type at run time: a `System.Type`, a
`Class<?>`, a JavaScript constructor, a Go `reflect.Type`, a Rust `TypeId`. The entity type is a key
and nothing more; it needs no base class and no interface beyond what §4.2 asks for.

### 4.1 Explicit registration

Associates one entity type with one prefix, validating the prefix (§2) as it goes.

### 4.2 Declarative registration

Each package offers a way for the entity type to **declare its own prefix**, using the language's
native mechanism: an attribute (.NET), a decorator (TypeScript), an annotation (Java), a method
(Go), a trait with an associated constant (Rust).

Declarations are collected from sources the caller **names explicitly** — an assembly, a module
namespace, a package, a list of types. A package **must not** discover entities implicitly by
walking everything loaded into the process: that sees only what happens to be loaded, costs time on
every start-up, and lets unrelated code register prefixes into the application.

When a source yields several types, they are registered in **ordinal order of their fully
qualified name**, so a clash between two of them is reported the same way round on every run.

A declaration **must not be inherited**: a subclass does not take its base's prefix.

When a declared prefix is invalid, the error **must name the type carrying the declaration**, since
there is no line of the caller's code to point at.

### 4.3 Collisions

Both routes feed the same registry and police each other. Registration **must** fail when:

- **the entity type is already registered** — checked first. The message differs depending on
  whether both registrations agree on the prefix ("registered a second time") or disagree ("cannot
  also be registered with …").
- **the prefix is already registered to a different entity type**, comparing prefixes with ASCII
  case-insensitivity, so `usr` and `USR` collide. The error names both types.

These are start-up failures. Nothing is overridden silently, in either order.

## 5. Generation

### 5.1 For a registered entity type

Returns `<registered prefix>_<value>`. When the type has no registered prefix the call **must** fail
with *entity prefix not registered*. It **must not** invent a prefix from the type's name.

This is the preferred form: the prefix lives in one place and is validated once.

### 5.2 With an ad-hoc prefix

Validates the prefix (§2) on every call, then returns `<prefix>_<value>`. For prefixes known only at
run time.

### 5.3 Reading the registry

The registry is exposed read-only:

- the prefix for an entity type, failing with *entity prefix not registered* if absent;
- a non-failing lookup (`TryGetPrefix`, `undefined`, `Optional`, `(string, bool)`, `Option`);
- every registration.

Returned prefixes carry no trailing separator.

## 6. Errors

Every package reports the same three failures, as types (or error values) that callers can tell
apart, sharing a common base where the language has one:

| Failure | Raised when | Carries |
| --- | --- | --- |
| invalid entity prefix | §2 fails | the prefix |
| duplicate entity prefix | §4.3 fails | the prefix, the entity type being registered, the conflicting entity type |
| entity prefix not registered | §5.1 or §5.3 for an unregistered type | the entity type |

Messages say what went wrong **and how to fix it**.

## 7. Concurrency

A built generator is immutable and safe to share across every thread, goroutine, task or worker in
the process. Generation **must not** take a process-wide lock on the hot path. Per-thread state
(or a short, per-generator critical section where the language has no thread-local storage) is
expected; whichever is used determines the scope of ULID monotonicity and is documented.

## 8. Conformance vectors

[`test-vectors.json`](test-vectors.json) contains:

| Key | What it checks |
| --- | --- |
| `ulid` | encoding `timestampMs` with the 80-bit random field `randomHex` gives `expected` |
| `uuidv7` | laying out `timestampMs` with octets 6–15 taken from `randomHex` gives `expected` |
| `ulidMonotonic` | the next state after `previous` at clock reading `nowMs` (a `null` `randomHex` means "freshly drawn — do not compare") |
| `prefixes.valid` | prefixes that must be accepted |
| `prefixes.invalid` | prefixes that must be rejected, with the `reason` (`empty`, `tooLong`, `separator`, `character`) and offending `index` |
| `prefixes.collidingPairs` | pairs that must collide when registered to two different types |
| `prefixes.distinctPairs` | pairs that must not |

Random fields are big-endian hexadecimal.

## 9. Performance

Each package ships a benchmark that measures, at minimum, generation for a registered type and
with an ad-hoc prefix, for both formats, against the language's nearest built-in identifier
(`Guid.NewGuid()`, `crypto.randomUUID()`, `UUID.randomUUID()`, and so on). Results are published in
the package README together with the command that reproduces them.
