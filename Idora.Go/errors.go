package idora

import (
	"errors"
	"fmt"
	"reflect"
	"strconv"
	"strings"
	"unicode/utf8"
)

// Sentinel errors. Every error this package returns matches one of them under [errors.Is]; use
// [errors.As] with the corresponding typed error for the details. An [*InvalidIDError] whose
// prefix was at fault also matches [ErrInvalidPrefix], through the [*InvalidPrefixError] it wraps.
var (
	// ErrInvalidPrefix reports a prefix that is empty, longer than [MaxPrefixLen] characters, or
	// contains something other than an ASCII letter or digit. See [*InvalidPrefixError].
	ErrInvalidPrefix = errors.New("idora: invalid entity prefix")

	// ErrDuplicatePrefix reports a prefix registered for two entity types (compared ignoring
	// ASCII case), or one entity type registered twice. See [*DuplicatePrefixError].
	ErrDuplicatePrefix = errors.New("idora: duplicate entity prefix")

	// ErrPrefixNotRegistered reports a request for the prefix of an entity type that was never
	// registered. See [*PrefixNotRegisteredError].
	ErrPrefixNotRegistered = errors.New("idora: entity prefix not registered")

	// ErrPromotedPrefix reports a type passed to [WithEntities] whose IDPrefix method is promoted
	// from an embedded field rather than declared on the type itself. Declarations are not
	// inherited. See [*PromotedPrefixError].
	ErrPromotedPrefix = errors.New("idora: promoted IDPrefix method")

	// ErrInvalidID reports text that is not a valid identifier, or one with a prefix other than
	// the one expected. See [*InvalidIDError].
	ErrInvalidID = errors.New("idora: invalid identifier")
)

// InvalidPrefixReason says which prefix rule an [InvalidPrefixError] broke. Its String form
// matches the reason names in the Idora conformance vectors.
type InvalidPrefixReason uint8

const (
	// ReasonEmpty means the prefix has no characters.
	ReasonEmpty InvalidPrefixReason = iota + 1
	// ReasonTooLong means the prefix has more than [MaxPrefixLen] characters.
	ReasonTooLong
	// ReasonSeparator means the prefix contains the [Separator], which would make an identifier
	// impossible to split back apart.
	ReasonSeparator
	// ReasonCharacter means the prefix contains a character that is not an ASCII letter or digit.
	ReasonCharacter
)

// String returns "empty", "tooLong", "separator" or "character".
func (r InvalidPrefixReason) String() string {
	switch r {
	case ReasonEmpty:
		return "empty"
	case ReasonTooLong:
		return "tooLong"
	case ReasonSeparator:
		return "separator"
	case ReasonCharacter:
		return "character"
	default:
		return "InvalidPrefixReason(" + strconv.Itoa(int(r)) + ")"
	}
}

// InvalidPrefixError reports a prefix that breaks the rules: it must be 1 to [MaxPrefixLen]
// ASCII letters or digits. It matches [ErrInvalidPrefix].
type InvalidPrefixError struct {
	// Prefix is the rejected prefix, exactly as supplied.
	Prefix string
	// Reason is the first rule the prefix broke.
	Reason InvalidPrefixReason
	// Index is the zero-based character (code point) index of the offending character for
	// ReasonSeparator and ReasonCharacter, and -1 otherwise.
	Index int
	// Char is the offending character for ReasonSeparator and ReasonCharacter, and 0 otherwise.
	// An invalid UTF-8 byte is reported as [utf8.RuneError].
	Char rune
	// Length is the length of the prefix in characters for ReasonTooLong, and 0 otherwise.
	Length int
	// EntityType is the type whose IDPrefix method declared the prefix, when the prefix came
	// from [WithEntities]; nil for explicit and ad-hoc prefixes.
	EntityType reflect.Type
}

// Error describes the broken rule and how to fix it.
func (e *InvalidPrefixError) Error() string {
	if e.EntityType != nil {
		return fmt.Sprintf("idora: the IDPrefix method of %s does not declare a valid prefix: %s",
			e.EntityType, e.detail())
	}
	return "idora: " + e.detail()
}

