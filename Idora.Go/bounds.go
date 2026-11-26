package idora

import (
	"fmt"
	"math"
	"time"
)

// maxTimestamp is the largest millisecond count a value's 48-bit timestamp can hold.
const maxTimestamp = 1<<48 - 1

// LowerBound returns the lowest identifier with the given prefix that any conforming generator
// could mint during the millisecond containing t, in format s. With [UpperBound] it turns a span
// of time into a range of strings:
//
//	lo, _ := idora.LowerBound("usr", from, idora.ULID)
//	hi, _ := idora.UpperBound("usr", to, idora.ULID)
//	rows, err := db.Query(`SELECT … WHERE id >= $1 AND id <= $2`, lo, hi)
//
// selects every "usr" identifier minted in [from, to], at millisecond precision, without parsing
// a single row. t's sub-millisecond part is dropped.
//
// The bounds compare correctly only against identifiers of the same prefix and format, as plain
// strings: the shape of a database column that holds one entity type in one format. They are
// themselves valid identifiers.
//
// It returns an [*InvalidPrefixError] for an invalid prefix, and a plain error for an undefined
// strategy or a t outside the range a 48-bit timestamp can hold, from the Unix epoch to the
// year 10889.
func LowerBound(prefix string, t time.Time, s Strategy) (ID, error) {
	return bound(prefix, t, s, false)
}

// UpperBound returns the highest identifier with the given prefix that any conforming generator
// could mint during the millisecond containing t, in format s. See [LowerBound].
func UpperBound(prefix string, t time.Time, s Strategy) (ID, error) { return bound(prefix, t, s, true) }

// LowerBoundValue returns the lowest value, without a prefix, that any conforming generator
// could produce during the millisecond containing t, in format s: the timestamp followed by
// sixteen "0" for a ULID, and by "7000", "8000" and twelve "0" for a UUIDv7. It fails as
// [LowerBound] does.
func LowerBoundValue(t time.Time, s Strategy) (string, error) {
	id, err := bound("x", t, s, false)
	return id.ValuePart(), err
}

// UpperBoundValue returns the highest value, without a prefix, that any conforming generator
// could produce during the millisecond containing t, in format s: the timestamp followed by
// sixteen "z" for a ULID, and by "7fff", "bfff" and twelve "f" for a UUIDv7. It fails as
// [LowerBound] does.
func UpperBoundValue(t time.Time, s Strategy) (string, error) {
	id, err := bound("x", t, s, true)
	return id.ValuePart(), err
}

// bound builds "<prefix>_<lowest or highest value>" for the millisecond containing t.
func bound(prefix string, t time.Time, s Strategy, upper bool) (ID, error) {
	if err := validatePrefix(prefix); err != nil {
		return ID{}, err
	}
	if !s.valid() {
		return ID{}, fmt.Errorf("idora: %s is not a defined strategy; use idora.ULID or idora.UUIDv7", s)
	}
	// Compared as instants, because UnixMilli overflows for times far enough from the epoch.
	if t.Before(time.Unix(0, 0)) || !t.Before(time.UnixMilli(maxTimestamp+1)) {
		return ID{}, fmt.Errorf("idora: %s is outside the range of an identifier's timestamp, from %s "+
			"to %s; no identifier can have been minted then, so clamp the time to that range",
			t.UTC().Format(time.RFC3339Nano), time.Unix(0, 0).UTC().Format(time.RFC3339Nano),
			time.UnixMilli(maxTimestamp).UTC().Format(time.RFC3339Nano))
	}
	ms := uint64(t.UnixMilli())

	var buf [maxIDLen]byte
	n := copy(buf[:], prefix)
	buf[n] = Separator
	n++
	if s == UUIDv7 {
		var random [uuidV7RandomLen]byte
		if upper {
			for i := range random {
				random[i] = 0xff
			}
		}
		// composeUUIDv7 forces the version and variant bits, which turns all-zero random octets
		// into 7000 8000 and all-one octets into 7fff bfff.
		encodeUUIDv7(buf[n:], ms, &random)
	} else if upper {
		encodeULID(buf[n:], ms, math.MaxUint16, math.MaxUint64)
	} else {
		encodeULID(buf[n:], ms, 0, 0)
	}
	n += s.valueLen()
	return ID{text: string(buf[:n]), ms: ms, sep: uint8(len(prefix)), strategy: s}, nil
}
