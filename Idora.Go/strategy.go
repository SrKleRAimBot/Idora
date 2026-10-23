package idora

import "strconv"

// Strategy selects the format of the value that follows the prefix.
//
// The zero value is [ULID].
type Strategy uint8

const (
	// ULID produces a 26-character lowercase Crockford Base32 ULID: a 48-bit millisecond
	// timestamp followed by 80 random bits, strictly increasing within one [Generator]. This is
	// the default.
	ULID Strategy = iota

	// UUIDv7 produces an RFC 9562 version 7 UUID written as 32 lowercase hexadecimal characters
	// without hyphens: a 48-bit millisecond timestamp followed by 74 random bits.
	UUIDv7
)

// String returns "ULID", "UUIDv7", or "Strategy(n)" for an undefined value.
func (s Strategy) String() string {
	switch s {
	case ULID:
		return "ULID"
	case UUIDv7:
		return "UUIDv7"
	default:
		return "Strategy(" + strconv.Itoa(int(s)) + ")"
	}
}

// valid reports whether s is one of the defined strategies.
func (s Strategy) valid() bool { return s == ULID || s == UUIDv7 }

// valueLen is the number of characters the strategy writes after the separator.
func (s Strategy) valueLen() int {
	if s == UUIDv7 {
		return uuidV7Len
	}
	return ulidLen
}
