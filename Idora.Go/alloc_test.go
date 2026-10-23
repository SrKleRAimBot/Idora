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
