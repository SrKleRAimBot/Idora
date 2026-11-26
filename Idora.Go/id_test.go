package idora_test

import (
	"database/sql"
	"database/sql/driver"
	"encoding"
	"encoding/json"
	"errors"
	"fmt"
	"reflect"
	"slices"
	"strings"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

const (
	ulidText = "usr_01k226e0g0008j4ct4ank7f24s" // 1754568000000 ms
	uuidText = "inv_0198846702007011a233445566778899"
)

var (
	_ encoding.TextMarshaler   = idora.ID{}
	_ encoding.TextUnmarshaler = (*idora.ID)(nil)
	_ sql.Scanner              = (*idora.ID)(nil)
	_ driver.Valuer            = idora.ID{}
	_ fmt.Stringer             = idora.ID{}
)

func TestParseReadsBothFormats(t *testing.T) {
	cases := []struct {
		text, prefix, value string
		strategy            idora.Strategy
		ms                  int64
	}{
		{ulidText, "usr", "01k226e0g0008j4ct4ank7f24s", idora.ULID, 1754568000000},
		{uuidText, "inv", "0198846702007011a233445566778899", idora.UUIDv7, 1754568000000},
	}
	for _, c := range cases {
		id, err := idora.Parse(c.text)
		if err != nil {
			t.Fatalf("Parse(%q): %v", c.text, err)
		}
		if id.String() != c.text || id.Prefix() != c.prefix || id.ValuePart() != c.value ||
			id.Strategy() != c.strategy || id.UnixMilli() != c.ms || id.IsZero() {
			t.Errorf("Parse(%q) = %q %q %q %v %d", c.text, id, id.Prefix(), id.ValuePart(), id.Strategy(), id.UnixMilli())
		}
		if want := time.Date(2025, 8, 7, 12, 0, 0, 0, time.UTC); !id.Time().Equal(want) || id.Time().Location() != time.UTC {
			t.Errorf("Parse(%q).Time() = %v, want %v", c.text, id.Time(), want)
		}
	}
}

func TestParseReadsWhatTheGeneratorMints(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
		for range 1000 {
			before := time.Now().UnixMilli()
			text := idora.MustGenerate[User](gen)
			after := time.Now().UnixMilli()
			id, err := idora.Parse(text)
			if err != nil {
				t.Fatalf("%v: Parse(%q): %v", s, text, err)
			}
			if id.Strategy() != s || id.Prefix() != "usr" || id.UnixMilli() < before || id.UnixMilli() > after {
				t.Fatalf("%v: Parse(%q) = %v %q %d outside [%d, %d]", s, text, id.Strategy(), id.Prefix(), id.UnixMilli(), before, after)
			}
		}
	}
}

func TestTimeIsUTCWhateverTheLocalZone(t *testing.T) {
	withNonUTCLocalZone(t)
	id := idora.MustParse(ulidText)
	if id.Time().Location() != time.UTC || id.Time().Hour() != 12 {
		t.Errorf("Time() = %v, want 12:00 UTC", id.Time())
	}
}

func TestZeroID(t *testing.T) {
	var zero idora.ID
	if !zero.IsZero() || zero.String() != "" || zero.Prefix() != "" || zero.ValuePart() != "" ||
		zero.UnixMilli() != 0 || !zero.Time().IsZero() || zero.Strategy() != idora.ULID {
		t.Errorf("zero ID = %q %q %q %d %v", zero, zero.Prefix(), zero.ValuePart(), zero.UnixMilli(), zero.Time())
	}
	if zero.HasPrefix("") != true || zero.HasPrefix("usr") {
		t.Error("zero ID prefix test")
	}
	// It sorts before every identifier, including the lowest possible one.
	lowest := idora.MustParse("a_00000000000000000000000000")
	if !zero.Before(lowest) || zero.Compare(zero) != 0 || !zero.Equal(idora.ID{}) {
		t.Error("zero ID does not sort first")
	}
	if v, err := zero.Value(); v != nil || err != nil {
		t.Errorf("zero.Value() = %v, %v; want nil, nil", v, err)
	}
	if b, _ := zero.MarshalText(); len(b) != 0 {
		t.Errorf("zero.MarshalText() = %q", b)
	}
}