func (e *InvalidPrefixError) detail() string {
	switch e.Reason {
	case ReasonEmpty:
		return fmt.Sprintf("an entity prefix cannot be empty; supply 1 to %d ASCII letters or digits, "+
			"for example \"usr\"", MaxPrefixLen)
	case ReasonTooLong:
		// The prefix may be untrusted input, from an identifier being parsed, of any length.
		return fmt.Sprintf("the entity prefix %s is %d characters long; the maximum is %d, so shorten it",
			quoteText(e.Prefix), e.Length, MaxPrefixLen)
	case ReasonSeparator:
		return fmt.Sprintf("the entity prefix %q contains the separator %q at index %d; the separator "+
			"divides the prefix from the value, so a prefix cannot contain one", e.Prefix, e.Char, e.Index)
	default:
		return fmt.Sprintf("the entity prefix %q contains the character %q at index %d; prefixes may "+
			"contain only ASCII letters and digits", e.Prefix, e.Char, e.Index)
	}
}

// Is reports whether target is [ErrInvalidPrefix].
func (e *InvalidPrefixError) Is(target error) bool { return target == ErrInvalidPrefix }

// DuplicatePrefixError reports a registration that would make the registry ambiguous. It
// matches [ErrDuplicatePrefix].
//
// When the entity type itself was already registered, EntityType and ConflictingEntityType are
// the same type. Otherwise Prefix collided, ignoring ASCII case, with ExistingPrefix belonging
// to ConflictingEntityType.
type DuplicatePrefixError struct {
	// Prefix is the prefix being registered.
	Prefix string
	// ExistingPrefix is the prefix already in the registry that Prefix clashed with.
	ExistingPrefix string
	// EntityType is the entity type being registered.
	EntityType reflect.Type
	// ConflictingEntityType is the entity type that already holds ExistingPrefix.
	ConflictingEntityType reflect.Type
}

// Error names both types and the prefixes involved, and says how to fix it.
func (e *DuplicatePrefixError) Error() string {
	if e.EntityType == e.ConflictingEntityType {
		// Both registrations agreeing on the prefix means the mapping was declared twice rather
		// than declared differently: a different mistake with a different fix.
		if e.Prefix == e.ExistingPrefix {
			return fmt.Sprintf("idora: the entity type %s is already registered with the prefix %q, "+
				"and cannot be registered a second time; each entity type may be registered exactly once, "+
				"so look for a repeated WithEntity option, a type declaring IDPrefix that is also "+
				"registered with WithEntity, or the same type passed to WithEntities twice",
				e.EntityType, e.ExistingPrefix)
		}
		return fmt.Sprintf("idora: the entity type %s is already registered with the prefix %q and "+
			"cannot also be registered with %q; each entity type may have only one prefix, so remove "+
			"whichever registration is wrong: a WithEntity option or the type's IDPrefix method",
			e.EntityType, e.ExistingPrefix, e.Prefix)
	}
	return fmt.Sprintf("idora: the entity prefix %q is already registered for %s and cannot also be "+
		"used for %s; prefixes must identify exactly one entity type, and are compared ignoring case, "+
		"so choose a different prefix for %s",
		e.Prefix, e.ConflictingEntityType, e.EntityType, e.EntityType)
}

// Is reports whether target is [ErrDuplicatePrefix].
func (e *DuplicatePrefixError) Is(target error) bool { return target == ErrDuplicatePrefix }

// PrefixNotRegisteredError reports a lookup or generation for an entity type that has no
// registered prefix. The package never invents a prefix from a type's name. It matches
// [ErrPrefixNotRegistered].
type PrefixNotRegisteredError struct {
	// EntityType is the type that has no prefix, after pointer types are reduced to their
	// element type. It is nil when a nil reflect.Type was looked up.
	EntityType reflect.Type
}

