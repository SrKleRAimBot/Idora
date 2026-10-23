package idora_test

import (
	"runtime"
	"slices"
	"sync"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

func TestULIDIsStrictlyIncreasingOnOneGoroutine(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	prev := idora.MustGenerate[User](gen)
	for range 100_000 {
		cur := idora.MustGenerate[User](gen)
		if cur <= prev {
			t.Fatalf("%s did not sort after %s", cur, prev)
		}
		prev = cur
	}
}

func TestBothStrategiesIncreaseAcrossMillisecondBoundaries(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s))
		var ids []string
		for range 5 {
			id, _ := gen.GenerateWithPrefix("x")
			ids = append(ids, id)
			time.Sleep(2 * time.Millisecond)
		}
		if !slices.IsSorted(ids) || len(slices.Compact(slices.Clone(ids))) != len(ids) {
			t.Errorf("%v: %v is not strictly increasing", s, ids)
		}
	}
}

func TestSortingGroupsByPrefixThenTime(t *testing.T) {
	for _, s := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(idora.WithStrategy(s), idora.WithEntity[User]("usr"), idora.WithEntity[Order]("ord"))
		u1 := idora.MustGenerate[User](gen)
		o1 := idora.MustGenerate[Order](gen)
		time.Sleep(2 * time.Millisecond)
		u2 := idora.MustGenerate[User](gen)
		o2 := idora.MustGenerate[Order](gen)
		got := []string{u2, o2, u1, o1}
		slices.Sort(got)
		if want := []string{o1, o2, u1, u2}; !slices.Equal(got, want) {
			t.Errorf("%v: sorted %v, want %v", s, got, want)
		}
	}
}

func TestULIDTimestampIsNonDecreasing(t *testing.T) {
	gen := idora.MustNew()
	var prev uint64
	for range 10_000 {
		id, _ := gen.GenerateWithPrefix("x")
		ms, _, _, err := idora.DecodeULID(id[2:])
		if err != nil {
			t.Fatal(err)
		}
		if ms < prev {
			t.Fatalf("timestamp went from %d back to %d", prev, ms)
		}
		prev = ms
	}
}

// Each goroutine's own sequence increases, as it would with per-thread state.
func TestULIDIncreasesPerGoroutineUnderConcurrency(t *testing.T) {
	gen := idora.MustNew()
	workers := max(4, runtime.GOMAXPROCS(0))
	var wg sync.WaitGroup
	errs := make(chan string, workers)
	for range workers {
		wg.Add(1)
		go func() {
			defer wg.Done()
			prev := ""
			for range 10_000 {
				id, _ := gen.GenerateWithPrefix("x")
				if id <= prev {
					errs <- id + " did not sort after " + prev
					return
				}
				prev = id
			}
		}()
	}
	wg.Wait()
	close(errs)
	for e := range errs {
		t.Error(e)
	}
}

// The per-Generator critical section gives a stronger guarantee than per-thread state: when one
// call finishes before another starts, on any goroutines, the later identifier sorts after.
// Serialising the goroutines through a lock and recording in that order checks exactly that.
func TestULIDIncreasesAcrossGoroutinesSharingAGenerator(t *testing.T) {
	gen := idora.MustNew()
	workers := max(4, runtime.GOMAXPROCS(0))
	var (
		mu  sync.Mutex
		ids []string
		wg  sync.WaitGroup
	)
	for range workers {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for range 5_000 {
				mu.Lock()
				id, _ := gen.GenerateWithPrefix("x")
				ids = append(ids, id)
				mu.Unlock()
			}
		}()
	}
	wg.Wait()
	for i := 1; i < len(ids); i++ {
		if ids[i] <= ids[i-1] {
			t.Fatalf("ids[%d] = %s did not sort after %s", i, ids[i], ids[i-1])
		}
	}
}