func TestParseKeepsTheInputVerbatim(t *testing.T) {
	// Parsing never folds case or trims: the text comes back exactly.
	id := idora.MustParse("USR_01hf7yat0004hmasw9nf6yy093")
	if id.String() != "USR_01hf7yat0004hmasw9nf6yy093" || id.Prefix() != "USR" {
		t.Errorf("got %q", id)
	}
}

func TestEqualityIsTextEquality(t *testing.T) {
	a := idora.MustParse(ulidText)
	b := idora.MustParse(strings.Clone(ulidText))
	c := idora.MustParse("ord_01k226e0g0008j4ct4ank7f24s")
	if a != b || !a.Equal(b) || a.Compare(b) != 0 {
		t.Error("equal texts are not equal IDs")
	}
	if a == c || a.Equal(c) || a.Compare(c) == 0 {
		t.Error("different prefixes compare equal")
	}
	set := map[idora.ID]bool{a: true}
	if !set[b] || set[c] {
		t.Error("IDs do not work as map keys")
	}
}

func TestCompareIsChronologicalAcrossPrefixesAndFormats(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"), idora.WithEntity[Order]("ord"))
	uuid := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[Invoice]("inv"))
	var want []idora.ID
	for range 5 {
		want = append(want, idora.MustGenerateID[User](gen), idora.MustGenerateID[Order](gen))
		time.Sleep(2 * time.Millisecond)
		want = append(want, idora.MustGenerateID[Invoice](uuid))
		time.Sleep(2 * time.Millisecond)
	}
	got := slices.Clone(want)
	slices.Reverse(got)
	slices.SortFunc(got, idora.ID.Compare)
	if !slices.Equal(got, want) {
		t.Errorf("sorted %v, want generation order %v", got, want)
	}
}

func TestCompareTimeLooksAtTheMillisecondOnly(t *testing.T) {
	a := idora.MustParse("usr_01k226e0g0008j4ct4ank7f24s")
	b := idora.MustParse("ord_0198846702007011a233445566778899") // the same millisecond
	c := idora.MustParse("doc_0198846702017fee9dccbbaa99887766") // one later
	if a.CompareTime(b) != 0 || !a.SameMillisecond(b) || a.Equal(b) {
		t.Error("identifiers from the same millisecond")
	}
	if a.CompareTime(c) != -1 || c.CompareTime(a) != 1 || a.SameMillisecond(c) {
		t.Error("identifiers from different milliseconds")
	}
	// Within one millisecond the natural order falls back to the value.
	if !b.Before(a) || !a.After(b) {
		t.Errorf("%s should sort before %s by value", b, a)
	}
}

func TestCreatedBeforeAndAfterTruncateToTheMillisecond(t *testing.T) {
	id := idora.MustParse(ulidText)
	minted := time.UnixMilli(id.UnixMilli())
	cases := []struct {
		at            time.Time
		before, after bool
	}{
		{minted, false, false},
		{minted.Add(999_999 * time.Nanosecond), false, false}, // still the same millisecond
		{minted.Add(time.Millisecond), true, false},
		{minted.Add(-time.Nanosecond), false, true}, // the previous millisecond
		{minted.Add(-time.Millisecond), false, true},
		{minted.In(time.FixedZone("x", 5*3600)), false, false}, // zones are irrelevant
	}
	for _, c := range cases {
		if got := id.CreatedBefore(c.at); got != c.before {
			t.Errorf("CreatedBefore(%v) = %v", c.at, got)
		}
		if got := id.CreatedAfter(c.at); got != c.after {
			t.Errorf("CreatedAfter(%v) = %v", c.at, got)
		}
	}
}

func TestCreatedBeforeAndAfterHoldForTimesFarFromTheEpoch(t *testing.T) {
	// t.UnixMilli() overflows int64 for these, which once made an epoch identifier report being
	// created after a time hundreds of millions of years in the future.
	for _, text := range []string{"ord_00000000000000000000000000", ulidText, "x_7zzzzzzzzzzzzzzzzzzzzzzzzz"} {
		id := idora.MustParse(text)
		future := []time.Time{time.Unix(10_000_000_000_000_000, 0), time.Unix(1<<62, 999_999_999)}
		past := []time.Time{time.Unix(-10_000_000_000_000_000, 0), time.Unix(-1<<62, 0), {}}
		for _, at := range future {
			if !id.CreatedBefore(at) || id.CreatedAfter(at) {
				t.Errorf("%s: CreatedBefore(%v) = %v, CreatedAfter = %v; want true, false", text, at, id.CreatedBefore(at), id.CreatedAfter(at))
			}
		}
		for _, at := range past {
			if id.CreatedBefore(at) || !id.CreatedAfter(at) {
				t.Errorf("%s: CreatedBefore(%v) = %v, CreatedAfter = %v; want false, true", text, at, id.CreatedBefore(at), id.CreatedAfter(at))
			}
		}
	}
}

