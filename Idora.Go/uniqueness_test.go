package idora_test

import (
	"runtime"
	"sync"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

func sequentialCount() int {
	if testing.Short() {
		return 100_000
	}
	return 1_000_000
}

func TestOneMillionInSequenceHaveNoDuplicates(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		t.Run(s.String(), func(t *testing.T) {
			gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
			n := sequentialCount()
			seen := make(map[string]struct{}, n)
			for range n {
				seen[idora.MustGenerate[User](gen)] = struct{}{}
			}
			if len(seen) != n {
				t.Errorf("%d duplicates in %d identifiers", n-len(seen), n)
			}
		})
	}
}

// generateConcurrently runs perWorker calls of fn on each of workers goroutines and returns
// every result.
func generateConcurrently(workers, perWorker int, fn func(worker int) string) []string {
	results := make([][]string, workers)
	var wg sync.WaitGroup
	for w := range workers {
		wg.Add(1)
		go func() {
			defer wg.Done()
			out := make([]string, perWorker)
			for i := range out {
				out[i] = fn(w)
			}
			results[w] = out
		}()
	}
	wg.Wait()
	var all []string
	for _, r := range results {
		all = append(all, r...)
	}
	return all
}

func assertUnique(t *testing.T, ids []string) {
	t.Helper()
	seen := make(map[string]struct{}, len(ids))
	for _, id := range ids {
		if _, dup := seen[id]; dup {
			t.Fatalf("duplicate identifier %s", id)
		}
		seen[id] = struct{}{}
	}
}

func TestEveryCoreAtOnceHasNoDuplicates(t *testing.T) {
	workers := max(8, 2*runtime.GOMAXPROCS(0))
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		t.Run(s.String(), func(t *testing.T) {
			gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"))
			ids := generateConcurrently(workers, 20_000, func(int) string { return idora.MustGenerate[User](gen) })
			assertUnique(t, ids)
			re := map[idora.Strategy]interface{ MatchString(string) bool }{idora.ULID: ulidID, idora.UUIDv7: uuidV7ID}[s]
			for _, id := range ids {
				if !re.MatchString(id) {
					t.Fatalf("malformed identifier %q under concurrency", id)
				}
			}
		})
	}
}

func TestSeveralGeneratorsOnManyGoroutinesHaveNoDuplicates(t *testing.T) {
	gens := []*idora.Generator{
		idora.MustNew(idora.WithEntity[User]("usr")),
		idora.MustNew(idora.WithEntity[User]("usr")),
		idora.MustNew(idora.WithEntity[User]("usr")),
	}
	ids := generateConcurrently(12, 10_000, func(w int) string { return idora.MustGenerate[User](gens[w%len(gens)]) })
	assertUnique(t, ids)
}

func TestAdHocConcurrentlyHasNoDuplicates(t *testing.T) {
	gen := idora.MustNew()
	ids := generateConcurrently(max(8, runtime.GOMAXPROCS(0)), 20_000, func(int) string {
		id, err := gen.GenerateWithPrefix("tmp")
		if err != nil {
			panic(err)
		}
		return id
	})
	assertUnique(t, ids)
}
