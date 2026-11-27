# Idora.Java Jackson

[Jackson](https://github.com/FasterXML/jackson-databind) 2 support for
[Idora.Java](../README.md) identifiers: `IdoraId` values written as JSON strings and read back
strictly, with an optional per-property prefix check.

```java
record Transfer(@IdoraIdPrefix("acc") IdoraId from, @IdoraIdPrefix("acc") IdoraId to, long cents) {}

mapper.readValue("""
    {"from": "acc_01k2f9v8ta7m3x0qbrn4dzeh5c", "to": "usr_01k2f9v8tb9p5r2xnc7fkw3m0d", "cents": 500}
    """, Transfer.class);
// InvalidFormatException: Cannot deserialize value of type `IdoraId` from String "usr_01k2f9v8tb...":
//   The identifier 'usr_01k2f9v8tb...' has the prefix 'usr', but 'acc' was expected. ...
//   (through reference chain: Transfer["to"])
```

---

## Install

```xml
<dependency>
  <groupId>io.github.srkleraimbot</groupId>
  <artifactId>idora-jackson</artifactId>
  <version>1.1.0</version>
</dependency>
```

```kotlin
implementation("io.github.srkleraimbot:idora-jackson:1.1.0")
```

It depends on `idora` and `jackson-databind`; it is built and tested against Jackson 2.21 and uses
only long-standing databind API. Java 17 or later. On the module path its automatic module name is
`io.github.srkleraimbot.idora.jackson`. For Jackson 3 (`tools.jackson`), which Spring Boot 4 uses,
this module does not apply.

---

## Register the module

```java
ObjectMapper mapper = JsonMapper.builder()
    .addModule(new IdoraModule())
    .build();
```

It is also registered for `ServiceLoader`, so `mapper.findAndRegisterModules()` picks it up, and
[`idora-spring-boot`](../idora-spring-boot) registers it with Spring Boot's `ObjectMapper` for you.
Registering it twice is harmless.

---

## What it does

| | Behaviour |
| --- | --- |
| Writing an `IdoraId` | A JSON string holding exactly its text: `"usr_01k2f9v8ta7m3x0qbrn4dzeh5c"` |
| Reading a JSON string | `IdoraId.parse`: strict, so no trimming, no case folding, no hyphens |
| Reading JSON `null` | `null` |
| Reading a number, boolean, object or array | `MismatchedInputException` |
| Reading a malformed identifier, including `""` | `InvalidFormatException`, with the `InvalidIdException` as its cause |
| `Map<IdoraId, V>` keys | Written as the text, read back strictly |

Failures are ordinary Jackson exceptions, so their path names the offending property
(`Transfer["to"]`, `Order["lines"]->ArrayList[3]`) and frameworks that map
`JsonMappingException` to `400 Bad Request` keep doing so. The cause is the core
`InvalidIdException`, whose `getReason()` says which rule was broken: `NO_SEPARATOR`, `PREFIX`,
`VALUE` or `PREFIX_MISMATCH`.

---

## Requiring a prefix: `@IdoraIdPrefix`

```java
public record Payment(
        @IdoraIdPrefix("usr") IdoraId payer,
        @IdoraIdPrefix("ord") List<IdoraId> orders,
        @IdoraIdPrefix("acc") Map<IdoraId, Long> balances) {}
```

The annotation makes the property accept only identifiers with exactly that prefix, case included.
It applies to the property's value and, for a collection, array or map property, to its elements,
values and keys. It may sit on a field, a setter, a creator parameter or a record
component. It affects reading only; writing never checks.

The prefix is validated once, when Jackson builds the deserializer for the property. A prefix no
identifier could carry (`@IdoraIdPrefix("us_r")`) fails with an `InvalidDefinitionException`
naming the property and the class, the first time the type is read, because it is a bug in the
code rather than bad input.

To check an identifier against an *entity type* rather than a prefix written out, take the
property as an `IdoraId` and call `ids.parseId(User.class, ...)` or `id.hasPrefix(...)` on it in
your own code; the module has no access to your generator's registry.

---

## Contributing

From `Idora.Java/`:

```bash
mvn -B -pl idora-jackson -am verify
```

The tests run a real `ObjectMapper` over records, beans, explicit creators, lists, maps (keys and
values) and root values, for writing, reading, every failure mode and `@IdoraIdPrefix` in each
position, plus service-loader discovery.

## License

MIT; see [LICENSE](../../LICENSE).
