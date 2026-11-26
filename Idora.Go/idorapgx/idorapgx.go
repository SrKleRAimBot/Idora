// Package idorapgx stores Idora UUIDv7 identifiers in native PostgreSQL uuid columns with pgx v5.
//
// An [idora.ID] already works with pgx in a text column, such as char(36) for a three-character
// prefix: it implements [database/sql/driver.Valuer] and [database/sql.Scanner], which pgx uses.
// A uuid column is half the size, 16 bytes, and other tools read it as a UUID, but it cannot
// hold the prefix. The prefix is a property of the entity type, so it is the same for every row
// and the column does not need it: this package strips it on the way in and restores it on the
// way out.
//
//	gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[Invoice]("inv"))
//	id := idora.MustGenerateID[Invoice](gen)
//
//	_, err := conn.Exec(ctx, `INSERT INTO invoices (id) VALUES ($1)`, idorapgx.UUID(id))
//
//	var back idora.ID
//	err = conn.QueryRow(ctx, `SELECT id FROM invoices`).Scan(idorapgx.ScanUUIDFor[Invoice](gen, &back))
//	// back is "inv_0198846702007011a233445566778899" again
//
// Only UUIDv7 identifiers fit: a ULID is not a UUID. Scanning is as strict as [idora.Parse], so
// a uuid column holding a version 4 UUID written by something else is reported rather than
// turned into an identifier that was never minted.
//
// This is a separate module from Idora.Go so that the core stays free of dependencies.
package idorapgx

import (
	"encoding/hex"
	"fmt"
	"reflect"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/jackc/pgx/v5/pgtype"
)

// UUID returns a query argument that writes id's value, without its prefix, to a PostgreSQL
// uuid parameter. The zero ID is written as NULL. Encoding fails if id is a ULID, since a ULID
// is not a UUID.
func UUID(id idora.ID) pgtype.UUIDValuer { return uuidArg{id} }

type uuidArg struct{ id idora.ID }

// UUIDValue implements [pgtype.UUIDValuer].
func (a uuidArg) UUIDValue() (pgtype.UUID, error) {
	if a.id.IsZero() {
		return pgtype.UUID{}, nil
	}
	if a.id.Strategy() != idora.UUIDv7 {
		return pgtype.UUID{}, fmt.Errorf("idorapgx: %s is a ULID, which a uuid column cannot hold; "+
			"generate UUIDv7 identifiers with idora.WithUUIDv7(), or store ULIDs in a text column",
			a.id)
	}
	var u pgtype.UUID
	if _, err := hex.Decode(u.Bytes[:], []byte(a.id.ValuePart())); err != nil {
		return pgtype.UUID{}, err // unreachable: a parsed UUIDv7 value is 32 hexadecimal digits
	}
	u.Valid = true
	return u, nil
}

// ScanUUID returns a scan destination that reads a PostgreSQL uuid into dst as an identifier
// with the given prefix. NULL scans as the zero ID. A UUID that is not a version 7 UUID with the
// RFC 9562 variant returns an [*idora.InvalidIDError], and an invalid prefix an
// [*idora.InvalidPrefixError]; either way dst is left unchanged.
func ScanUUID(dst *idora.ID, prefix string) pgtype.UUIDScanner {
	return uuidDest{dst: dst, prefix: prefix, err: idora.ValidatePrefix(prefix)}
}

// ScanUUIDFor is [ScanUUID] for entity type T, using the prefix T was registered with in g. If
// T has no registered prefix, scanning returns an [*idora.PrefixNotRegisteredError].
func ScanUUIDFor[T any](g *idora.Generator, dst *idora.ID) pgtype.UUIDScanner {
	prefix, err := g.Prefix(reflect.TypeFor[T]())
	return uuidDest{dst: dst, prefix: prefix, err: err}
}

type uuidDest struct {
	dst    *idora.ID
	prefix string
	err    error
}

// ScanUUID implements [pgtype.UUIDScanner].
func (d uuidDest) ScanUUID(v pgtype.UUID) error {
	if d.err != nil {
		return d.err
	}
	if !v.Valid {
		*d.dst = idora.ID{}
		return nil
	}
	buf := make([]byte, 0, idora.MaxPrefixLen+1+32)
	buf = append(buf, d.prefix...)
	buf = append(buf, idora.Separator)
	buf = hex.AppendEncode(buf, v.Bytes[:])
	id, err := idora.ParsePrefixed(string(buf), d.prefix)
	if err != nil {
		return err
	}
	*d.dst = id
	return nil
}
