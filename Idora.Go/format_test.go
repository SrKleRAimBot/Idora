package idora_test

import (
	"encoding/hex"
	"regexp"
	"strings"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

var (
	ulidID   = regexp.MustCompile(`^usr_[0-7][0-9a-hjkmnp-tv-z]{25}$`)
	uuidV7ID = regexp.MustCompile(`^usr_[0-9a-f]{12}7[0-9a-f]{3}[89ab][0-9a-f]{15}$`)
)

func TestULIDFormat(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	for range 1000 {
		if id := idora.MustGenerate[User](gen); !ulidID.MatchString(id) {
			t.Fatalf("%q is not usr_ followed by 26 Crockford Base32 characters", id)
		}
	}
}

func TestUUIDv7Format(t *testing.T) {
	gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[User]("usr"))
	for range 1000 {
		id := idora.MustGenerate[User](gen)
		if !uuidV7ID.MatchString(id) {
			t.Fatalf("%q is not usr_ followed by a hyphen-free UUIDv7", id)
		}
		// It parses as a 16-octet UUID with version 7 and the RFC 9562 variant.
		raw, err := hex.DecodeString(id[4:])
		if err != nil || len(raw) != 16 || raw[6]>>4 != 7 || raw[8]>>6 != 0b10 {
			t.Fatalf("%q does not decode to a version 7, variant 10 UUID", id)
		}
	}
}

func TestBothStrategiesProduceLowercaseASCIIWithExactlyOneSeparator(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s))
		for range 1000 {
			id, _ := gen.GenerateWithPrefix("x")
			value := id[2:]
			if strings.Count(id, "_") != 1 || value != strings.ToLower(value) {
				t.Fatalf("%v: %q", s, id)
			}
			for i := range len(id) {
				if id[i] >= 0x80 {
					t.Fatalf("%v: %q is not ASCII", s, id)
				}
			}
		}
	}
}

func TestULIDEncodesTheCurrentTime(t *testing.T) {
	gen := idora.MustNew()
	before := time.Now().UnixMilli()
	id, _ := gen.GenerateWithPrefix("x")
	after := time.Now().UnixMilli()
	ms, _, _, err := idora.DecodeULID(id[2:])
	if err != nil {
		t.Fatal(err)
	}
	if int64(ms) < before || int64(ms) > after {
		t.Errorf("timestamp %d outside [%d, %d]", ms, before, after)
	}
}

func TestULIDVariesTheRandomFieldWithinAMillisecond(t *testing.T) {
	gen := idora.MustNew()
	a, _ := gen.GenerateWithPrefix("x")
	b, _ := gen.GenerateWithPrefix("x")
	if a[2:12] == b[2:12] && a[12:] == b[12:] {
		t.Errorf("%s and %s are identical", a, b)
	}
}

func TestIdentifierSplitsOnTheFirstSeparator(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("Usr1"))
	prefix, value, _ := strings.Cut(idora.MustGenerate[User](gen), "_")
	if prefix != "Usr1" || len(value) != 26 {
		t.Errorf("split into %q and %q", prefix, value)
	}
}

func TestIdentifierLengthIsPrefixPlusSeparatorPlusValue(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s))
		for _, p := range []string{"a", "usr", "abcdefghijklmnop"} {
			id, _ := gen.GenerateWithPrefix(p)
			want := len(p) + 1 + map[idora.Strategy]int{idora.ULID: 26, idora.UUIDv7: 32}[s]
			if len(id) != want {
				t.Errorf("%v %q: length %d, want %d", s, p, len(id), want)
			}
		}
	}
}
