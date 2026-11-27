# idorapgx

Native PostgreSQL `uuid` columns for [Idora.Go](../README.md) UUIDv7 identifiers, with
[pgx v5](https://github.com/jackc/pgx).

An `idora.ID` already works with pgx in a text column: it's a `driver.Valuer` and an
`sql.Scanner`, which pgx uses. A `uuid` column is smaller (16 bytes instead of 36 characters for a
three-character prefix) and other tools read it as a UUID, but it can't hold the prefix. It doesn't
need to: the prefix is a property of the entity type, the same on every row. This package strips it
on the way in and puts it back on the way out.

```go
gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[Invoice]("inv"))
id := idora.MustGenerateID[Invoice](gen)

_, err := conn.Exec(ctx, `INSERT INTO invoices (id, total) VALUES ($1, $2)`, idorapgx.UUID(id), 42)

var back idora.ID
err = conn.QueryRow(ctx, `SELECT id FROM invoices LIMIT 1`).Scan(idorapgx.ScanUUIDFor[Invoice](gen, &back))
// back.String() == id.String(): "inv_0198846702007011a233445566778899"
```

```bash
go get github.com/SrKleRAimBot/Idora/Idora.Go/idorapgx@latest
```

This is its own module, so the core module keeps its promise of no dependencies. It requires Go
1.23 and pgx v5.7 or later.

| Function | Does |
| --- | --- |
| `idorapgx.UUID(id)` | a query argument writing `id`'s value to a `uuid` parameter; the zero ID writes `NULL` |
| `idorapgx.ScanUUID(&id, "inv")` | a scan destination reading a `uuid` back as an `inv_` identifier; `NULL` reads as the zero ID |
| `idorapgx.ScanUUIDFor[Invoice](gen, &id)` | the same, with `Invoice`'s registered prefix |

Both work in pgx's binary and text formats.

**Only UUIDv7 identifiers fit.** A ULID isn't a UUID, so `UUID` returns an error for one rather
than writing something no other tool could read. Generate the entity's identifiers with
`idora.WithUUIDv7()`, or keep ULIDs in a text column.

**Scanning is as strict as `idora.Parse`.** A `uuid` column that also holds version 4 UUIDs
written by something else fails with an `*idora.InvalidIDError` (`ReasonValue`) instead of
becoming an identifier Idora never minted. An invalid prefix fails with an
`*idora.InvalidPrefixError`, and an unregistered type with an `*idora.PrefixNotRegisteredError`.
On any error the destination is left unchanged.

The range bounds of the core work here too: `idora.LowerBoundValue(from, idora.UUIDv7)` is a valid
UUIDv7 value, so `idorapgx.UUID` of `idora.LowerBound("inv", from, idora.UUIDv7)` is the low end
of a `uuid` range query.

## Testing

```bash
go vet ./...
go test -race ./...
```

The tests drive `pgtype.Map`, which is what encodes parameters and decodes results on a pgx
connection, so they check what goes over the wire without a PostgreSQL server.

## Releasing

Tagged `Idora.Go/idorapgx/vX.Y.Z`. `go.mod` requires a released version of the core module and
replaces it with `../` for development; consumers ignore the replace directive. Release the core
first. See [Releasing](../README.md#releasing) in the core README.

## License

MIT. See [LICENSE](LICENSE).