func TestHasPrefixIsExact(t *testing.T) {
	id := idora.MustParse(ulidText)
	for p, want := range map[string]bool{"usr": true, "USR": false, "us": false, "usr_": false, "": false} {
		if id.HasPrefix(p) != want {
			t.Errorf("HasPrefix(%q) = %v", p, !want)
		}
	}
}

func TestParsePrefixed(t *testing.T) {
	if _, err := idora.ParsePrefixed(ulidText, "usr"); err != nil {
		t.Errorf("matching prefix: %v", err)
	}
	_, err := idora.ParsePrefixed(ulidText, "USR")
	var invalid *idora.InvalidIDError
	if !errors.As(err, &invalid) || invalid.Reason != idora.ReasonPrefixMismatch || invalid.ExpectedPrefix != "USR" {
		t.Errorf("case-different prefix: %v", err)
	}
	// An invalid expected prefix cannot match anything; the identifier's own checks come first.
	if _, err := idora.ParsePrefixed(ulidText, "us_r"); !errors.As(err, &invalid) || invalid.Reason != idora.ReasonPrefixMismatch {
		t.Errorf("invalid expected prefix: %v", err)
	}
	if _, err := idora.ParsePrefixed("usr_x", "ord"); !errors.As(err, &invalid) || invalid.Reason != idora.ReasonValue {
		t.Errorf("bad value is reported before a mismatch: %v", err)
	}
}

func TestMustParsePanicsWithTheError(t *testing.T) {
	defer func() {
		err, _ := recover().(error)
		if !errors.Is(err, idora.ErrInvalidID) {
			t.Errorf("recovered %v, want an ErrInvalidID error", err)
		}
	}()
	idora.MustParse("nope")
}

func TestParseFor(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"), idora.WithEntity[Order]("ord"))

	id, err := idora.ParseFor[User](gen, ulidText)
	if err != nil || id.String() != ulidText {
		t.Errorf("ParseFor[User] = %v, %v", id, err)
	}
	if _, err := idora.ParseFor[*User](gen, ulidText); err != nil {
		t.Errorf("ParseFor[*User]: %v", err)
	}
	if _, err := gen.ParseFor(reflect.TypeFor[User](), ulidText); err != nil {
		t.Errorf("gen.ParseFor: %v", err)
	}

	_, err = idora.ParseFor[Order](gen, ulidText)
	var invalid *idora.InvalidIDError
	if !errors.As(err, &invalid) || invalid.Reason != idora.ReasonPrefixMismatch || invalid.ExpectedPrefix != "ord" {
		t.Errorf("ParseFor[Order] on a usr identifier: %v", err)
	}

	// An unregistered type is the program's fault, not the identifier's, whatever the input.
	for _, s := range []string{ulidText, "garbage"} {
		_, err = idora.ParseFor[Comment](gen, s)
		if !errors.Is(err, idora.ErrPrefixNotRegistered) || errors.Is(err, idora.ErrInvalidID) {
			t.Errorf("ParseFor[Comment](%q): %v, want ErrPrefixNotRegistered", s, err)
		}
	}
}

func TestIsValidFor(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	if !idora.IsValidFor[User](gen, ulidText) || idora.IsValidFor[User](gen, uuidText) ||
		idora.IsValidFor[User](gen, "usr_bad") || !gen.IsValidFor(reflect.TypeFor[*User](), ulidText) {
		t.Error("IsValidFor gave the wrong answer")
	}
	defer func() {
		if err, _ := recover().(error); !errors.Is(err, idora.ErrPrefixNotRegistered) {
			t.Errorf("recovered %v, want ErrPrefixNotRegistered", err)
		}
	}()
	idora.IsValidFor[Comment](gen, ulidText)
	t.Error("IsValidFor did not panic for an unregistered type")
}

