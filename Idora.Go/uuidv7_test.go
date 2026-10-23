package idora

import (
	"bytes"
	"encoding/hex"
	"slices"
	"testing"
)

func TestComposeUUIDv7(t *testing.T) {
	tests := []struct {
		name string
		ms   uint64
		rnd  [10]byte
		want string
	}{
		{"epoch gives a zero timestamp", 0, [10]byte{}, "00000000000070008000000000000000"},
		{"largest timestamp keeps the low 48 bits", 1<<48 - 1, [10]byte{}, "ffffffffffff70008000000000000000"},
		{"timestamp beyond 48 bits is truncated", 1 << 48, [10]byte{}, "00000000000070008000000000000000"},
		{"version and variant override the random bits",
			0x0102030405_06, [10]byte{0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff},
			"0102030405067fffbfffffffffffffff"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			u := composeUUIDv7(tt.ms, &tt.rnd)
			if got := hex.EncodeToString(u[:]); got != tt.want {
				t.Errorf("composeUUIDv7 = %s, want %s", got, tt.want)
			}
		})
	}
}

func TestEncodeUUIDv7SetsVersionAndVariantForAnyRandomness(t *testing.T) {
	var e entropyBuffer
	for range 10_000 {
		var rnd [uuidV7RandomLen]byte
		copy(rnd[:], e.take(uuidV7RandomLen))
		var out [uuidV7Len]byte
		encodeUUIDv7(out[:], 1_754_568_000_000, &rnd)
		raw, err := hex.DecodeString(string(out[:]))
		if err != nil {
			t.Fatal(err)
		}
		if raw[6]>>4 != 7 {
			t.Fatalf("%s: version nibble %x, want 7", out, raw[6]>>4)
		}
		if raw[8]>>6 != 0b10 {
			t.Fatalf("%s: variant bits %02b, want 10", out, raw[8]>>6)
		}
		// The bits that are not version or variant are the random input, untouched.
		if raw[6]&0x0f != rnd[0]&0x0f || raw[7] != rnd[1] || raw[8]&0x3f != rnd[2]&0x3f || !bytes.Equal(raw[9:], rnd[3:]) {
			t.Fatalf("%s does not carry the random octets %x", out, rnd)
		}
	}
}

func TestEncodeUUIDv7ForIncreasingTimestampsSortsInTimeOrder(t *testing.T) {
	var ids []string
	var rnd [uuidV7RandomLen]byte
	for i := range uint64(100) {
		rnd[0] = byte(100 - i) // randomness that runs the other way must not matter
		var out [uuidV7Len]byte
		encodeUUIDv7(out[:], 1_700_000_000_000+i, &rnd)
		ids = append(ids, string(out[:]))
	}
	if !slices.IsSorted(ids) {
		t.Errorf("UUIDv7 text does not sort in timestamp order")
	}
}
