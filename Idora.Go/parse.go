package idora

import (
	"reflect"
	"strings"
)

// Parse reads a conforming identifier into an [ID]. Parsing is strict: it accepts exactly the
// text a conforming generator emits, of either format, and nothing else. There is no trimming,
// no case folding and no tolerance for hyphens.
//
// The checks run in this order, and the first failure is returned as an [*InvalidIDError]
// carrying the matching reason:
//
//  1. [ReasonNoSeparator]: s contains no underscore.
//  2. [ReasonPrefix]: the text before the first underscore is not a valid prefix (1 to
//     [MaxPrefixLen] ASCII letters or digits). The error wraps the [*InvalidPrefixError].
//  3. [ReasonValue]: the text after it is neither a ULID (26 lowercase Crockford Base32
//     characters, the first no greater than 7) nor a UUIDv7 (32 lowercase hexadecimal
//     characters with the version 7 and the RFC 9562 variant).
//
// The returned ID shares s rather than copying it, so Parse does not allocate on success.
func Parse(s string) (ID, error) { return parse(s, "", false) }

// ParsePrefixed is [Parse] for an identifier that must carry a particular prefix. After the
// checks of Parse it returns an [*InvalidIDError] with [ReasonPrefixMismatch] unless the prefix
// is exactly prefix: an ordinal comparison, case included, so "USR_…" does not match "usr".
//
// Where the prefix belongs to a registered entity type, [ParseFor] keeps it in one place.
func ParsePrefixed(s, prefix string) (ID, error) { return parse(s, prefix, true) }

// MustParse is like [Parse] but panics if s is not a valid identifier. It suits constants in
// tests and initialisation code, never input.
func MustParse(s string) ID {
	id, err := Parse(s)
	if err != nil {
		panic(err)
	}
	return id
}

// IsValid reports whether s is a valid identifier: whether [Parse] would succeed. It does not
// allocate.
func IsValid(s string) bool {
	_, ok := scan(s)
	return ok
}

// IsValidPrefixed reports whether s is a valid identifier whose prefix is exactly prefix:
// whether [ParsePrefixed] would succeed. It does not allocate.
func IsValidPrefixed(s, prefix string) bool {
	id, ok := scan(s)
	return ok && id.Prefix() == prefix
}

// ParseFor parses an identifier that must belong to entity type T: it looks up the prefix T
// was registered with and applies [ParsePrefixed]. A pointer type uses its element type's prefix.
//
// When T has no registered prefix it returns a [*PrefixNotRegisteredError] rather than an
// [*InvalidIDError], whatever s holds: the identifier is not at fault.
//
// ParseFor is a function rather than a method because Go methods cannot have type parameters.
func ParseFor[T any](g *Generator, s string) (ID, error) {
	return g.ParseFor(reflect.TypeFor[T](), s)
}

// ParseFor is the generic [ParseFor] for an entity type known only at run time.
func (g *Generator) ParseFor(t reflect.Type, s string) (ID, error) {
	prefix, err := g.Prefix(t)
	if err != nil {
		return ID{}, err
	}
	return parse(s, prefix, true)
}

// IsValidFor reports whether s is a valid identifier for entity type T: whether [ParseFor]
// would succeed.
//
// It panics with a [*PrefixNotRegisteredError] if T has no registered prefix, like
// [MustGenerate]: an unregistered type is a mistake in the program, not a bad identifier, and
// answering false would hide it behind every identifier looking invalid. Use [ParseFor] to get
// the error instead.
func IsValidFor[T any](g *Generator, s string) bool {
	return g.IsValidFor(reflect.TypeFor[T](), s)
}

// IsValidFor is the generic [IsValidFor] for an entity type known only at run time. It panics
// in the same way when t has no registered prefix.
func (g *Generator) IsValidFor(t reflect.Type, s string) bool {
	prefix, err := g.Prefix(t)
	if err != nil {
		panic(err)
	}
	return IsValidPrefixed(s, prefix)
}

// GenerateID is [Generate] returning a parsed [ID] instead of a string, for code that keeps
// identifiers as IDs. It costs the same: one allocation, the identifier's text.
func GenerateID[T any](g *Generator) (ID, error) {
	return g.GenerateIDFor(reflect.TypeFor[T]())
}

// MustGenerateID is like [GenerateID] but panics if T has no registered prefix.
func MustGenerateID[T any](g *Generator) ID {
	id, err := g.GenerateIDFor(reflect.TypeFor[T]())
	if err != nil {
		panic(err)
	}
	return id
}

