package idora_test

import (
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

// For UUIDv7, 10,000 runs draw roughly 100 KB of entropy, so the 1 KiB buffer is refilled about
// a hundred times during the measurement: refilling must not allocate either.
const allocRuns = 10_000

func TestGenerationAllocatesOnlyTheReturnedString(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
		cases := map[string]func(){
			"registered": func() { _, _ = idora.Generate[User](gen) },
			"pointer":    func() { _, _ = idora.Generate[*User](gen) },
			"ad-hoc":     func() { _, _ = gen.GenerateWithPrefix("tmp") },
		}
		for name, fn := range cases {
			if n := testing.AllocsPerRun(allocRuns, fn); n != 1 {
				t.Errorf("%v %s: %v allocations per identifier, want 1", s, name, n)
			}
		}
	}
}

func TestLookupsDoNotAllocate(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	if n := testing.AllocsPerRun(1000, func() { _, _ = idora.PrefixOf[User](gen) }); n != 0 {
		t.Errorf("PrefixOf allocated %v times", n)
	}
}

func TestGenerateIDAllocatesOnlyTheText(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
		if n := testing.AllocsPerRun(allocRuns, func() { _, _ = idora.GenerateID[User](gen) }); n != 1 {
			t.Errorf("%v: GenerateID allocated %v times, want 1", s, n)
		}
	}
}

// Parsing keeps the input string, so reading and comparing identifiers allocates nothing.
func TestParsingAndComparingDoNotAllocate(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	ulid, uuid := "usr_01k226e0g0008j4ct4ank7f24s", "usr_0198846702007011a233445566778899"
	a, b := idora.MustParse(ulid), idora.MustParse(uuid)
	cases := map[string]func(){
		"Parse ULID":      func() { _, _ = idora.Parse(ulid) },
		"Parse UUIDv7":    func() { _, _ = idora.Parse(uuid) },
		"ParsePrefixed":   func() { _, _ = idora.ParsePrefixed(ulid, "usr") },
		"ParseFor":        func() { _, _ = idora.ParseFor[User](gen, ulid) },
		"IsValid":         func() { _ = idora.IsValid(ulid) },
		"IsValidPrefixed": func() { _ = idora.IsValidPrefixed("bad", "usr") },
		"IsValidFor":      func() { _ = idora.IsValidFor[User](gen, uuid) },
		"Compare":         func() { _ = a.Compare(b) },
		"Time":            func() { _ = a.Time() },
	}
	for name, fn := range cases {
		if n := testing.AllocsPerRun(1000, fn); n != 0 {
			t.Errorf("%s allocated %v times", name, n)
		}
	}
}