func TestGenerateID(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
		for range 1000 {
			id, err := idora.GenerateID[User](gen)
			if err != nil {
				t.Fatal(err)
			}
			// Every field matches what parsing the text gives.
			if parsed := idora.MustParse(id.String()); parsed != id {
				t.Fatalf("%v: GenerateID gave %#v, parsing its text gives %#v", s, id, parsed)
			}
		}
		if _, err := idora.GenerateID[Comment](gen); !errors.Is(err, idora.ErrPrefixNotRegistered) {
			t.Errorf("GenerateID[Comment]: %v", err)
		}
		if _, err := gen.GenerateIDFor(reflect.TypeFor[*User]()); err != nil {
			t.Errorf("GenerateIDFor(*User): %v", err)
		}
	}
}

// A ULID stream that has borrowed a millisecond reports the timestamp it encoded, not the clock.
func TestGenerateIDMatchesTheEncodedTimestamp(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	prev := idora.MustGenerateID[User](gen)
	for range 100_000 {
		id := idora.MustGenerateID[User](gen)
		if !id.After(prev) || id.UnixMilli() != idora.MustParse(id.String()).UnixMilli() {
			t.Fatalf("%s after %s", id, prev)
		}
		prev = id
	}
}

func TestInvalidIDErrorMessages(t *testing.T) {
	long := "usr_" + strings.Repeat("a", 200)
	cases := map[string]string{
		"nope":                                     `idora: "nope" is not an identifier: it contains no separator '_'; an identifier is a prefix, an underscore and a value, for example "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"`,
		"us-r_01k226e0g0008j4ct4ank7f24s":          `idora: "us-r_01k226e0g0008j4ct4ank7f24s" is not a valid identifier: the entity prefix "us-r" contains the character '-' at index 2; prefixes may contain only ASCII letters and digits`,
		"usr_":                                     `idora: "usr_" is not a valid identifier: it has nothing after the separator; a value is a 26-character lowercase ULID or a 32-character lowercase UUIDv7 without hyphens`,
		"usr_01K226E0G0008J4CT4ANK7F24S":           `idora: "usr_01K226E0G0008J4CT4ANK7F24S" is not a valid identifier: the value "01K226E0G0008J4CT4ANK7F24S" has a length of 26, so it must be a ULID, but contains 'K' at index 2, which is not in the lowercase Crockford Base32 alphabet (0-9 and a-z without i, l, o and u)`,
		"usr_81k226e0g0008j4ct4ank7f24s":           `idora: "usr_81k226e0g0008j4ct4ank7f24s" is not a valid identifier: the value "81k226e0g0008j4ct4ank7f24s" has a length of 26, so it must be a ULID, but starts with '8'; the first character of a ULID is at most '7', or it would not fit in 128 bits`,
		"inv_00000000000140008000000000000001":     `idora: "inv_00000000000140008000000000000001" is not a valid identifier: the value "00000000000140008000000000000001" has a length of 32, so it must be a UUIDv7, but its version digit (index 12) is '4' rather than '7'`,
		"inv_0000000000017000c000000000000001":     `idora: "inv_0000000000017000c000000000000001" is not a valid identifier: the value "0000000000017000c000000000000001" has a length of 32, so it must be a UUIDv7, but its variant digit (index 16) is 'c' rather than one of '8', '9', 'a' or 'b'`,
		"inv_0000000000017000800000000000000g":     `idora: "inv_0000000000017000800000000000000g" is not a valid identifier: the value "0000000000017000800000000000000g" has a length of 32, so it must be a UUIDv7, but contains 'g' at index 31, which is not a lowercase hexadecimal digit`,
		"inv_01988467-0200-7011-a233-445566778899": `idora: "inv_01988467-0200-7011-a233-445566778899" is not a valid identifier: the value "01988467-0200-7011-a233-445566778899" has a length of 36; a value is a 26-character lowercase ULID or a 32-character lowercase UUIDv7 without hyphens, and hyphens are not accepted`,
	}
	for text, want := range cases {
		_, err := idora.Parse(text)
		if err == nil || err.Error() != want {
			t.Errorf("Parse(%q):\n got %v\nwant %s", text, err, want)
		}
	}

	_, err := idora.ParsePrefixed(ulidText, "ord")
	if want := `idora: "usr_01k226e0g0008j4ct4ank7f24s" has the prefix "usr", but the prefix "ord" was expected; prefixes are compared exactly, case included, so the identifier belongs to a different entity type`; err.Error() != want {
		t.Errorf("mismatch:\n got %v\nwant %s", err, want)
	}

	// Long input is shortened in the message but kept whole in the field.
	_, err = idora.Parse(long)
	var invalid *idora.InvalidIDError
	if !errors.As(err, &invalid) || invalid.Text != long || !strings.Contains(err.Error(), `aaaa"…`) || len(err.Error()) > 500 {
		t.Errorf("long input: %v", err)
	}
}

