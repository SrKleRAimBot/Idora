package idorapgx_test

import (
	"encoding/hex"
	"errors"
	"strings"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idorapgx"
	"github.com/jackc/pgx/v5/pgtype"
)

type (
	Invoice struct{}
	Comment struct{} // never registered
)

const invoiceID = "inv_0198846702007011a233445566778899"

var formats = map[string]int16{"binary": pgtype.BinaryFormatCode, "text": pgtype.TextFormatCode}

// The tests drive pgx's type map directly: it is what encodes parameters and decodes results on a
// connection, so no server is needed to check what goes over the wire.
func encode(t *testing.T, format int16, v any) []byte {
	t.Helper()
	buf, err := pgtype.NewMap().Encode(pgtype.UUIDOID, format, v, nil)
	if err != nil {
		t.Fatalf("Encode: %v", err)
	}
	return buf
}

func scan(format int16, src []byte, dst any) error {
	return pgtype.NewMap().Scan(pgtype.UUIDOID, format, src, dst)
}

func TestRoundTrip(t *testing.T) {
	gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[Invoice]("inv"))
	ids := []idora.ID{idora.MustParse(invoiceID)}
	for range 100 {
		ids = append(ids, idora.MustGenerateID[Invoice](gen))
	}
	for name, format := range formats {
		for _, id := range ids {
			buf := encode(t, format, idorapgx.UUID(id))
			var back, typed idora.ID
			if err := scan(format, buf, idorapgx.ScanUUID(&back, "inv")); err != nil || back != id {
				t.Fatalf("%s: %s came back as %q, %v", name, id, back, err)
			}
			if err := scan(format, buf, idorapgx.ScanUUIDFor[Invoice](gen, &typed)); err != nil || typed != id {
				t.Fatalf("%s: ScanUUIDFor: %q, %v", name, typed, err)
			}
		}
	}
}

func TestWireFormat(t *testing.T) {
	id := idora.MustParse(invoiceID)
	if got := hex.EncodeToString(encode(t, pgtype.BinaryFormatCode, idorapgx.UUID(id))); got != id.ValuePart() {
		t.Errorf("binary = %s, want the 16 bytes of %s", got, id.ValuePart())
	}
	if got := string(encode(t, pgtype.TextFormatCode, idorapgx.UUID(id))); got != "01988467-0200-7011-a233-445566778899" {
		t.Errorf("text = %s", got)
	}
}

func TestNull(t *testing.T) {
	if buf := encode(t, pgtype.BinaryFormatCode, idorapgx.UUID(idora.ID{})); buf != nil {
		t.Errorf("the zero ID encoded as %x, want NULL", buf)
	}
	id := idora.MustParse(invoiceID)
	for name, format := range formats {
		if err := scan(format, nil, idorapgx.ScanUUID(&id, "inv")); err != nil || !id.IsZero() {
			t.Errorf("%s: NULL scanned as %q, %v", name, id, err)
		}
	}
}

func TestULIDsDoNotFit(t *testing.T) {
	_, err := pgtype.NewMap().Encode(pgtype.UUIDOID, pgtype.BinaryFormatCode,
		idorapgx.UUID(idora.MustParse("usr_01k226e0g0008j4ct4ank7f24s")), nil)
	if err == nil || !strings.Contains(err.Error(), "is a ULID, which a uuid column cannot hold") {
		t.Errorf("got %v", err)
	}
}

func TestScanIsStrict(t *testing.T) {
	v4, _ := hex.DecodeString("f47ac10b58cc4372a5670e02b2c3d479")
	keep := idora.MustParse(invoiceID)
	for name, format := range formats {
		src := v4
		if format == pgtype.TextFormatCode {
			src = []byte("f47ac10b-58cc-4372-a567-0e02b2c3d479")
		}
		dst := keep
		err := scan(format, src, idorapgx.ScanUUID(&dst, "inv"))
		var invalid *idora.InvalidIDError
		if !errors.As(err, &invalid) || invalid.Reason != idora.ReasonValue || dst != keep {
			t.Errorf("%s: a version 4 UUID: %v, dst %q", name, err, dst)
		}
	}
	good := encode(t, pgtype.BinaryFormatCode, idorapgx.UUID(keep))
	dst := keep
	if err := scan(pgtype.BinaryFormatCode, good, idorapgx.ScanUUID(&dst, "in_v")); !errors.Is(err, idora.ErrInvalidPrefix) || dst != keep {
		t.Errorf("an invalid prefix: %v", err)
	}
	gen := idora.MustNew(idora.WithUUIDv7())
	if err := scan(pgtype.BinaryFormatCode, good, idorapgx.ScanUUIDFor[Comment](gen, &dst)); !errors.Is(err, idora.ErrPrefixNotRegistered) {
		t.Errorf("an unregistered type: %v", err)
	}
}

// Range bounds are valid UUIDv7 identifiers, so they can be the ends of a uuid range query.
func TestBoundsEncode(t *testing.T) {
	from := idora.MustParse(invoiceID).Time()
	for _, bound := range []func(string, time.Time, idora.Strategy) (idora.ID, error){idora.LowerBound, idora.UpperBound} {
		b, err := bound("inv", from, idora.UUIDv7)
		if err != nil {
			t.Fatal(err)
		}
		var back idora.ID
		buf := encode(t, pgtype.BinaryFormatCode, idorapgx.UUID(b))
		if err := scan(pgtype.BinaryFormatCode, buf, idorapgx.ScanUUID(&back, "inv")); err != nil || back != b {
			t.Errorf("%s came back as %q, %v", b, back, err)
		}
	}
}
