# Idora.NET.EntityFrameworkCore

Entity Framework Core support for [Idora.NET](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET)
identifiers: keys that fill themselves in on insert, `IdoraId` columns, and creation-time queries
that run off the primary key's index.

```csharp
modelBuilder.Entity<User>().HasIdoraKey(user => user.Id);

db.Users.Add(new User { Name = "Ada" });   // Id is now "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
```

> Part of [**Idora**](https://github.com/SrKleRAimBot/Idora): the same identifiers, with the same
> rules, for .NET, JavaScript/TypeScript, Java, Go and Rust.

---

## Install

```bash
dotnet add package Idora.NET.EntityFrameworkCore
```

Targets `net8.0`, `net9.0`, `net10.0` and `net11.0`, and depends on `Idora.NET` and
`Microsoft.EntityFrameworkCore`:

| Your application | EF Core the package needs | Works with |
| --- | --- | --- |
| .NET 8, 9 or 10 | **8.0** or later | EF Core 8, 9 and 10 |
| .NET 11 | **11.0** or later | EF Core 11 |

An application on .NET 8 to 10 keeps its own EF Core version. On .NET 11 the package needs EF Core
11, because EF Core 11 changed a constructor that code built for earlier releases calls, so a .NET 11
application still on EF Core 10 is moved up to 11. The tests run against EF Core 10 on .NET 10 and
EF Core 11 on .NET 11. It is provider
neutral: SQL Server, PostgreSQL, SQLite and the rest all work, because everything it configures is
part of EF Core itself.

---

## Quick start

Register Idora and the context in the same container, as usual:

```csharp
builder.Services.AddIdora(options => options.RegisterEntitiesFromAssemblyContaining<User>());
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
```

Then configure the key:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIdoraKey(user => user.Id);
    }
}
```

`HasIdoraKey` does four things in one call:

- makes the property the primary key;
- gives it a value generator, so an entity added with a `null` key gets a new identifier carrying the
  prefix registered for its type — `usr_…` for a `User`;
- marks it non-Unicode, because every character of an identifier is ASCII (`varchar`, not
  `nvarchar`);
- gives it a maximum length: 43 for ULIDs and 49 for UUIDv7s, room for any prefix.

The key is assigned the moment the entity is **added**, not when it is saved, and it is permanent
rather than a temporary placeholder. You can put it in a foreign key, a log line or a response
before `SaveChanges` runs, and a key you assign yourself is left alone.

---

## Exact column sizes

Every identifier for one entity is exactly the same length — 30 characters for `usr` with ULIDs — so
the column can be sized to fit. Inject the registry into the context and pass it in:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IEntityPrefixRegistry prefixes)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIdoraKey(user => user.Id, prefixes);   // varchar(30)
    }
}
```

A type that is not registered now fails while the model is built, rather than on the first insert.
If your application generates UUIDv7s, say so: `HasIdoraKey(user => user.Id, prefixes,
IdGenerationStrategy.UuidV7)`.

## Every registered entity at once

Rather than one `HasIdoraKey` per entity, configure them all at the end of `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.ApplyIdoraKeys(prefixes);
}
```

That configures each entity type that is the root of its hierarchy, has a registered prefix, and has
a single `string` or `IdoraId` primary key — found by convention for a property called `Id`, or by
your own `HasKey`. Everything else is left exactly as it was: types with an `int` or `Guid` key,
composite keys, keyless types, and types with no registered prefix.

---

## `IdoraId` properties

To hold identifiers as [`IdoraId`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET#reading-identifiers-back)
rather than `string`, map the type once for the whole model:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.UseIdoraIds();
}
```

Every `IdoraId` and `IdoraId?` property — keys, foreign keys, plain columns — is then stored as its
text in a non-Unicode string column, with a value comparer for change tracking. `HasIdoraKey`
accepts an `IdoraId` property just as it does a `string`:

```csharp
public sealed class Invoice
{
    public IdoraId Id { get; set; }
    public IdoraId? CorrectsId { get; set; }
}