func TestInvalidIDErrorUnwrapsThePrefixError(t *testing.T) {
	_, err := idora.Parse("café_01k226e0g0008j4ct4ank7f24s")
	var prefixErr *idora.InvalidPrefixError
	if !errors.As(err, &prefixErr) || prefixErr.Reason != idora.ReasonCharacter || prefixErr.Index != 3 || prefixErr.Char != 'é' {
		t.Fatalf("errors.As(*InvalidPrefixError): %v", err)
	}
	if !errors.Is(err, idora.ErrInvalidPrefix) || !errors.Is(err, idora.ErrInvalidID) {
		t.Error("the error should match both ErrInvalidID and ErrInvalidPrefix")
	}
	_, err = idora.Parse("usr_bad")
	if errors.Is(err, idora.ErrInvalidPrefix) || errors.Unwrap(err) != nil {
		t.Errorf("a value error has no cause: %v", errors.Unwrap(err))
	}
}

func TestInvalidIDReasonString(t *testing.T) {
	if got := idora.InvalidIDReason(0).String(); got != "InvalidIDReason(0)" {
		t.Errorf("got %q", got)
	}
}

func TestParseRejectsInvalidUTF8AndControlCharacters(t *testing.T) {
	for _, s := range []string{
		"usr_01k226e0g0008j4ct4ank7f2\xff", "\xff_01k226e0g0008j4ct4ank7f24s",
		"usr_01k226e0g0008j4ct4ank7f2\x00", "usr\x00_01k226e0g0008j4ct4ank7f24s",
		"usr_0198846702007011a23344556677889\xe9",
	} {
		if _, err := idora.Parse(s); !errors.Is(err, idora.ErrInvalidID) || idora.IsValid(s) {
			t.Errorf("Parse(%q) accepted it", s)
		}
		_, err := idora.Parse(s)
		_ = err.Error() // the message builder copes with any byte
	}
}

type jsonEntity struct {
	ID       idora.ID  `json:"id"`
	Parent   idora.ID  `json:"parent"`
	Optional *idora.ID `json:"optional,omitempty"`
}

func TestJSON(t *testing.T) {
	in := jsonEntity{ID: idora.MustParse(ulidText)}
	b, err := json.Marshal(in)
	if err != nil {
		t.Fatal(err)
	}
	if want := `{"id":"` + ulidText + `","parent":""}`; string(b) != want {
		t.Errorf("Marshal = %s, want %s", b, want)
	}
	var out jsonEntity
	if err := json.Unmarshal(b, &out); err != nil || out != in {
		t.Errorf("round trip = %+v, %v", out, err)
	}

	// null leaves the zero ID, as encoding/json does for any value type.
	if err := json.Unmarshal([]byte(`{"id":null}`), &out); err != nil {
		t.Errorf("null: %v", err)
	}

	err = json.Unmarshal([]byte(`{"id":"usr_nope"}`), &out)
	if !errors.Is(err, idora.ErrInvalidID) {
		t.Errorf("invalid identifier in JSON: %v", err)
	}
	if err := json.Unmarshal([]byte(`{"id":42}`), &out); err == nil {
		t.Error("a number decoded as an ID")
	}

	// Identifiers work as map keys.
	m := map[idora.ID]int{idora.MustParse(ulidText): 1}
	b, _ = json.Marshal(m)
	var back map[idora.ID]int
	if err := json.Unmarshal(b, &back); err != nil || back[idora.MustParse(ulidText)] != 1 {
		t.Errorf("map keys: %s, %v, %v", b, back, err)
	}
}

