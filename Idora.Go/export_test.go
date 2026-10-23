package idora

import (
	"fmt"
	"strings"
)

// DecodeULID is an independent decoder used by the tests to read values back. It deliberately
// shares no code with encodeULID: it walks the alphabet with strings.IndexByte and accumulates
// 130 bits one character at a time.
func DecodeULID(s string) (ms uint64, hi uint16, lo uint64, err error) {
	if len(s) != ulidLen {
		return 0, 0, 0, fmt.Errorf("ulid %q has length %d, want %d", s, len(s), ulidLen)
	}
	// Accumulate into a 128-bit (high, low) pair, shifting left five bits per character.
	var high, low uint64
	for i := 0; i < len(s); i++ {
		v := strings.IndexByte(crockford, s[i])
		if v < 0 {
			return 0, 0, 0, fmt.Errorf("ulid %q has non-Crockford character %q at %d", s, s[i], i)
		}
		if i == 0 && v > 7 {
			return 0, 0, 0, fmt.Errorf("ulid %q overflows 128 bits", s)
		}
		high = high<<5 | low>>59
		low = low<<5 | uint64(v)
	}
	ms = high >> 16
	hi = uint16(high)
	return ms, hi, low, nil
}
