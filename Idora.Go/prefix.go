package idora

import "unicode/utf8"

const (
	// Separator divides the prefix from the value in every identifier. A prefix can never
	// contain it, so splitting an identifier on its first Separator always recovers both parts.
	Separator = '_'

	// MaxPrefixLen is the longest prefix accepted, in characters.
	MaxPrefixLen = 16

	// maxIDLen is the longest identifier any strategy can produce.
	maxIDLen = MaxPrefixLen + 1 + uuidV7Len
)

// validatePrefix applies the rules of spec §2 in order and reports the first that fails. It does
// not allocate when the prefix is valid.
//
// Lengths and indexes count characters (Unicode code points), not bytes, so a prefix that is
// invalid because of a non-ASCII character reports the same index in every Idora package.
// A byte sequence that is not valid UTF-8 counts as one character per invalid byte.
func validatePrefix(prefix string) error {
	if len(prefix) == 0 {
		return &InvalidPrefixError{Prefix: prefix, Reason: ReasonEmpty, Index: -1}
	}

	// A string of at most MaxPrefixLen bytes holds at most MaxPrefixLen characters, so the rune
	// count is only needed on the failure path.
	if len(prefix) > MaxPrefixLen {
		if n := utf8.RuneCountInString(prefix); n > MaxPrefixLen {
			return &InvalidPrefixError{Prefix: prefix, Reason: ReasonTooLong, Index: -1, Length: n}
		}
	}

	for i := 0; i < len(prefix); i++ {
		if isASCIIAlnum(prefix[i]) {
			continue
		}
		r, _ := utf8.DecodeRuneInString(prefix[i:])
		reason := ReasonCharacter
		if r == Separator {
			reason = ReasonSeparator
		}
		return &InvalidPrefixError{
			Prefix: prefix,
			Reason: reason,
			Index:  utf8.RuneCountInString(prefix[:i]),
			Char:   r,
		}
	}
	return nil
}

// isASCIIAlnum is an explicit ASCII range check; Unicode-aware predicates would accept 'é'.
func isASCIIAlnum(c byte) bool {
	return ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z') || ('0' <= c && c <= '9')
}

// foldPrefix lower-cases the ASCII letters of a validated prefix, giving the key that makes
// "usr" and "USR" collide.
func foldPrefix(prefix string) string {
	for i := 0; i < len(prefix); i++ {
		if 'A' <= prefix[i] && prefix[i] <= 'Z' {
			b := []byte(prefix)
			for j := i; j < len(b); j++ {
				if 'A' <= b[j] && b[j] <= 'Z' {
					b[j] += 'a' - 'A'
				}
			}
			return string(b)
		}
	}
	return prefix
}
