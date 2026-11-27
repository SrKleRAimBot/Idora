# Idora.Java Spring Boot

[Spring Boot](https://spring.io/projects/spring-boot) 3 auto-configuration for
[Idora.Java](../README.md). Add the dependency and the application has an `IdoraGenerator` bean
registered with every `@EntityPrefix` type in its own packages:

```java
@Service
class OrderService {
    private final IdoraGenerator ids;

    OrderService(IdoraGenerator ids) {
        this.ids = ids;
    }

    Order create() {
        return new Order(ids.generate(Order.class));   // "ord_01k2f9v8ta7m3x0qbrn4dzeh5c"
    }
}

@RestController
class OrderController {
    @GetMapping("/orders/{id}")
    Order get(@PathVariable IdoraId id) { ... }          // "/orders/nonsense" is a 400
}
```

---

## Install

```xml
<dependency>
  <groupId>io.github.srkleraimbot</groupId>
  <artifactId>idora-spring-boot</artifactId>
  <version>1.1.0</version>
</dependency>
```

```kotlin
implementation("io.github.srkleraimbot:idora-spring-boot:1.1.0")
```

Add [`idora-jackson`](../idora-jackson) and [`idora-hibernate`](../idora-hibernate) alongside it for
JSON and JPA; each is switched on only when it is on the class path. Built and tested against Spring
Boot 3.5 (Spring Framework 6.2); Java 17 or later. Spring Boot 4 is not tested and not expected to
work: it moved the classes this module hooks into and switched to Jackson 3. Automatic module name:
`io.github.srkleraimbot.idora.spring`.

---

## What you get

| Bean | When | Does |
| --- | --- | --- |
| `IdoraGenerator` | always, unless you declare your own | built from the `idora.*` properties and the annotation scan, below |
| `StringToIdoraIdConverter` | always | binds `@PathVariable IdoraId`, `@RequestParam IdoraId`, form fields, in Spring MVC and WebFlux |
| `IdoraModule` | `idora-jackson` and Jackson on the class path | registered with Spring Boot's `ObjectMapper`, so `IdoraId` is a JSON string |
| `HibernatePropertiesCustomizer` | `idora-hibernate` on the class path | hands the generator to Hibernate, so `@IdoraGeneratedId` keys use the same registry and strategy |

Declaring your own `IdoraGenerator` bean replaces the auto-configured one, and everything else uses
yours.

---

## Configuration

```properties
# ulid (the default) or uuid-v7
idora.strategy=uuid-v7

# Where @EntityPrefix types are collected from. Empty (the default) means the application's own
# packages: those of the @SpringBootApplication class, exactly as @EntityScan defaults.
idora.scan.packages=com.example.billing,com.example.accounts
idora.scan.enabled=true

# Types you cannot annotate, by fully qualified class name. The brackets are needed because class
# names contain dots.
idora.prefixes[com.example.vendor.Widget]=wdg
```

The jar ships `spring-configuration-metadata.json`, so your IDE completes and documents these.

The generator is built in this order: the strategy; every `@EntityPrefix` type in the scanned
packages; the `idora.prefixes` entries; then every `IdoraGeneratorBuilderCustomizer` bean, in
`@Order`:

```java
@Bean
IdoraGeneratorBuilderCustomizer vendorTypes() {
    return builder -> builder.registerEntity(Widget.class, "wdg");
}
```

All of them feed the same builder, so the [collision rules](../README.md#mixing-the-two) apply
across them: an `idora.prefixes` entry claiming a prefix a scanned type already has fails start-up
with a `DuplicateEntityPrefixException`, naming both types. So does an invalid prefix or a class name
that cannot be loaded. Nothing is discovered later.

### How the scan works

It uses Spring's own class path scanning, which reads class files without loading the classes and
understands Spring Boot's nested jars, and then loads only the matches, **without initialising**
them. It finds classes, records, interfaces, enums and nested types carrying `@EntityPrefix`
themselves; as in the core, a subclass of an annotated type does not inherit its prefix.

Scanning the application's own packages by default is what `@EntityScan` and Spring Data do. It is
still a source you named, through `@SpringBootApplication`, not a walk over everything on the class
path, so a library cannot register prefixes into your application. Point `idora.scan.packages`
elsewhere, or turn the scan off, if your entities live outside the application's packages.

---

## Web binding

`@PathVariable IdoraId` and `@RequestParam IdoraId` parse strictly. A malformed identifier fails
conversion, which Spring MVC answers with **400 Bad Request**; an empty parameter converts to `null`,
as Spring's own converters do, so a blank optional parameter reads as absent.

The converter accepts any prefix. To require the prefix of a particular entity, check it in the
handler:

```java
@GetMapping("/orders/{id}")
Order get(@PathVariable String id) {
    IdoraId orderId = ids.parseId(Order.class, id);   // InvalidIdException for "usr_..." or garbage
    ...
}
```

and map `InvalidIdException` to the status you want with an `@ExceptionHandler`, typically `400` or
`404`.

---

## Contributing

From `Idora.Java/`:

```bash
mvn -B -pl idora-spring-boot -am verify
```

The tests use `ApplicationContextRunner` for every property, the default and explicit scan, the
customizers, backing off for an application's own generator and every start-up failure; a
`FilteredClassLoader` to prove the Jackson and Hibernate parts stay off without their jars; a real
JPA context over H2 persisting an entity whose key comes from the application's generator; and
MockMvc for path variables, request parameters, JSON bodies and the 400 on a malformed identifier.

## License

MIT; see [LICENSE](../../LICENSE).
