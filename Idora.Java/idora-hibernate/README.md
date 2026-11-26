# Idora.Java Hibernate

[Hibernate ORM](https://hibernate.org/orm/) 6 support for [Idora.Java](../README.md) identifiers:
primary keys generated as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c` when an entity is persisted, and
`IdoraId` attributes stored as text with no annotation at all.

```java
@Entity
@EntityPrefix("ord")
public class Order {

    @Id
    @IdoraGeneratedId
    private IdoraId id;            // "ord_01k2f9v8ta7m3x0qbrn4dzeh5c", assigned by persist()

    private IdoraId customerId;    // a VARCHAR column, read back as an IdoraId
}
```

---

## Install

```xml
<dependency>
  <groupId>io.github.srkleraimbot</groupId>
  <artifactId>idora-hibernate</artifactId>
  <version>1.1.0</version>
</dependency>
```

```kotlin
implementation("io.github.srkleraimbot:idora-hibernate:1.1.0")
```

`hibernate-core` is a **provided** dependency: your application chooses the Hibernate release and
this jar never brings one in. It is built and tested against Hibernate ORM 6.6 (the line Spring
Boot 3.5 uses) over H2. Java 17 or later. Automatic module name:
`io.github.srkleraimbot.idora.hibernate`.

---

## Generated primary keys: `@IdoraGeneratedId`

Put it next to `@Id`, instead of `@GeneratedValue`, on a `String` or `IdoraId` field or getter:

```java
@Id
@IdoraGeneratedId
private String id;
```

The identifier is generated in Java, **before** the `INSERT`: the entity has its key as soon as
`persist` returns, Hibernate can batch the inserts, and the database does no work to hand out keys.
Consecutive keys from one thread are strictly increasing, so inserts land at the right-hand edge of
the primary-key index.

### Where the prefix comes from

| Annotation | Prefix |
| --- | --- |
| `@IdoraGeneratedId` | the one registered for the entity, in the configured generator's registry |
| `@IdoraGeneratedId(entity = Order.class)` | the one registered for `Order` |
| `@IdoraGeneratedId(prefix = "ord")` | `ord`, as written; no registry involved |

"The entity" is the **root of its inheritance hierarchy**. Every entity in a hierarchy shares one
identifier column, so a `Dog extends Animal` is persisted with `Animal`'s prefix, and `Dog` need not
be registered. That keeps the column one prefix wide, which is what the
[range bounds](../README.md#range-bounds-querying-by-creation-time) need. An `@Id` declared once on a
`@MappedSuperclass` is resolved separately for each entity that inherits it, so each gets its own
prefix.

### Telling Hibernate which generator to use

Prefixes and the value format come from your application's `IdoraGenerator`, so Hibernate-generated
keys are identical to the ones you generate yourself. Hibernate finds it through the setting
`idora.generator` (`IdoraHibernateSettings.GENERATOR`), whose value may be:

- the `IdoraGenerator` itself, when the configuration is built in code:

  ```java
  new StandardServiceRegistryBuilder().applySetting(IdoraHibernateSettings.GENERATOR, ids)
  Persistence.createEntityManagerFactory("app", Map.of(IdoraHibernateSettings.GENERATOR, ids))
  ```

- an `IdoraGeneratorProvider`, or the name of an `IdoraGeneratorProvider` class, which is what a
  `persistence.xml` or `hibernate.properties` can express:

  ```java
  public final class Ids implements IdoraGeneratorProvider {
      public static final IdoraGenerator IDS =
          IdoraGenerator.builder().registerEntitiesFromPackageOf(Order.class).build();

      @Override
      public IdoraGenerator getIdoraGenerator() {
          return IDS;
      }
  }
  ```

  ```xml
  <property name="idora.generator" value="com.example.Ids"/>
  ```

When the setting is absent, a single `IdoraGeneratorProvider` registered in
`META-INF/services/io.github.srkleraimbot.idora.hibernate.IdoraGeneratorProvider` is used. Several
registered providers are an error rather than a guess.

**With [`idora-spring-boot`](../idora-spring-boot) you configure nothing**: it hands the
application's `IdoraGenerator` bean to Hibernate through a `HibernatePropertiesCustomizer`, unless
`spring.jpa.properties.idora.generator` says otherwise.

With an explicit `prefix` and no generator configured, the defaults are used
(`IdoraGenerator.create()`: ULID values).

### Misconfiguration fails start-up

Everything is resolved while Hibernate builds its metadata, never on the first insert:

| Mistake | Fails with |
| --- | --- |
| No generator configured, and the annotation needs the registry | `MappingException` listing the ways to configure one |
| The entity (or `entity = ...`) is not registered | `EntityPrefixNotRegisteredException` naming the type |
| `prefix = "b_d"` | `InvalidEntityPrefixException` |
| Both `prefix` and `entity` | `MappingException` |
| The attribute is not a `String` or `IdoraId` | `MappingException` naming the type |
| A setting of the wrong kind, an unloadable class name, a provider returning `null`, several service-loader providers | `MappingException` saying what was expected |

---

## `IdoraId` attributes and query parameters

`idora-hibernate` registers a Hibernate `JavaType` for `IdoraId` through a `TypeContributor` that
Hibernate discovers on its own. So every `IdoraId`, whether a key, a plain attribute or a query
parameter, maps to a `VARCHAR` with no `@Convert`, `@Type` or `@Column`:

```java
List<Order> orders = session
    .createSelectionQuery("from Order where customerId = :customer", Order.class)
    .setParameter("customer", customerId)          // an IdoraId
    .getResultList();
```

This matters more than it looks. `IdoraId` is `Serializable`, and Hibernate's fallback for a
serializable class it does not know is a **binary blob of the Java serialization**. With this jar on
the class path that cannot happen.

Reading a column that holds something other than a conforming identifier fails the read with an
`InvalidIdException` naming the text: the strict parse applies to the database too.

A column generated from the mapping is 49 characters wide, the longest an identifier can be. Size
it to your prefix and format with `@Column(length = 30)` or your own migration; see
[Sizing your database column](../README.md#sizing-your-database-column).

### Querying by creation time

The range bounds work as query parameters:

```java
session.createSelectionQuery("from Order o where o.id between :from and :to", Order.class)
    .setParameter("from", ids.lowerBound(Order.class, Instant.parse("2025-08-01T00:00:00Z")))
    .setParameter("to", ids.upperBound(Order.class, Instant.parse("2025-08-31T23:59:59.999Z")))
    .getResultList();
```

This is a primary-key range scan, with no timestamp column and no parsing. It needs the database to
compare the column as plain bytes, which is the case for H2, PostgreSQL's `C` collation and any
binary collation; see the core README.

---

## The portable `AttributeConverter`

`IdoraIdConverter` is a plain JPA `AttributeConverter<IdoraId, String>` for code that must stay
provider-neutral, or that names converters explicitly:

```java
@Convert(converter = IdoraIdConverter.class)
private IdoraId orderId;
```

Under Hibernate you do not need it (the type contributor already covers attributes, keys and query
parameters, which a converter cannot). It is not auto-applied, so that adding this jar never changes
a mapping silently. To auto-apply it, declare a subclass in a package your persistence unit scans:

```java
@Converter(autoApply = true)
public class IdoraIdAutoConverter extends IdoraIdConverter {}
```

---

## Contributing

From `Idora.Java/`:

```bash
mvn -B -pl idora-hibernate -am verify
```

The tests build real session factories over in-memory H2 databases: generated `String` and
`IdoraId` keys, both strategies, explicit prefixes and entities, single-table inheritance, five
hundred inserts in one transaction, every way of configuring the generator (instance, provider,
class, class name, service loader), every start-up failure, the column type and width, query
parameters, range-bound queries, null and corrupt column values, and the converter.

## License

MIT; see [LICENSE](../../LICENSE).
