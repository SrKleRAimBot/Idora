package idora

import "math"

const (
	// ulidLen is the length of an encoded ULID.
	ulidLen = 26

	// crockford is the lowercase Crockford Base32 alphabet: no i, l, o or u.
	crockford = "0123456789abcdefghjkmnpqrstvwxyz"
)

// ulidState is one ULID: a 48-bit millisecond timestamp and an 80-bit random field split into
// its high 16 bits and low 64 bits. A Generator keeps the last one it issued.
type ulidState struct {
	ms uint64
	hi uint16
	lo uint64
}

// stepULID is the monotonic algorithm of spec §3.1, kept pure so it can be tested without a
// clock or an entropy source. Given the previous state and the clock reading now it returns
// the next state; when reseed is true the caller must fill next.hi and next.lo with fresh
// randomness.
func stepULID(prev ulidState, now uint64) (next ulidState, reseed bool) {
	if now > prev.ms {
		return ulidState{ms: now}, true
	}
	// The same millisecond, or a clock that stepped backwards: keep the timestamp and count.
	if prev.lo != math.MaxUint64 {
		prev.lo++
		return prev, false
	}
	if prev.hi != math.MaxUint16 {
		prev.hi++
		prev.lo = 0
		return prev, false
	}
	// 2^80 values in one millisecond. Unreachable in practice, but borrowing the next
	// millisecond keeps the sequence increasing instead of wrapping.
	return ulidState{ms: prev.ms + 1}, true
}

// encodeULID writes the 26-character encoding of a ULID into dst, which must have room for it.
// The 128 bits are one big-endian number right-aligned in 130 bits, so the first character
// carries only the top three bits of the timestamp.
func encodeULID(dst []byte, ms uint64, hi uint16, lo uint64) {
	dst = dst[:ulidLen] // one bounds check for the whole function
	h := uint64(hi)

	dst[0] = crockford[(ms>>45)&0x1f]
	dst[1] = crockford[(ms>>40)&0x1f]
	dst[2] = crockford[(ms>>35)&0x1f]
	dst[3] = crockford[(ms>>30)&0x1f]
	dst[4] = crockford[(ms>>25)&0x1f]
	dst[5] = crockford[(ms>>20)&0x1f]
	dst[6] = crockford[(ms>>15)&0x1f]
	dst[7] = crockford[(ms>>10)&0x1f]
	dst[8] = crockford[(ms>>5)&0x1f]
	dst[9] = crockford[ms&0x1f]

	// Character 13 straddles the two words: one bit from hi, four from lo.
	dst[10] = crockford[(h>>11)&0x1f]
	dst[11] = crockford[(h>>6)&0x1f]
	dst[12] = crockford[(h>>1)&0x1f]
	dst[13] = crockford[((h&0x1)<<4)|(lo>>60)]
	dst[14] = crockford[(lo>>55)&0x1f]
	dst[15] = crockford[(lo>>50)&0x1f]
	dst[16] = crockford[(lo>>45)&0x1f]
	dst[17] = crockford[(lo>>40)&0x1f]
	dst[18] = crockford[(lo>>35)&0x1f]
	dst[19] = crockford[(lo>>30)&0x1f]
	dst[20] = crockford[(lo>>25)&0x1f]
	dst[21] = crockford[(lo>>20)&0x1f]
	dst[22] = crockford[(lo>>15)&0x1f]
	dst[23] = crockford[(lo>>10)&0x1f]
	dst[24] = crockford[(lo>>5)&0x1f]
	dst[25] = crockford[lo&0x1f]
}
