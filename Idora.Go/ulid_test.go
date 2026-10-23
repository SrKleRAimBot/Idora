package idora

import (
	"math"
	"strings"
	"testing"
)

func encodeULIDString(ms uint64, hi uint16, lo uint64) string {
	var b [ulidLen]byte
	encodeULID(b[:], ms, hi, lo)
	return string(b[:])
}

func TestEncodeULID(t *testing.T) {
	tests := []struct {
		name string
		ms   uint64
		hi   uint16
		lo   uint64
		want string
	}{
		{"everything zero", 0, 0, 0, "00000000000000000000000000"},
		{"every bit set", 1<<48 - 1, math.MaxUint16, math.MaxUint64, "7zzzzzzzzzzzzzzzzzzzzzzzzz"},
		{"timestamp 1", 1, 0, 0, "00000000010000000000000000"},
		{"timestamp 31", 31, 0, 0, "000000000z0000000000000000"},
		{"timestamp 32", 32, 0, 0, "00000000100000000000000000"},
		{"timestamp 33", 33, 0, 0, "00000000110000000000000000"},
		{"random 1", 0, 0, 1, "00000000000000000000000001"},
		{"random 31", 0, 0, 31, "0000000000000000000000000z"},
		{"random 32", 0, 0, 32, "00000000000000000000000010"},
		// The high word's top five bits are the first character after the timestamp.
		{"high word top bits", 0, 0xf800, 0, "0000000000z000000000000000"},
		// Character 13 takes the low bit of the high word and the top four of the low word.
		{"straddling character", 0, 1, 0, "0000000000000g000000000000"},
		{"straddling character low half", 0, 0, 0xf << 60, "0000000000000f000000000000"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := encodeULIDString(tt.ms, tt.hi, tt.lo); got != tt.want {
				t.Errorf("encodeULID = %s, want %s", got, tt.want)
			}
		})
	}
}

func TestEncodeULIDRoundTripsThroughAnIndependentDecoder(t *testing.T) {
	var e entropyBuffer
	for range 10_000 {
		r := e.take(16)
		ms := (uint64(r[0])<<40 | uint64(r[1])<<32 | uint64(r[2])<<24 | uint64(r[3])<<16 |
			uint64(r[4])<<8 | uint64(r[5]))
		hi := uint16(r[6])<<8 | uint16(r[7])
		lo := uint64(r[8])<<56 | uint64(r[9])<<48 | uint64(r[10])<<40 | uint64(r[11])<<32 |
			uint64(r[12])<<24 | uint64(r[13])<<16 | uint64(r[14])<<8 | uint64(r[15])
		s := encodeULIDString(ms, hi, lo)
		gotMs, gotHi, gotLo, err := DecodeULID(s)
		if err != nil {
			t.Fatal(err)
		}
		if gotMs != ms || gotHi != hi || gotLo != lo {
			t.Fatalf("%s decoded to (%d, %x, %x), want (%d, %x, %x)", s, gotMs, gotHi, gotLo, ms, hi, lo)
		}
		if strings.ContainsAny(s, "ilou") {
			t.Fatalf("%s contains a letter Crockford Base32 excludes", s)
		}
	}
}

func TestEncodeULIDWritesOnlyTwentySixCharacters(t *testing.T) {
	buf := []byte(strings.Repeat("#", 40))
	encodeULID(buf[5:], 1<<48-1, math.MaxUint16, math.MaxUint64)
	if got := string(buf[:5]) + string(buf[5+ulidLen:]); got != strings.Repeat("#", 40-ulidLen) {
		t.Errorf("encodeULID wrote outside its 26 characters: %q", buf)
	}
}

func TestStepULID(t *testing.T) {
	const ms = 1_754_568_000_000
	tests := []struct {
		name       string
		prev       ulidState
		now        uint64
		want       ulidState
		wantReseed bool
	}{
		{"first use stamps the supplied timestamp", ulidState{}, ms, ulidState{ms: ms}, true},
		{"a new millisecond draws fresh randomness", ulidState{ms, 7, 9}, ms + 1, ulidState{ms: ms + 1}, true},
		{"the same millisecond increments by exactly one", ulidState{ms, 7, 9}, ms, ulidState{ms, 7, 10}, false},
		{"a backwards clock keeps the last timestamp", ulidState{ms + 5, 0, 10}, ms, ulidState{ms + 5, 0, 11}, false},
		{"the low word carries into the high word", ulidState{ms, 0, math.MaxUint64}, ms, ulidState{ms, 1, 0}, false},
		{"an exhausted field borrows the next millisecond", ulidState{ms, math.MaxUint16, math.MaxUint64}, ms,
			ulidState{ms: ms + 1}, true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, reseed := stepULID(tt.prev, tt.now)
			if got != tt.want || reseed != tt.wantReseed {
				t.Errorf("stepULID(%+v, %d) = %+v, %v; want %+v, %v", tt.prev, tt.now, got, reseed, tt.want, tt.wantReseed)
			}
		})
	}
}

func TestNextULIDWithAFrozenClockIsStrictlyIncreasing(t *testing.T) {
	var g Generator
	const ms = 1_754_568_000_000
	prev := encodeULIDString(0, 0, 0)
	for range 100_000 {
		s := g.nextULID(ms)
		cur := encodeULIDString(s.ms, s.hi, s.lo)
		if cur <= prev {
			t.Fatalf("%s did not sort after %s", cur, prev)
		}
		prev = cur
	}
}

func TestNextULIDWithABackwardsClockNeverRegresses(t *testing.T) {
	var g Generator
	first := g.nextULID(1_000_000)
	second := g.nextULID(999_000)
	if second.ms != first.ms {
		t.Errorf("timestamp regressed from %d to %d", first.ms, second.ms)
	}
	a := encodeULIDString(first.ms, first.hi, first.lo)
	b := encodeULIDString(second.ms, second.hi, second.lo)
	if b <= a {
		t.Errorf("%s did not sort after %s", b, a)
	}
}

func TestNextULIDVariesTheRandomFieldAcrossMilliseconds(t *testing.T) {
	var g Generator
	seen := map[[2]uint64]bool{}
	for i := range uint64(1000) {
		s := g.nextULID(1_000_000 + i)
		key := [2]uint64{uint64(s.hi), s.lo}
		if seen[key] {
			t.Fatalf("random field %x repeated after a reseed", key)
		}
		seen[key] = true
	}
}
