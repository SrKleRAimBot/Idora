package idora

import (
	"cmp"
	"database/sql/driver"
	"fmt"
	"strings"
	"time"
)

// ID is a parsed identifier: its prefix, its value, the value's format and the millisecond
// timestamp the value begins with. Build one with [Parse], [ParsePrefixed], [ParseFor] or
// [GenerateID]. It accepts any conforming identifier, whichever strategy the local [Generator]
// uses and whichever Idora package minted it.
//
// An ID is a small immutable value. Pass it by value, compare it with ==, and use it as a map
// key: two IDs are == exactly when their texts are equal, which is also what [ID.Equal] reports.
// Parsing keeps the input string rather than copying it, so [Parse] does not allocate.
//
// The zero ID holds no identifier. [ID.IsZero] reports it; its [ID.String], [ID.Prefix] and
// [ID.ValuePart] are empty, its timestamp reads as 0 except that [ID.Time] returns the zero
// [time.Time], and it sorts before every identifier. It encodes as empty text and as SQL NULL,
// so an optional identifier needs no pointer.
//
// ID implements [encoding.TextMarshaler] and [encoding.TextUnmarshaler], so encoding/json and
// encoding/xml read and write it as a string, and [database/sql.Scanner] and [driver.Valuer], so
// it can be a column, a query argument or a scan destination.
type ID struct {
	// text comes first: GORM and similar libraries infer a column type for a Valuer struct from
	// its first field, so this makes an ID column a string column without any tags.
	text     string
	ms       uint64
	sep      uint8 // index of the separator in text, which is the prefix length
	strategy Strategy
}

// IsZero reports whether id is the zero ID, holding no identifier.
func (id ID) IsZero() bool { return id.text == "" }

// String returns the whole identifier, exactly the text it was parsed from, or "" for the zero
// ID.
func (id ID) String() string { return id.text }

// Prefix returns the text before the first separator, exactly as written and without the
// separator.
func (id ID) Prefix() string { return id.text[:id.sep] }

// ValuePart returns the text after the first separator: the 26-character ULID or the
// 32-character UUIDv7.
//
// It is not called Value because [ID.Value] is the [driver.Valuer] method that database/sql
// requires.
func (id ID) ValuePart() string {
	if id.text == "" {
		return ""
	}
	return id.text[id.sep+1:]
}

// Strategy reports the format of the value, [ULID] or [UUIDv7]: the same enumeration that
// selects what a [Generator] produces. The zero ID reports ULID, the zero Strategy.
func (id ID) Strategy() Strategy { return id.strategy }

// UnixMilli returns the 48-bit millisecond timestamp the value begins with: milliseconds since
// the Unix epoch, independent of the clock and the time zone of the reader.
func (id ID) UnixMilli() int64 { return int64(id.ms) }

// Time returns the instant the value's timestamp names, in UTC and at millisecond precision. The
// zero ID returns the zero time.Time.
func (id ID) Time() time.Time {
	if id.text == "" {
		return time.Time{}
	}
	return time.UnixMilli(int64(id.ms)).UTC()
}

// HasPrefix reports whether the identifier's prefix is exactly prefix: an ordinal comparison,
// case included, without the separator.
func (id ID) HasPrefix(prefix string) bool { return id.Prefix() == prefix }

// Equal reports whether id and other are the same identifier, which is the same as id == other.
func (id ID) Equal(other ID) bool { return id.text == other.text }

// Compare returns -1, 0 or +1 as id sorts before, with or after other in the natural order: by
// timestamp, oldest first; then by value; then by prefix, comparing strings ordinally. It is 0
// exactly when the two are equal.
//
// The order is chronological across entity types and formats: an order created after a user
// sorts after that user, although "ord" < "usr". For ULIDs from one [Generator] it is exactly
// generation order. The method expression suits the slices package:
//
//	slices.SortFunc(ids, idora.ID.Compare)
//
// Sorting the strings themselves gives a different order, grouped by prefix, which is what a
// database index on the column holds.
func (id ID) Compare(other ID) int {
	if c := cmp.Compare(id.ms, other.ms); c != 0 {
		return c
	}
	if c := strings.Compare(id.ValuePart(), other.ValuePart()); c != 0 {
		return c
	}
	return strings.Compare(id.Prefix(), other.Prefix())
}

// Before reports whether id sorts strictly before other in the natural order of [ID.Compare].
func (id ID) Before(other ID) bool { return id.Compare(other) < 0 }

// After reports whether id sorts strictly after other in the natural order of [ID.Compare].
func (id ID) After(other ID) bool { return id.Compare(other) > 0 }

// CompareTime compares the timestamps alone, returning -1, 0 or +1 as id was minted in an
// earlier, the same or a later millisecond than other.
func (id ID) CompareTime(other ID) int { return cmp.Compare(id.ms, other.ms) }

// SameMillisecond reports whether id and other were minted in the same millisecond.
func (id ID) SameMillisecond(other ID) bool { return id.ms == other.ms }

// CreatedBefore reports whether the identifier was minted strictly before t, at millisecond
// precision: t's sub-millisecond part is dropped before comparing, so an identifier is never
// created before the millisecond it was minted in.
//
// It compares instants rather than t.UnixMilli(), which overflows for a t far enough from the
// epoch: minted before t's millisecond is the same as t being at or after the next millisecond.
func (id ID) CreatedBefore(t time.Time) bool { return !t.Before(time.UnixMilli(int64(id.ms) + 1)) }

// CreatedAfter reports whether the identifier was minted strictly after t, at millisecond
// precision: t's sub-millisecond part is dropped before comparing.
//
// Like [ID.CreatedBefore], it compares instants, so a t of any magnitude is safe: minted after
// t's millisecond is the same as t falling before the identifier's own.
func (id ID) CreatedAfter(t time.Time) bool { return t.Before(time.UnixMilli(int64(id.ms))) }

// MarshalText implements [encoding.TextMarshaler]. The text is the identifier itself, and empty
// for the zero ID.
func (id ID) MarshalText() ([]byte, error) { return []byte(id.text), nil }

// UnmarshalText implements [encoding.TextUnmarshaler] by parsing text with [Parse]. Empty text
// decodes to the zero ID, so the zero value round-trips; any other text must be a valid
// identifier, and an invalid one leaves id unchanged and returns an [*InvalidIDError].
//
// To reject an empty or missing identifier, check [ID.IsZero] after decoding.
func (id *ID) UnmarshalText(text []byte) error {
	if len(text) == 0 {
		*id = ID{}
		return nil
	}
	parsed, err := Parse(string(text))
	if err != nil {
		return err
	}
	*id = parsed
	return nil
}

// Value implements [driver.Valuer]: the identifier as a string, or nil (SQL NULL) for the zero
// ID.
func (id ID) Value() (driver.Value, error) {
	if id.text == "" {
		return nil, nil
	}
	return id.text, nil
}

// Scan implements [database/sql.Scanner]. It accepts a string or []byte holding a valid
// identifier, and NULL, which scans as the zero ID. Anything else, including an empty string,
// returns an error and leaves id unchanged.
func (id *ID) Scan(src any) error {
	var parsed ID
	var err error
	switch v := src.(type) {
	case nil:
		*id = ID{}
		return nil
	case string:
		parsed, err = Parse(v)
	case []byte:
		// The driver may reuse the slice after Scan returns, so the text is copied.
		parsed, err = Parse(string(v))
	default:
		return fmt.Errorf("idora: cannot scan a %T into an idora.ID; store identifiers in a text "+
			"column such as char(30) or varchar", src)
	}
	if err != nil {
		return err
	}
	*id = parsed
	return nil
}