// GenerateIDFor is [GenerateID] for an entity type known only at run time.
func (g *Generator) GenerateIDFor(t reflect.Type) (ID, error) {
	prefix, ok := g.prefixes[entityKey(t)]
	if !ok {
		return ID{}, &PrefixNotRegisteredError{EntityType: entityKey(t)}
	}
	return g.compose(prefix), nil
}

// parse applies the rules of spec §6.1 in order. When check is true the prefix must equal
// expected.
func parse(s, expected string, check bool) (ID, error) {
	id, ok := scan(s)
	if !ok {
		return ID{}, diagnose(s)
	}
	if check && id.Prefix() != expected {
		return ID{}, &InvalidIDError{Text: s, Reason: ReasonPrefixMismatch, ExpectedPrefix: expected}
	}
	return id, nil
}

// scan is the success path of parsing: it validates s and decodes its timestamp, reporting only
// whether it succeeded, so neither it nor its callers allocate. [diagnose] works out why a
// rejected identifier failed.
func scan(s string) (ID, bool) {
	sep := strings.IndexByte(s, Separator)
	// A valid prefix is at most MaxPrefixLen ASCII characters, so a separator further in means
	// an invalid prefix and saves validatePrefix from counting characters.
	if sep < 1 || sep > MaxPrefixLen {
		return ID{}, false
	}
	for i := range sep {
		if !isASCIIAlnum(s[i]) {
			return ID{}, false
		}
	}
	strategy, ms, ok := scanValue(s[sep+1:])
	if !ok {
		return ID{}, false
	}
	return ID{text: s, ms: ms, sep: uint8(sep), strategy: strategy}, true
}

// diagnose builds the error for an identifier [scan] rejected, applying the checks in the order
// of spec §6.1.
func diagnose(s string) error {
	sep := strings.IndexByte(s, Separator)
	if sep < 0 {
		return &InvalidIDError{Text: s, Reason: ReasonNoSeparator}
	}
	if err := validatePrefix(s[:sep]); err != nil {
		return &InvalidIDError{Text: s, Reason: ReasonPrefix, Cause: err.(*InvalidPrefixError)}
	}
	return &InvalidIDError{Text: s, Reason: ReasonValue}
}

// crockfordValue maps a byte to its value in the lowercase Crockford alphabet, or 0xff.
var crockfordValue = func() (t [256]byte) {
	for i := range t {
		t[i] = 0xff
	}
	for i := range len(crockford) {
		t[crockford[i]] = byte(i)
	}
	return t
}()

// hexValue maps a byte to its value as a lowercase hexadecimal digit, or 0xff.
var hexValue = func() (t [256]byte) {
	for i := range t {
		t[i] = 0xff
	}
	for i := range len(hexDigits) {
		t[hexDigits[i]] = byte(i)
	}
	return t
}()

// scanValue recognises a ULID or a UUIDv7 by its length, validates every character and decodes
// the 48-bit timestamp it begins with.
func scanValue(v string) (Strategy, uint64, bool) {
	switch len(v) {
	case ulidLen:
		ms, ok := scanULID(v)
		return ULID, ms, ok
	case uuidV7Len:
		ms, ok := scanUUIDv7(v)
		return UUIDv7, ms, ok
	default:
		return 0, 0, false
	}
}

// scanULID checks a 26-character ULID and decodes characters 0–9, its timestamp. The first
// character carries only three bits, so anything above 7 would overflow 128 bits.
func scanULID(v string) (uint64, bool) {
	v = v[:ulidLen]
	if v[0] > '7' {
		return 0, false
	}
	var ms uint64
	for i := range 10 {
		c := crockfordValue[v[i]]
		if c == 0xff {
			return 0, false
		}
		ms = ms<<5 | uint64(c)
	}
	for i := 10; i < ulidLen; i++ {
		if crockfordValue[v[i]] == 0xff {
			return 0, false
		}
	}
	return ms, true
}

// scanUUIDv7 checks a 32-character UUIDv7: lowercase hexadecimal throughout, the version nibble
// 7 at character 12 and the RFC 9562 variant (8, 9, a or b) at character 16. Characters 0–11
// are its timestamp.
func scanUUIDv7(v string) (uint64, bool) {
	v = v[:uuidV7Len]
	if v[12] != '7' {
		return 0, false
	}
	switch v[16] {
	case '8', '9', 'a', 'b':
	default:
		return 0, false
	}
	var ms uint64
	for i := range 12 {
		c := hexValue[v[i]]
		if c == 0xff {
			return 0, false
		}
		ms = ms<<4 | uint64(c)
	}
	for i := 12; i < uuidV7Len; i++ {
		if hexValue[v[i]] == 0xff {
			return 0, false
		}
	}
	return ms, true
}
