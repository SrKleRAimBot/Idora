package idora

import (
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

// The shared vectors live outside this module, in the Idora repository's spec directory. A copy
// of the module fetched through the Go proxy does not include them, so the test skips there;
// inside the repository a missing file is a failure.
const vectorsPath = "../spec/test-vectors.json"

type vectors struct {
	ULID []struct {
		TimestampMs uint64 `json:"timestampMs"`
		RandomHex   string `json:"randomHex"`
		Expected    string `json:"expected"`
	} `json:"ulid"`
	UUIDv7 []struct {
		TimestampMs uint64 `json:"timestampMs"`
		RandomHex   string `json:"randomHex"`
		Expected    string `json:"expected"`
	} `json:"uuidv7"`
	ULIDMonotonic []struct {
		Description string `json:"description"`
		Previous    struct {
			TimestampMs uint64 `json:"timestampMs"`
			RandomHex   string `json:"randomHex"`
		} `json:"previous"`
		NowMs    uint64 `json:"nowMs"`
		Expected struct {
			TimestampMs uint64  `json:"timestampMs"`
			RandomHex   *string `json:"randomHex"`
			Encoded     string  `json:"encoded"`
		} `json:"expected"`
	} `json:"ulidMonotonic"`
	Prefixes struct {
		Valid   []string `json:"valid"`
		Invalid []struct {
			Prefix string `json:"prefix"`
			Reason string `json:"reason"`
			Index  *int   `json:"index"`
		} `json:"invalid"`
		CollidingPairs [][2]string `json:"collidingPairs"`
		DistinctPairs  [][2]string `json:"distinctPairs"`
	} `json:"prefixes"`
}

func loadVectors(t *testing.T) *vectors {
	t.Helper()
	data, err := os.ReadFile(vectorsPath)
	if errors.Is(err, os.ErrNotExist) {
		if _, specErr := os.Stat(filepath.Dir(vectorsPath)); specErr == nil {
			t.Fatalf("%s is missing from the spec directory", vectorsPath)
		}
		t.Skipf("%s not found; the conformance vectors run inside the Idora repository", vectorsPath)
	}
	if err != nil {
		t.Fatal(err)
	}
	var v vectors
	if err := json.Unmarshal(data, &v); err != nil {
		t.Fatalf("parsing %s: %v", vectorsPath, err)
	}
	if len(v.ULID) == 0 || len(v.UUIDv7) == 0 || len(v.ULIDMonotonic) == 0 || len(v.Prefixes.Valid) == 0 {
		t.Fatalf("%s is missing a section", vectorsPath)
	}
	return &v
}

// ulidRandom splits a 20-digit big-endian hex random field into its high 16 and low 64 bits.
func ulidRandom(t *testing.T, s string) (uint16, uint64) {
	t.Helper()
	b, err := hex.DecodeString(s)
	if err != nil || len(b) != 10 {
		t.Fatalf("bad randomHex %q", s)
	}
	return binary.BigEndian.Uint16(b), binary.BigEndian.Uint64(b[2:])
}

func ulidRandomHex(hi uint16, lo uint64) string {
	var b [10]byte
	binary.BigEndian.PutUint16(b[:], hi)
	binary.BigEndian.PutUint64(b[2:], lo)
	return hex.EncodeToString(b[:])
}

func TestConformanceULID(t *testing.T) {
	for _, v := range loadVectors(t).ULID {
		hi, lo := ulidRandom(t, v.RandomHex)
		var got [ulidLen]byte
		encodeULID(got[:], v.TimestampMs, hi, lo)
		if string(got[:]) != v.Expected {
			t.Errorf("encodeULID(%d, %s) = %s, want %s", v.TimestampMs, v.RandomHex, got, v.Expected)
		}
	}
}

func TestConformanceUUIDv7(t *testing.T) {
	for _, v := range loadVectors(t).UUIDv7 {
		var random [uuidV7RandomLen]byte
		if n, err := hex.Decode(random[:], []byte(v.RandomHex)); err != nil || n != uuidV7RandomLen {
			t.Fatalf("bad randomHex %q", v.RandomHex)
		}
		var got [uuidV7Len]byte
		encodeUUIDv7(got[:], v.TimestampMs, &random)
		if string(got[:]) != v.Expected {
			t.Errorf("encodeUUIDv7(%d, %s) = %s, want %s", v.TimestampMs, v.RandomHex, got, v.Expected)
		}
	}
}

func TestConformanceULIDMonotonic(t *testing.T) {
	for _, v := range loadVectors(t).ULIDMonotonic {
		t.Run(v.Description, func(t *testing.T) {
			hi, lo := ulidRandom(t, v.Previous.RandomHex)
			next, reseed := stepULID(ulidState{ms: v.Previous.TimestampMs, hi: hi, lo: lo}, v.NowMs)

			if next.ms != v.Expected.TimestampMs {
				t.Errorf("timestamp = %d, want %d", next.ms, v.Expected.TimestampMs)
			}
			if v.Expected.RandomHex == nil {
				if !reseed {
					t.Errorf("reseed = false, want a freshly drawn random field")
				}
				return
			}
			if reseed {
				t.Errorf("reseed = true, want an incremented random field")
			}
			if got := ulidRandomHex(next.hi, next.lo); got != *v.Expected.RandomHex {
				t.Errorf("random = %s, want %s", got, *v.Expected.RandomHex)
			}
			var enc [ulidLen]byte
			encodeULID(enc[:], next.ms, next.hi, next.lo)
			if string(enc[:]) != v.Expected.Encoded {
				t.Errorf("encoded = %s, want %s", enc, v.Expected.Encoded)
			}
		})
	}
}

func TestConformancePrefixesValid(t *testing.T) {
	for _, p := range loadVectors(t).Prefixes.Valid {
		if err := validatePrefix(p); err != nil {
			t.Errorf("validatePrefix(%q) = %v, want nil", p, err)
		}
	}
}

func TestConformancePrefixesInvalid(t *testing.T) {
	for _, v := range loadVectors(t).Prefixes.Invalid {
		err := validatePrefix(v.Prefix)
		var invalid *InvalidPrefixError
		if !errors.As(err, &invalid) {
			t.Errorf("validatePrefix(%q) = %v, want *InvalidPrefixError", v.Prefix, err)
			continue
		}
		if !errors.Is(err, ErrInvalidPrefix) {
			t.Errorf("validatePrefix(%q) does not match ErrInvalidPrefix", v.Prefix)
		}
		if invalid.Reason.String() != v.Reason {
			t.Errorf("validatePrefix(%q) reason = %s, want %s", v.Prefix, invalid.Reason, v.Reason)
		}
		wantIndex := -1
		if v.Index != nil {
			wantIndex = *v.Index
		}
		if invalid.Index != wantIndex {
			t.Errorf("validatePrefix(%q) index = %d, want %d", v.Prefix, invalid.Index, wantIndex)
		}
		if invalid.Prefix != v.Prefix {
			t.Errorf("validatePrefix(%q) carries prefix %q", v.Prefix, invalid.Prefix)
		}
	}
}

type vectorFirst struct{}
type vectorSecond struct{}

func TestConformancePrefixPairs(t *testing.T) {
	v := loadVectors(t)
	build := func(a, b string) error {
		_, err := New(WithEntity[vectorFirst](a), WithEntity[vectorSecond](b))
		return err
	}
	for _, pair := range v.Prefixes.CollidingPairs {
		err := build(pair[0], pair[1])
		var dup *DuplicatePrefixError
		if !errors.As(err, &dup) {
			t.Errorf("registering %q then %q: err = %v, want *DuplicatePrefixError", pair[0], pair[1], err)
			continue
		}
		if dup.Prefix != pair[1] || dup.ExistingPrefix != pair[0] ||
			dup.EntityType != reflect.TypeFor[vectorSecond]() ||
			dup.ConflictingEntityType != reflect.TypeFor[vectorFirst]() {
			t.Errorf("registering %q then %q: unexpected details %+v", pair[0], pair[1], dup)
		}
	}
	for _, pair := range v.Prefixes.DistinctPairs {
		if err := build(pair[0], pair[1]); err != nil {
			t.Errorf("registering %q then %q: err = %v, want nil", pair[0], pair[1], err)
		}
	}
}