func TestUnmarshalTextLeavesTheIDUnchangedOnError(t *testing.T) {
	id := idora.MustParse(ulidText)
	if err := id.UnmarshalText([]byte("bad")); err == nil || id.String() != ulidText {
		t.Errorf("after a failed UnmarshalText: %q, %v", id, err)
	}
	// The ID owns its text: changing the input slice afterwards changes nothing.
	buf := []byte(uuidText)
	if err := id.UnmarshalText(buf); err != nil {
		t.Fatal(err)
	}
	buf[0] = 'X'
	if id.String() != uuidText {
		t.Errorf("UnmarshalText aliased its input: %q", id)
	}
}

func TestSQLScanAndValue(t *testing.T) {
	var id idora.ID
	for _, src := range []any{ulidText, []byte(ulidText)} {
		id = idora.ID{}
		if err := id.Scan(src); err != nil || id.String() != ulidText {
			t.Errorf("Scan(%T) = %q, %v", src, id, err)
		}
	}
	buf := []byte(uuidText)
	_ = id.Scan(buf)
	buf[0] = 'X'
	if id.String() != uuidText {
		t.Errorf("Scan aliased the driver's buffer: %q", id)
	}
	if err := id.Scan(nil); err != nil || !id.IsZero() {
		t.Errorf("Scan(nil) = %q, %v", id, err)
	}

	id = idora.MustParse(ulidText)
	for _, src := range []any{"", "usr_bad", 42, time.Now()} {
		if err := id.Scan(src); err == nil || id.String() != ulidText {
			t.Errorf("Scan(%#v) = %q, %v; want an error and no change", src, id, err)
		}
	}
	if err := id.Scan(42); err == nil || !strings.Contains(err.Error(), "cannot scan a int") {
		t.Errorf("Scan(42): %v", err)
	}

	v, err := idora.MustParse(ulidText).Value()
	if s, ok := v.(string); !ok || s != ulidText || err != nil {
		t.Errorf("Value() = %#v, %v", v, err)
	}
}

func TestBoundsContainEveryIdentifierInTheSpan(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
		from := time.Now()
		var ids []string
		for range 2000 {
			ids = append(ids, idora.MustGenerate[User](gen))
		}
		time.Sleep(2 * time.Millisecond)
		to := time.Now()
		lo, err := idora.LowerBound("usr", from, s)
		if err != nil {
			t.Fatal(err)
		}
		hi, err := idora.UpperBound("usr", to, s)
		if err != nil {
			t.Fatal(err)
		}
		for _, id := range ids {
			if id < lo.String() || id > hi.String() {
				t.Fatalf("%v: %s is outside [%s, %s]", s, id, lo, hi)
			}
		}
		// And an identifier from just after the span is outside it.
		time.Sleep(2 * time.Millisecond)
		if later := idora.MustGenerate[User](gen); later <= hi.String() {
			t.Errorf("%v: %s was minted after the span but is <= %s", s, later, hi)
		}
	}
}

func TestBoundsReject(t *testing.T) {
	now := time.Now()
	if _, err := idora.LowerBound("us_r", now, idora.ULID); !errors.Is(err, idora.ErrInvalidPrefix) {
		t.Errorf("invalid prefix: %v", err)
	}
	if _, err := idora.UpperBound("usr", now, idora.Strategy(9)); err == nil || !strings.Contains(err.Error(), "Strategy(9)") {
		t.Errorf("undefined strategy: %v", err)
	}
	maxTime := time.UnixMilli(1<<48 - 1)
	for _, at := range []time.Time{
		time.Unix(0, 0).Add(-time.Nanosecond), time.Time{}, maxTime.Add(time.Millisecond),
		time.Date(292277026596, 1, 1, 0, 0, 0, 0, time.UTC), // UnixMilli overflows here
	} {
		if _, err := idora.LowerBoundValue(at, idora.ULID); err == nil || !strings.Contains(err.Error(), "outside the range") {
			t.Errorf("LowerBoundValue(%v): %v", at, err)
		}
	}
	// The extremes themselves are fine, including the last nanosecond of the last millisecond.
	if v, err := idora.UpperBoundValue(maxTime.Add(999_999), idora.ULID); err != nil || v != "7zzzzzzzzzzzzzzzzzzzzzzzzz" {
		t.Errorf("UpperBoundValue(max) = %q, %v", v, err)
	}
	if v, err := idora.LowerBoundValue(time.Unix(0, 0), idora.UUIDv7); err != nil || v != "00000000000070008000000000000000" {
		t.Errorf("LowerBoundValue(epoch) = %q, %v", v, err)
	}
}