modelBuilder.Entity<Invoice>().HasIdoraKey(invoice => invoice.Id);
```

Equality and comparisons translate in queries: `db.Invoices.Where(i => i.Id == id)` and
`i.Id >= lower` become plain SQL on the column.

Rows are read **strictly**. A stored value that is not a valid identifier fails the query with an
`InvalidIdException` rather than materialising something half-formed. The one exception is the
empty string, which reads as `default(IdoraId)` — the value an unassigned non-nullable `IdoraId`
property is written as. Declare the property `IdoraId?` to store a real `NULL` instead.

## A generated column that is not the key

`HasIdoraValueGenerator` sets the generator and nothing else, for an alternate public identifier and
the like:

```csharp
modelBuilder.Entity<Invoice>()
    .Property(invoice => invoice.PublicReference)
    .HasIdoraValueGenerator()
    .HasMaxLength(30)
    .IsUnicode(false);
```

---

## Querying by creation time

An identifier's value begins with its timestamp, so the identifiers minted for one prefix during a
span of time are a contiguous range of strings. `WhereCreatedBetween` turns that into two
comparisons on the key column:

```csharp
var lastWeek = await db.Orders
    .WhereCreatedBetween(order => order.Id, "ord", DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow)
    .ToListAsync();
```

```sql
SELECT ... FROM "Orders" AS "o"
WHERE "o"."Id" >= @lower AND "o"."Id" <= @upper
```

No `CreatedAt` column, no secondary index: the database answers it from the primary key. Both ends
are inclusive at millisecond precision, the bounds go as parameters so the plan is reused, and it
composes with the rest of the query. It works on `string` and `IdoraId` columns alike, and in memory
too.

`WhereIdBetween(order => order.Id, lower, upper)` is the building block underneath, for a range you
have computed yourself — resuming a cursor from the last identifier a client saw, for instance. The
bounds come from [`IdoraId.LowerBound` and `IdoraId.UpperBound`](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET#querying-by-creation-time).

The range is meaningful for identifiers of one prefix and one format, which is what a key column
holds. The database compares them with the column's collation, and the range needs one that orders
the identifier's characters exactly as a byte comparison does. A binary collation guarantees it.
A linguistic collation may not: Czech and Slovak treat `ch` as one letter, Danish `aa`, Lithuanian
sorts `y` before `j`, Estonian `z` before `t`, and a **numeric** collation, such as a PostgreSQL ICU
collation with `kn-true`, compares runs of digits as numbers. Any of them can put a bound on the
wrong side of a row, and the range silently misses it. Give the key column a binary collation, for
example on PostgreSQL:

```csharp
builder.Entity<Order>().Property(order => order.Id).UseCollation("C");
```

The same methods work on any `IQueryable`, LINQ to Objects included, and there the comparison is
ordinal whatever the current culture. EF Core's own InMemory provider is the exception: it is an EF
Core provider, so it gets the `string.Compare` that relational databases translate, and evaluates
that with the current culture. Test range queries against SQLite instead, as EF Core's own guidance
recommends.

---

## Inheritance

In a hierarchy the key is declared once, on the root, and shared. Register the root, and every type
in the hierarchy gets its prefix:

```csharp
options.RegisterEntity<Animal>("ani");   // a Dog is "ani_…"
options.RegisterEntity<Cat>("cat");      // unless registered in its own right: a Cat is "cat_…"
```

A derived type registered with a prefix longer than the root's shares the root's column, so size it
with the overload that takes no registry.

---

## When it cannot generate

| Failure | Cause | Fix |
| --- | --- | --- |
| `InvalidOperationException`: *could not find an IIdoraGenerator* | The context cannot see the application's services | Register it with `AddDbContext` in the container `AddIdora` was called on, or give a hand-built context `optionsBuilder.UseApplicationServiceProvider(services)` |
| `EntityPrefixNotRegisteredException` | Neither the entity's type nor a base type in the model has a registered prefix | Register it; the generator never invents a prefix |

EF Core builds a context type's model once and caches it. The registry is a singleton in an
application, so this only matters in tests: every container that hosts the same context type should
register the same prefixes, or the first one's column sizes win.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).
