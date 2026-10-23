# Idora.Java

Entity identifiers that tell you what they are, and sort by when they were made, for Java 17+.

```
usr_01k2f9v8ta7m3x0qbrn4dzeh5c
ord_01k2f9v8tb9p5r2xnc7fkw3m0d
inv_01k2f9v8tc4h8w6zqm1vptb2j9
```

A prefix you register once, an underscore, and a lowercase [ULID](https://github.com/ulid/spec) or
[UUIDv7](https://www.rfc-editor.org/rfc/rfc9562). No magic strings at the call site, no
configuration file, and **zero runtime dependencies**.

Idora.Java is part of [Idora](../README.md), a family of libraries that implement one
[specification](../spec/SPECIFICATION.md) and produce identifiers that are indistinguishable from
each other: same format, same ordering, same validation rules, same failure modes. Its siblings are
[Idora.NET](../Idora.NET), [Idora.JS](../Idora.JS), [Idora.Go](../Idora.Go) and
[Idora.Rust](../Idora.Rust). Every package's test suite reproduces the shared
[conformance vectors](../spec/test-vectors.json).

---

## Why not just use `UUID.randomUUID()`?

Two reasons, and they compound.

**An opaque identifier tells you nothing.** When `f47ac10b-58cc-4372-a567-0e02b2c3d479` turns up in
a log line, a stack trace or a customer's email, you cannot tell whether it is a user, an order or
an invoice. You go and look. With `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` you already know, and so does
everybody else on the team. This is why Stripe, GitHub and others put prefixes on their public
identifiers.

**A random identifier is hostile to your database.** `UUID.randomUUID()` produces version 4 values
scattered uniformly across the keyspace, so every insert lands on a different page of the index.
Pages split, the index fragments, and the working set grows until it stops fitting in memory. ULID
and UUIDv7 both begin with a millisecond timestamp, so consecutive inserts land next to each other
at the end of the index: the access pattern an auto-incrementing column gives you, without needing
the database to hand out the numbers.

You also get creation-time ordering for free. Sorting the identifiers as plain strings puts the rows
in the order they were created, with no separate timestamp column and no secondary index.

---

## Install

Maven:

```xml
<dependency>
  <groupId>io.github.srkleraimbot</groupId>
  <artifactId>idora</artifactId>
  <version>1.0.0</version>
</dependency>
```

Gradle (Kotlin DSL):

```kotlin
implementation("io.github.srkleraimbot:idora:1.0.0")
```

Gradle (Groovy DSL):

```groovy
implementation 'io.github.srkleraimbot:idora:1.0.0'
```

Requires Java 17 or later. The jar has no dependencies beyond `java.base`. It is a named JPMS
module; on the module path, add:

```java
requires io.github.srkleraimbot.idora;
```

---

## Quick start

Build a generator once, at start-up, with your entity prefixes:

```java
import io.github.srkleraimbot.idora.IdoraGenerator;

IdoraGenerator ids = IdoraGenerator.builder()
    .registerEntity(User.class, "usr")
    .registerEntity(Order.class, "ord")
    .registerEntity(Invoice.class, "inv")
    .build();
```

Or declare the prefix on the entity and collect them in one line (see
[With an annotation](#with-an-annotation)):

```java
@EntityPrefix("usr")
public final class User { /* ... */ }

IdoraGenerator ids = IdoraGenerator.builder()
    .registerEntitiesFromPackageOf(User.class)
    .build();
```

Then use it wherever you create an entity:

```java
ids.generate(Order.class);   // "ord_01k2f9v8tb9p5r2xnc7fkw3m0d"
```

That is the whole API for the common case. The generator is immutable and thread-safe, so keep one
instance for the life of the application: a `static final` field, or a singleton bean in whatever
container you use.

```java
// Spring, for example
@Bean
IdoraGenerator idoraGenerator() {
    return IdoraGenerator.builder().registerEntitiesFromPackageOf(User.class).build();
}
```

---

## Registering entity prefixes

There are two ways to say that a `User` gets `usr`. They do the same thing, they can be mixed in one
builder, and both are read once while the generator is built and never again.

Whichever you use, a prefix must be **1 to 16 ASCII letters or digits**. The underscore is excluded
because it separates the prefix from the value; allowing one inside a prefix would make an
identifier impossible to split back apart unambiguously. Non-ASCII letters and digits (`é`, `١`)
are rejected too. Every registration is validated as it is made, so a mistake throws on the line
that caused it, at start-up, rather than surfacing later as a wrong prefix in your database. See
[Error handling](#error-handling).

### Explicitly

`registerEntity(Class, String)` maps a type to the prefix its identifiers carry. The type is used
purely as a key: it needs no base class, no interface and no annotation. A class, record, enum or
interface all work.

```java
IdoraGenerator ids = IdoraGenerator.builder()
    .registerEntity(User.class, "usr")
    .registerEntity(Order.class, "ord")
    .build();
```

### With an annotation

Past a handful of entities, that list stops reading as a single source of truth and starts reading
as a wall. `@EntityPrefix` puts the prefix next to the type it describes:

```java
@EntityPrefix("usr")
public final class User {
    private String id;
    // ...
}
```

and you tell the builder where to collect them from:

| Method | Registers |
| --- | --- |
| `registerEntitiesFromPackageOf(User.class)` | Every `@EntityPrefix` type in `User`'s package and its sub-packages, within the directory, jar or named module `User` was loaded from. Point it at one of your entities. |
| `registerEntities(User.class, Order.class, ...)` | Exactly the types you list (also accepts a `Collection`). Each must carry `@EntityPrefix` itself. |

Either way the types are registered in **ordinal order of their fully qualified (binary) name**, so
a clash between two of them is reported the same way round on every run, whatever order the file
system or your argument list happened to produce.

The scan runs once, while the generator is built, and what it finds goes into the same immutable
lookup as an explicit registration. **Nothing reflects over anything once the generator exists**, so
`generate(User.class)` costs exactly the same whichever way `User` was registered.

The scan is careful about what it touches:

- It reads class files as bytes and **loads only the ones that mention `@EntityPrefix`**. The other
  classes in the package are never loaded, so they cost no metaspace and cannot fail to link.
- Classes it does load are loaded **without being initialised**: no static initialiser runs.
- Exploded directories (`target/classes`), jars and **named JPMS modules** (through the module's
  own `ModuleReader`) are all supported. Classes nested under a prefix inside a jar, as in Spring
  Boot's `BOOT-INF/classes/`, are found when the class loader exposes them as `jar:` URLs.
- Non-public, nested and private nested types, records, interfaces and enums are all found.
- The annotation is **not inherited** (it is deliberately not `@Inherited`, and is read with
  `getDeclaredAnnotation`), so a subclass never quietly takes its base type's prefix.

#### Why you name the package

The obvious alternative is for the library to walk the class path and find your entities for you.
It is one line shorter and considerably worse:

- **It walks everything.** Every class in every jar on the class path, on every start-up, for a
  feature whose entire premise is that it costs nothing at run time.
- **It sees only what it can see.** Class paths are not uniformly listable: nested jars, custom
  class loaders and module layers each hide classes in their own way. An entity the walk misses is
  not a start-up failure; it is an `EntityPrefixNotRegisteredException` on whichever request first
  creates that kind of entity, possibly in production.
- **It is not your registry any more.** Any library on the class path could register a prefix into
  your application, or collide with one of yours and stop it starting.

Naming the package turns all three into a problem you cannot have. Pointing at a class rather than
writing the package name as a string means a package rename keeps working, and a type that moves
fails to compile rather than fails at start-up.

### Mixing the two

Both styles write into the same registry, so you can annotate the entities you own and register the
rest explicitly:

```java
IdoraGenerator ids = IdoraGenerator.builder()
    .registerEntitiesFromPackageOf(User.class)        // your domain
    .registerEntity(ThirdPartyThing.class, "tpt")     // a type you cannot annotate
    .build();
```

The collision rules do not care where a registration came from. Two annotations claiming `usr`, an
annotation and a `registerEntity` call claiming `usr`, the same package scanned twice, or the same
type registered both ways all fail with a `DuplicateEntityPrefixException`. The last one fails
**even if both agree on the prefix**, because one type with two sources of truth is a mistake worth
hearing about.

---

## Choosing the value format

ULID is the default. Call `useUuidV7()` to switch:

```java
IdoraGenerator ids = IdoraGenerator.builder()
    .useUuidV7()
    .registerEntity(User.class, "usr")
    .build();
```

(`useUlid()` and `strategy(IdGenerationStrategy)` exist too, for when you would rather the choice
were written down or comes from configuration.)

|  | **ULID** (default) | **UUIDv7** |
| --- | --- | --- |
| Length | 26 characters | 32 characters |
| Alphabet | Crockford Base32 (`0-9`, `a-z` less `i l o u`) | hexadecimal (`0-9`, `a-f`) |
| Example | `01k2f9v8ta7m3x0qbrn4dzeh5c` | `0195e7b4a1c87f3d9e2b6c4a8f01d5e3` |
| Entropy | 80 bits | 74 bits |
| Standardised as | a community spec | RFC 9562 |
| Parses as a `java.util.UUID` | no | yes, once the hyphens are put back |
| Ordering within one millisecond | strictly increasing per thread | unordered |

**Use the ULID default unless something forces your hand.** It holds the same 128 bits in six fewer
characters, which matters once the identifier is a primary key repeated across every foreign key,
index and log line. Its alphabet also omits `i`, `l`, `o` and `u`, the characters people
mis-transcribe when reading an identifier aloud or copying it off a screenshot.

**Choose UUIDv7 when something outside your control expects a real UUID**: a `uuid`-typed database
column, a schema that validates the format, or another service that parses the value. The value is
printed without hyphens; to get a `java.util.UUID`:

```java
String hex = id.substring(id.indexOf('_') + 1);
UUID uuid = new UUID(Long.parseUnsignedLong(hex, 0, 16, 16), Long.parseUnsignedLong(hex, 16, 32, 16));
uuid.version();   // 7
```

---

## The two `generate` methods

```java
String generate(Class<?> entityType);   // uses the prefix entityType was registered with
String generate(String entityPrefix);   // uses the prefix you pass in
```

**Prefer `generate(Class)`.** The prefix lives in one place, your start-up configuration, instead
of being repeated as a string literal at every call site, so a typo is impossible and renaming a
prefix is a one-line change. Calling it for a type you forgot to register throws immediately rather
than quietly producing something wrong:

```java
ids.generate(User.class);      // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
ids.generate(Comment.class);   // throws EntityPrefixNotRegisteredException: Comment was never registered
```

It never invents a prefix from the class name: that would change silently the day somebody renamed
the class.

`generate(String)` exists for the cases where the prefix genuinely is not known until run time: a
plugin's own types, or a prefix read from configuration.

```java
ids.generate("tmp");           // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c"
```

It validates the prefix on every call, which costs a nanosecond or so. `generate(Class)` validated
once, at start-up. A generator with nothing registered (`IdoraGenerator.create()`) supports only
this form.

### Reading prefixes back

`ids.registry()` returns the read-only `EntityPrefixRegistry`, for when you need the prefix itself
rather than a whole identifier. Checking that an identifier belongs to the entity a route expects,
for instance:

```java
EntityPrefixRegistry prefixes = ids.registry();

boolean isOrderId = id.startsWith(prefixes.getPrefix(Order.class) + "_");
Optional<String> maybe = prefixes.findPrefix(Comment.class);      // Optional.empty()
Map<Class<?>, String> all = prefixes.registrations();             // unmodifiable, in registration order
```

`getPrefix` throws `EntityPrefixNotRegisteredException` for an unregistered type; `findPrefix`
returns an empty `Optional`. Prefixes come back exactly as registered, without the separator.

---

## What an identifier is made of

```
    usr _ 01k2f9v8ta 7m3x0qbrn4dzeh5c
    └┬┘ ┬ └────┬───┘ └───────┬───────┘
     │  │      │             │
     │  │      │             └─ 80 bits of randomness, 16 characters
     │  │      └─ 48-bit millisecond timestamp, 10 characters
     │  └─ separator, always a single underscore
     └─ the prefix you registered, 1-16 characters
```

The timestamp comes first, which is what makes the identifiers sort by creation time as plain
strings. UUIDv7 has the same shape: 48 bits of timestamp, then the version nibble (`7`), the variant
bits and 74 bits of randomness, printed as 32 hexadecimal characters instead of 26 Base32 ones.
Every character of every identifier is ASCII.

### Sizing your database column

| | ULID | UUIDv7 |
| --- | --- | --- |
| Value | 26 | 32 |
| Separator | 1 | 1 |
| Prefix | 1-16 | 1-16 |
| **Total** | **28-43** | **34-49** |

For a typical three-character prefix that is **30 characters** with ULID and **36** with UUIDv7.
Every identifier for a given entity is exactly the same length, so a fixed-width column is the right
choice: `char(30)` (or `varchar(30)`; the difference is a byte). The characters are all ASCII, so a
non-Unicode type and a binary or `C` collation are fine, and make comparisons cheaper.

If you would rather store UUIDv7 identifiers in a native `uuid` column, strip the prefix and convert
the remainder as shown [above](#choosing-the-value-format). The prefix is a property of the type, so
you already know it from the column you are reading.

---

## Error handling

Every Idora exception extends `IdoraException`, an unchecked, **sealed** base class, so you can
catch that one type, or `switch` exhaustively over the three subclasses.

| Exception | Thrown when | Thrown at | Fix |
| --- | --- | --- | --- |
| `InvalidEntityPrefixException` | A prefix is empty, longer than 16 characters, or contains something other than an ASCII letter or digit | Start-up (registration), or on the `generate(String)` call | Use 1-16 letters and digits, no underscore |
| `DuplicateEntityPrefixException` | One prefix is registered for two types (ignoring ASCII case), or one type is registered twice, by any combination of `registerEntity`, annotations and scans | Start-up | Give each type its own prefix, and register it exactly once |
| `EntityPrefixNotRegisteredException` | `generate(Class)` or `registry().getPrefix(Class)` for a type with no prefix | On the call | Register the type, or use `generate(String)` |

Outside that hierarchy, as Java convention expects:

- a `null` argument throws `NullPointerException` whose message is the parameter name;
- `registerEntities` given a type without `@EntityPrefix`, or `registerEntitiesFromPackageOf` given a
  primitive, an array, a class in the unnamed package, or a class loaded from somewhere that cannot
  be listed, throws `IllegalArgumentException` saying what to do instead.

The duplicate and invalid cases are start-up failures by design. A duplicate prefix defeats the
whole point (you could no longer tell from `acc_01k2f9...` whether you were looking at an `Account`
or an `Accrual`), so the generator refuses to build rather than let it through. Prefixes collide
**ignoring ASCII case**, so `usr` and `USR` count as a collision. The comparison is ASCII-only on
purpose: a locale-sensitive `toLowerCase()` would let `INV` and `inv` coexist on a machine with a
Turkish default locale, and nowhere else.

The messages say what went wrong and how to fix it. Registering one type twice with the same prefix
says "registered a second time" and lists where the repeat might come from (including scanning a
package twice); registering it with a different prefix says which two prefixes disagree. When a bad
prefix comes from an `@EntityPrefix` annotation, the message names the annotated type. An explicit
registration leaves a stack trace pointing at the line you wrote; an annotation leaves nothing, and
*"contains the character '!' at index 3"* is not much help across two hundred classes.

Each exception carries the details as getters, not just in the message:

```java
try {
    IdoraGenerator.builder()
        .registerEntity(Account.class, "acc")
        .registerEntity(Accrual.class, "ACC");
} catch (DuplicateEntityPrefixException e) {
    e.getPrefix();                  // "ACC"
    e.getEntityType();              // Accrual.class
    e.getConflictingEntityType();   // Account.class
}

try {
    ids.generate("user_account");
} catch (InvalidEntityPrefixException e) {
    e.getReason();                  // Reason.SEPARATOR  (EMPTY, TOO_LONG, SEPARATOR or CHARACTER)
    e.getIndex();                   // OptionalInt[4]
    e.getEntityType();              // Optional.empty(), or the annotated type
}
```

---

## Performance

Generating an identifier costs **about 27 nanoseconds** and allocates **only the string that comes
back**: about six times faster than `UUID.randomUUID().toString()`, with a third of the garbage.

Measured with JMH 1.37 on OpenJDK 21.0.13 (Homebrew), Apple M-series (M3 Max, 14 cores),
macOS 27.0.1, default G1 collector, `-wi 3 -i 5 -f 1 -w 1s -r 1s -prof gc`:

| Benchmark | Threads | Mean (ns/op) | Ratio | Allocated (B/op) |
| --- | ---: | ---: | ---: | ---: |
| `UUID.randomUUID().toString()` *(baseline)* | 1 | 176.5 ± 7.2 | 1.00 | 208 |
| `"cus_" + UUID.randomUUID()` | 1 | 197.9 ± 15.7 | 1.12 | 264 |
| **`generate(Class)`, ULID** | 1 | **27.2 ± 0.2** | **0.15** | **72** |
| **`generate(String)`, ULID** | 1 | **26.7 ± 0.3** | **0.15** | **72** |
| **`generate(Class)`, UUIDv7** | 1 | **46.1 ± 1.0** | **0.26** | **82** |
| **`generate(String)`, UUIDv7** | 1 | **48.5 ± 1.2** | **0.27** | **82** |
| `UUID.randomUUID().toString()` | 4 | 1047.4 ± 74.9 | 5.93 | 176 |
| **`generate(Class)`, ULID** | 4 | **28.8 ± 0.4** | **0.16** | **72** |
| `generate(Class)`, ULID | 14 (max) | 824.6 ± 480.4 | 4.67 | 72 |
| `System.currentTimeMillis()` alone | 14 (max) | 654.7 ± 631.5 | 3.71 | 0 |

Per-thread times are averages. At four threads Idora takes the same time per identifier as on one,
because threads do not share state; `UUID.randomUUID()` gets six times slower, because every thread
queues on the JDK's single shared `SecureRandom`. The two max-thread rows were measured on a
machine with a load average of 6 to 10 from other work. With every core busy, **the wall-clock read
itself** no longer scales on this host (the last row is that read and nothing else), and it is most
of the cost of the row above it. That is a property of the platform, not of the generator, and the
error bars show how noisy it is. Your own hardware will give you your own numbers:

```bash
mvn -B -pl benchmarks -am package -DskipTests
java -jar benchmarks/target/benchmarks.jar -wi 3 -i 5 -f 1 -w 1s -r 1s -prof gc
```

72 bytes is exactly a 30-character compact string: a 24-byte `String` object and a 48-byte
`byte[]`. The UUIDv7 rows are a 36-character string (80 bytes) plus about 2 bytes per identifier
amortised from refilling the entropy block, described below.

What gets it there:

- **One copy into the final string.** The prefix and the value are assembled as ASCII bytes in a
  small, reused scratch buffer and copied once into a Latin-1 compact `String`. There is no
  `StringBuilder`, no concatenation, no `char[]` and no intermediate string, and a test fails if
  that ever changes.
- **Buffered entropy.** Asking `SecureRandom` for ten bytes per identifier would dominate everything
  else put together. Instead each stripe (below) keeps a 1 KiB block of cryptographically strong
  bytes and hands out slices of it, refilling about every hundred identifiers. Refills use the JDK's
  `DRBG` (NIST SP 800-90A Hash_DRBG, seeded from the operating system) rather than the platform
  default `NativePRNG`: on JDK 21 a 1 KiB refill takes about 1.4 µs and allocates a fixed 200 bytes,
  against 5 µs and about 3 KiB for `NativePRNG`. Both are cryptographically strong.
- **Monotonic ULIDs are mostly arithmetic.** Within a millisecond, the next ULID is the previous
  one's random field plus one, so it needs no new entropy at all. This is why ULID is faster than
  UUIDv7 here: UUIDv7 needs fresh randomness for every value.
- **A plain lookup.** Registered prefixes are stored with their separator already appended, as
  bytes, in a `HashMap` keyed by `Class` (whose hash is the identity hash), so resolving
  `generate(User.class)` to `"usr_"` is a couple of nanoseconds.

---

## Thread safety, monotonicity and virtual threads

A built `IdoraGenerator` is immutable; share one instance across every platform and virtual thread
in the process. Generation takes no process-wide lock.

All mutable state (the ULID counter, the entropy block and the scratch buffer) lives in a fixed
pool of **stripes** shared by every generator in the process. There are twice as many stripes as
available processors, rounded up to a power of two and capped at 256; each is about 1.3 KiB, and
creates and seeds its `SecureRandom` only when first used. A thread is mapped to a stripe by its
thread id, and holds that stripe's lock for a few dozen nanoseconds while it builds one identifier.
The lock is a compare-and-set on a field inside the padded stripe, not `synchronized`, so it never
pins a virtual thread to its carrier on Java 21.

This is deliberately not a `ThreadLocal`. A virtual thread is cheap to create and often lives for
one request, and with a `ThreadLocal` each one would allocate a fresh kilobyte of entropy and seed a
fresh `SecureRandom` to produce perhaps a single identifier. With stripes, ten thousand short-lived
virtual threads touch no more state than a handful of platform threads would, and memory stays
bounded however many threads there are (the test suite checks this).

The exact ordering guarantees:

- **One thread, ULID: strictly increasing.** A thread's id never changes, so it always maps to the
  same stripe, and every ULID it generates sorts strictly after the one before, even within one
  millisecond, even if the clock steps backwards, and across every generator instance in the
  process. This holds for virtual threads too, whichever carrier they run on.
- **Threads that share a stripe** share its stream, which is still strictly increasing in the order
  the stripe's lock was taken. This is a side effect, not a promise.
- **Different threads, ULID**, and **all UUIDv7 values**: ordered by millisecond only. Values from
  the same millisecond on different stripes, or any two UUIDv7s in the same millisecond, are in no
  particular order. Guaranteeing more would need a process-wide lock.

Timestamps come from `System.currentTimeMillis()`, milliseconds since the Unix epoch, which is UTC
by definition. No time zone is read anywhere, and the test suite runs under
`-Duser.timezone=Australia/Sydney` to prove it.

---

## GraalVM native-image

Explicit registration (`registerEntity`) and `registerEntities(...)` work in a native image with no
configuration: `registerEntity` uses no reflection at all, and `registerEntities` only reads the
annotation from classes you name in code.

**`registerEntitiesFromPackageOf` is the exception.** It lists class files at run time and loads the
classes it finds by name, which a native image does not have: there are no class files to read, and
classes reachable only through `Class.forName` are not included unless configured. This is the Java
counterpart of Idora.NET's trimming caveat. In practice:

- If you do not build native images (most applications), this changes nothing.
- If you do, register with `registerEntity` or `registerEntities(User.class, Order.class, ...)`,
  and do not call `registerEntitiesFromPackageOf` in code that is compiled to a native image. A
  location the scan cannot list is reported at start-up with an `IllegalArgumentException`, but
  that behaviour is not tested under native-image, so do not rely on it there.

---

## Questions you might reasonably have

**How strong is the ordering guarantee?**
Identifiers are ordered by the millisecond they were created in, across threads and processes.
Within one millisecond, ULIDs generated on one thread are strictly increasing; see
[Thread safety, monotonicity and virtual threads](#thread-safety-monotonicity-and-virtual-threads)
for the exact scope.

**Do identifiers stay in order across servers in different time zones?**
Yes. Both formats define their timestamp as milliseconds since the Unix epoch, an absolute instant
with no zone attached, and Idora reads it from `System.currentTimeMillis()`. A server in Sydney and
one in Los Angeles that agree on the instant produce identifiers that interleave in true creation
order. What *does* affect cross-server ordering is **clock skew**: ordering between two machines is
only as accurate as the gap between their clocks, which with NTP is typically single-digit
milliseconds. If you need a total order across machines that does not depend on their clocks, you
need a sequencer, not an identifier format.

**What if the clock goes backwards?**
An NTP correction or a resumed VM snapshot will not produce an out-of-order ULID. The generator
keeps the previous millisecond rather than accepting the earlier reading, so the sequence never
regresses; identifiers made during that window carry a timestamp slightly ahead of the wall clock,
which is the lesser evil.

**Could two identifiers collide?**
Not in any practical sense. Within a stripe, collision is impossible by construction: the counter
increments. Across stripes, two identifiers collide only if 80 bits (ULID) or 74 bits (UUIDv7) of
cryptographically strong randomness coincide inside the same millisecond.

**Are they unguessable? Can I use one as a security token?**
The random portion is cryptographically strong, but the timestamp is plainly readable, consecutive
ULIDs from one thread within a millisecond differ by one, and identifiers are meant to appear in logs, URLs and support
tickets. **Treat them as public.** Authorise every request properly; never use an identifier as a
bearer token or a password-reset secret.

**How do I split an identifier back apart?**
Split on the first underscore. The prefix cannot contain one, so this is unambiguous:

```java
int separator = id.indexOf('_');
String prefix = id.substring(0, separator);    // "usr"
String value  = id.substring(separator + 1);   // "01k2f9v8ta7m3x0qbrn4dzeh5c"
```

**Can I use these as JPA / Hibernate primary keys?**
Yes; they are ordinary strings. Assign one in the constructor or a `@PrePersist` callback rather
than asking the database to generate the key, and map the column as a fixed-length string. Because
the values are time-ordered, B-tree inserts stay at the right-hand edge of the index, which is the
reason to prefer them over `UUID.randomUUID()`.

**Are identifiers case-sensitive?**
Compare them with `String.equals`. Generated values are always lowercase, and neither alphabet has
two characters that differ only by case. Your prefix is emitted exactly as you registered it, so
registering `USR` gives you `USR_01k2f9...`.

**Why is the maximum prefix 16 characters?**
Long enough for a readable abbreviation, short enough that the prefix never dominates the
identifier. If you want more, the prefix is probably trying to be a description.

**Which registration style should I use?**
Explicit if you have a handful of entities or build native images. Annotations once the
registration list is long enough that keeping it in step with the domain is its own chore. There is
no run-time difference: both end up in the same lookup before the first identifier is generated.

**Can I override an annotated prefix with a `registerEntity` call?**
No, that throws. It would give a type whose prefix depends on the order two lines ran in, and the
usual reason to want it, a copy-paste slip, is exactly what the exception is there to catch. Change
the annotation instead.

**Does the scan work in a Spring Boot fat jar, or under my custom class loader?**
It reads the location the marker class was actually defined from: a directory, a jar (including
classes under a `BOOT-INF/classes/` style prefix inside one, when the loader exposes them as `jar:`
URLs) or a named module. The prefixed layout is covered by the tests; Spring Boot's own loader is
not, so check it once in your application. If a class loader hands out
locations that cannot be listed, you get an `IllegalArgumentException` at start-up naming the
location, and `registerEntities(...)` is the drop-in alternative.

**Can several generators coexist?**
Yes; each has its own registry and value format. They share the stripes, so ULIDs from one thread
stay strictly increasing even when they alternate between generators.

---

## Contributing

From `Idora.Java/`, with JDK 17 or later:

```bash
mvn -B verify                                       # compile (-Xlint:all -Werror), test, javadoc, package
mvn -B test                                         # the test suite only
mvn -B package -DskipTests                          # jars only
mvn -B -pl benchmarks -am package -DskipTests && \
  java -jar benchmarks/target/benchmarks.jar        # benchmarks (add -prof gc for allocation)
```

The build fails on any compiler warning and any Javadoc warning. Tests are JUnit 5, written
Arrange-Act-Assert, and run on the module path patched into the library's module with a non-UTC
default time zone. They cover every public member's success, failure and edge cases; the encoders,
the monotonic counter, the striped entropy pool and the package scanner directly (named module,
exploded directory and jar, the last two through a fresh class loader); uniqueness and ordering
across platform and virtual threads; the allocation claim; and every vector in
[`spec/test-vectors.json`](../spec/test-vectors.json).

Releases are built with the `release` profile, which signs the artifacts and publishes `idora` (and
only `idora`, with a flattened, parent-free POM) to Maven Central through the Central Portal:

```bash
mvn -B -P release deploy
```

It needs a `central` server in `settings.xml` holding a Central Portal user token, and
`MAVEN_GPG_KEY` (an ASCII-armoured private key) and `MAVEN_GPG_PASSPHRASE` in the environment.

## License

MIT; see [LICENSE](../LICENSE).
