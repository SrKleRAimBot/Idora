package idorapgx_test

import (
	"fmt"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idorapgx"
	"github.com/jackc/pgx/v5/pgtype"
)

// On a connection the calls are conn.Exec(ctx, sql, idorapgx.UUID(id)) and
// row.Scan(idorapgx.ScanUUIDFor[Invoice](gen, &id)); the type map below is what they use.
func Example() {
	gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[Invoice]("inv"))
	id := idora.MustParse("inv_0198846702007011a233445566778899")

	m := pgtype.NewMap()
	wire, _ := m.Encode(pgtype.UUIDOID, pgtype.TextFormatCode, idorapgx.UUID(id), nil)
	fmt.Println(string(wire))

	var back idora.ID
	err := m.Scan(pgtype.UUIDOID, pgtype.TextFormatCode, wire, idorapgx.ScanUUIDFor[Invoice](gen, &back))
	fmt.Println(back, err)
	// Output:
	// 01988467-0200-7011-a233-445566778899
	// inv_0198846702007011a233445566778899 <nil>
}
