# idoragorm

[GORM](https://gorm.io) integration for [Idora.Go](../README.md): primary keys assigned on
create, and queries by creation time answered from the primary key index.

```go
gen := idora.MustNew(idora.WithEntities(User{}, Order{}))
db.Use(idoragorm.New(gen))

u := User{Name: "Ada", Orders: []Order{{Total: 42}}}
db.Create(&u) // u.ID == "usr_01k2f9v8ta7m3x0qbrn4dzeh5c", u.Orders[0].ID == "ord_01k2f9v8tb…"

var recent []User
db.Scopes(idoragorm.CreatedBetweenFor[User](gen, "", time.Now().Add(-time.Hour), time.Now())).Find(&recent)
```

This is its own module, so the core module keeps its promise of no dependencies:

```bash
go get github.com/SrKleRAimBot/Idora/Idora.Go/idoragorm@latest
```

It requires Go 1.23 and GORM v1.31 or later.

---

## Assigning primary keys

`idoragorm.New(gen)` returns a `gorm.Plugin`. Once it's installed with `db.Use`, every create
(`Create`, `CreateInBatches`, `Save` of a new row, and the rows GORM creates for associations)
looks at the model's primary key, and fills it if all of these hold:

- the key is a `string`, a type whose underlying type is `string`, or an `idora.ID`;
- it's still empty, so a key you set yourself is never replaced;
- the model type has a prefix: it was registered with the generator (`WithEntity` or
  `WithEntities`), or it implements `idora.Entity` (see below).

Anything else, such as an auto-incrementing integer key or a model with no prefix, is left exactly
as GORM would handle it. Batches are filled in order from the generator's single ULID stream, so a
batch's keys increase in the order of the slice.

The key is filled **before** GORM's `BeforeCreate` hooks run, so a hook can read it, or override
it.

```go
type User struct {
    ID   string // or idora.ID
    Name string
}
```

An `idora.ID` key needs no tags: GORM sees a string column, and scans it back into an `ID`.

### Declared prefixes

A model the generator doesn't know, but which declares `IDPrefix`, gets identifiers too:

```go
type Order struct {
    ID    idora.ID
    Total int
}

func (Order) IDPrefix() string { return "ord" }
```

Its declaration is checked once per type, exactly as listing it in `idora.WithEntities` alongside
the generator's registrations would check it. An invalid prefix, an `IDPrefix` promoted from an
embedded field, or a prefix that collides with a registered one (ignoring case) fails the create
with the matching idora error (`ErrInvalidPrefix`, `ErrPromotedPrefix`, `ErrDuplicatePrefix`),
and no row is written.

Pass `idoragorm.WithoutDeclaredPrefixes()` to limit the plugin to registered types. Listing
every entity in `idora.New` is the stricter style, and the one the
[specification](../../spec/SPECIFICATION.md#42-declarative-registration) prefers: the list is
where you read off every prefix the application issues, and collisions are found at start-up
rather than on the first create.

## Querying by creation time

A value starts with its timestamp, so the rows of one entity type created during a span of time
have a contiguous range of keys. The scopes turn the span into that range:

```go
db.Scopes(idoragorm.CreatedBetweenFor[User](gen, "", from, to)).Find(&users)
db.Scopes(idoragorm.CreatedBetween("users.id", "usr", idora.ULID, from, to)).Find(&users)
```

```sql
SELECT * FROM `users` WHERE `users`.`id` >= "usr_01k226e0g00000000000000000"
                        AND `users`.`id` <= "usr_01k229vw40zzzzzzzzzzzzzzzz"
```

The span is inclusive at millisecond precision. The database answers it from the primary key
index; there's no `created_at` column to keep, index or trust. An empty column name means the
model's primary key. `CreatedBetweenFor` takes the prefix from `T`'s registration and the format
from the generator; `CreatedBetween` takes both explicitly. Use the format the rows were generated
with: the bounds compare correctly only against identifiers of the same prefix and format, which
is what a column holding one entity type contains.

A mistake in the arguments — an invalid prefix, an unregistered type, an undefined format, or a
time before 1970 — is added to the query's errors, so the query fails instead of quietly matching
everything or nothing.

## Testing

```bash
go vet ./...
go test -race ./...
```

The tests run against a real SQLite database through [glebarez/sqlite](https://github.com/glebarez/sqlite),
a pure-Go driver, so they need no cgo and no server.

## Releasing

Tagged `Idora.Go/idoragorm/vX.Y.Z`. `go.mod` requires a released version of the core module and
replaces it with `../` for development; consumers ignore the replace directive. Release the core
first. See [Releasing](../README.md#releasing) in the core README.

## License

MIT. See [LICENSE](LICENSE).