// Error names the type and says how to register it.
func (e *PrefixNotRegisteredError) Error() string {
	return fmt.Sprintf("idora: no entity prefix is registered for %s; register one when building the "+
		"generator, for example idora.New(idora.WithEntity[%s](\"abc\")), or call GenerateWithPrefix "+
		"with an explicit prefix", typeString(e.EntityType), typeName(e.EntityType))
}

// Is reports whether target is [ErrPrefixNotRegistered].
func (e *PrefixNotRegisteredError) Is(target error) bool { return target == ErrPrefixNotRegistered }

// PromotedPrefixError reports a type passed to [WithEntities] that has an IDPrefix method only
// because it embeds a type that declares one. Go promotes methods of embedded fields, but an
// Idora prefix declaration is not inherited: an Admin that embeds User is not a User, and
// silently giving it "usr" would make the two indistinguishable. It matches [ErrPromotedPrefix].
type PromotedPrefixError struct {
	// EntityType is the type that was passed to WithEntities.
	EntityType reflect.Type
	// Field is the name of the embedded field the IDPrefix method is promoted from.
	Field string
}

// Error names the type and the embedded field, and says how to fix it.
func (e *PromotedPrefixError) Error() string {
	return fmt.Sprintf("idora: %s has an IDPrefix method only because it is promoted from its embedded "+
		"field %s, and prefix declarations are not inherited; declare IDPrefix on %s itself, or register "+
		"it explicitly with idora.WithEntity[%s](\"abc\")",
		e.EntityType, e.Field, e.EntityType, typeName(e.EntityType))
}

// Is reports whether target is [ErrPromotedPrefix].
func (e *PromotedPrefixError) Is(target error) bool { return target == ErrPromotedPrefix }

// InvalidIDReason says which parsing rule an [InvalidIDError] broke. Its String form matches the
// reason names in the Idora conformance vectors.
type InvalidIDReason uint8

const (
	// ReasonNoSeparator means the text contains no [Separator], so it has no prefix and value
	// to split into.
	ReasonNoSeparator InvalidIDReason = iota + 1
	// ReasonPrefix means the text before the first separator is not a valid prefix. The
	// [InvalidIDError] carries the [*InvalidPrefixError] as its Cause.
	ReasonPrefix
	// ReasonValue means the text after the first separator is neither a ULID nor a UUIDv7.
	ReasonValue
	// ReasonPrefixMismatch means the identifier is valid, but its prefix is not exactly the one
	// the caller asked for.
	ReasonPrefixMismatch
)

// String returns "noSeparator", "prefix", "value" or "prefixMismatch".
func (r InvalidIDReason) String() string {
	switch r {
	case ReasonNoSeparator:
		return "noSeparator"
	case ReasonPrefix:
		return "prefix"
	case ReasonValue:
		return "value"
	case ReasonPrefixMismatch:
		return "prefixMismatch"
	default:
		return "InvalidIDReason(" + strconv.Itoa(int(r)) + ")"
	}
}

// InvalidIDError reports text that [Parse] and its relatives rejected. It matches
// [ErrInvalidID], and for [ReasonPrefix] also [ErrInvalidPrefix], because it unwraps to Cause.
type InvalidIDError struct {
	// Text is the rejected input, exactly as supplied.
	Text string
	// Reason is the first rule the text broke.
	Reason InvalidIDReason
	// ExpectedPrefix is the prefix the caller asked for, for ReasonPrefixMismatch, and ""
	// otherwise.
	ExpectedPrefix string
	// Cause is the reason the prefix is invalid, for ReasonPrefix, and nil otherwise.
	Cause *InvalidPrefixError
}

// Error describes the broken rule and how to fix it.
func (e *InvalidIDError) Error() string {
	text := quoteText(e.Text)
	switch e.Reason {
	case ReasonNoSeparator:
		return fmt.Sprintf("idora: %s is not an identifier: it contains no separator %q; an identifier "+
			"is a prefix, an underscore and a value, for example \"usr_01k2f9v8ta7m3x0qbrn4dzeh5c\"",
			text, Separator)
	case ReasonPrefix:
		detail := "its prefix is invalid"
		if e.Cause != nil {
			detail = e.Cause.detail()
		}
		return fmt.Sprintf("idora: %s is not a valid identifier: %s", text, detail)
	case ReasonPrefixMismatch:
		prefix, _, _ := strings.Cut(e.Text, string(Separator))
		return fmt.Sprintf("idora: %s has the prefix %q, but the prefix %q was expected; prefixes "+
			"are compared exactly, case included, so the identifier belongs to a different entity type",
			text, prefix, e.ExpectedPrefix)
	default:
		_, value, _ := strings.Cut(e.Text, string(Separator))
		return fmt.Sprintf("idora: %s is not a valid identifier: %s", text, valueProblem(value))
	}
}

// Unwrap returns Cause, so errors.Is and errors.As see the [*InvalidPrefixError] behind
// ReasonPrefix. It returns nil for the other reasons.
func (e *InvalidIDError) Unwrap() error {
	if e.Cause == nil {
		return nil
	}
	return e.Cause
}

// Is reports whether target is [ErrInvalidID].
func (e *InvalidIDError) Is(target error) bool { return target == ErrInvalidID }

// valueProblem explains why the text after the separator is not a value. It is only called
// for text the parser rejected, and works out the reason again rather than making the parser
// record it.
func valueProblem(v string) string {
	const formats = "a value is a 26-character lowercase ULID or a 32-character lowercase UUIDv7 " +
		"without hyphens"
	switch len(v) {
	case 0:
		return "it has nothing after the separator; " + formats
	case ulidLen:
		if i := strings.IndexFunc(v, func(r rune) bool { return r > 0x7f || crockfordValue[r] == 0xff }); i >= 0 {
			return fmt.Sprintf("the value %q has a length of 26, so it must be a ULID, but contains %q "+
				"at index %d, which is not in the lowercase Crockford Base32 alphabet "+
				"(0-9 and a-z without i, l, o and u)", v, firstRune(v[i:]), i)
		}
		return fmt.Sprintf("the value %q has a length of 26, so it must be a ULID, but starts with %q; "+
			"the first character of a ULID is at most '7', or it would not fit in 128 bits", v, v[0])
	case uuidV7Len:
		if i := strings.IndexFunc(v, func(r rune) bool { return r > 0x7f || hexValue[r] == 0xff }); i >= 0 {
			return fmt.Sprintf("the value %q has a length of 32, so it must be a UUIDv7, but contains %q "+
				"at index %d, which is not a lowercase hexadecimal digit", v, firstRune(v[i:]), i)
		}
		if v[12] != '7' {
			return fmt.Sprintf("the value %q has a length of 32, so it must be a UUIDv7, but its version "+
				"digit (index 12) is %q rather than '7'", v, v[12])
		}
		return fmt.Sprintf("the value %q has a length of 32, so it must be a UUIDv7, but its variant "+
			"digit (index 16) is %q rather than one of '8', '9', 'a' or 'b'", v, v[16])
	default:
		hint := ""
		if strings.ContainsRune(v, '-') {
			hint = ", and hyphens are not accepted"
		}
		// Any length gets here, so the value is quoted shortened: it is untrusted input.
		return fmt.Sprintf("the value %s has a length of %d; %s%s", quoteText(v), len(v), formats, hint)
	}
}

func firstRune(s string) rune {
	for _, r := range s {
		return r
	}
	return 0
}

// quoteText quotes rejected input for a message, shortening it so that a megabyte of garbage
// in a request does not become a megabyte of log line.
func quoteText(s string) string {
	const limit = 64
	if len(s) <= limit {
		return strconv.Quote(s)
	}
	cut := limit
	for cut > 0 && !utf8.RuneStart(s[cut]) {
		cut--
	}
	return strconv.Quote(s[:cut]) + "…"
}

func typeString(t reflect.Type) string {
	if t == nil {
		return "<nil>"
	}
	return t.String()
}

func typeName(t reflect.Type) string {
	if t == nil {
		return "T"
	}
	if t.Name() != "" {
		return t.Name()
	}
	return t.String()
}
