package idora

import (
	"encoding/binary"
	"iter"
	"reflect"
	"sync"
	"time"
)

// Generator produces identifiers. Build one with [New] at start-up and share it: its registry
// is immutable once built, and every method is safe for concurrent use by any number of
// goroutines.
//
// The zero value is a usable ULID generator with no registered entity types, suitable for
// [Generator.GenerateWithPrefix]. A Generator must not be copied after first use.
//
// Each Generator owns one ULID stream. Its last value and its 1 KiB block of crypto/rand entropy
// sit behind a short, per-Generator critical section; there is no process-wide lock. Every ULID a
// Generator returns therefore sorts strictly after every ULID it returned before, whichever
// goroutines made the calls. Separate Generators are separate streams, ordered relative to each
// other only by millisecond.
//
// UUIDv7 values have no ordering state within a millisecond, so UUIDv7 generation takes no lock
// at all: it draws from 1 KiB entropy blocks held per P (logical processor) in a sync.Pool, and
// scales with cores.
type Generator struct {
	strategy Strategy
	prefixes map[reflect.Type]string
	order    []registration

	mu      sync.Mutex
	last    ulidState
	entropy entropyBuffer
}

// New builds a Generator from the options, applied in order. The first option that fails stops
// construction, and its error is returned with a nil Generator. With no options it produces
// ULIDs and has no registered entity types.
func New(opts ...Option) (*Generator, error) {
	var b builder
	for _, opt := range opts {
		if opt.apply == nil {
			continue
		}
		if err := opt.apply(&b); err != nil {
			return nil, err
		}
	}
	return &Generator{strategy: b.strategy, prefixes: b.prefixes, order: b.order}, nil
}

// MustNew is like [New] but panics if an option fails. It suits package-level variables and
// tests, where a registration mistake is a programming error.
func MustNew(opts ...Option) *Generator {
	g, err := New(opts...)
	if err != nil {
		panic(err)
	}
	return g
}

// Strategy reports the value format the Generator produces.
func (g *Generator) Strategy() Strategy { return g.strategy }

// Generate returns a new identifier for entity type T, "<prefix>_<value>", using the prefix T
// was registered with. A pointer type uses its element type's prefix.
//
// It returns a [*PrefixNotRegisteredError] if T has no prefix; it never invents one from the
// type's name. This is the preferred form: the prefix lives in one place and was validated once,
// when the Generator was built.
//
// Generate is a function rather than a method because Go methods cannot have type parameters.
func Generate[T any](g *Generator) (string, error) {
	return g.GenerateFor(reflect.TypeFor[T]())
}

// MustGenerate is like [Generate] but panics if T has no registered prefix. It suits
// initialisation code and tests.
func MustGenerate[T any](g *Generator) string {
	id, err := g.GenerateFor(reflect.TypeFor[T]())
	if err != nil {
		panic(err)
	}
	return id
}

// GenerateFor is [Generate] for an entity type known only at run time.
func (g *Generator) GenerateFor(t reflect.Type) (string, error) {
	prefix, ok := g.prefixes[entityKey(t)]
	if !ok {
		return "", &PrefixNotRegisteredError{EntityType: entityKey(t)}
	}
	return g.compose(prefix), nil
}

// GenerateWithPrefix returns a new identifier with an ad-hoc prefix, for prefixes known only at
// run time. The prefix is validated on every call and emitted exactly as given; an invalid one
// returns an [*InvalidPrefixError].
//
// Prefer [Generate] where the entity type is known: it keeps the prefix in one place.
func (g *Generator) GenerateWithPrefix(prefix string) (string, error) {
	if err := validatePrefix(prefix); err != nil {
		return "", err
	}
	return g.compose(prefix), nil
}

// PrefixOf returns the prefix registered for entity type T, without the trailing separator, or
// a [*PrefixNotRegisteredError].
func PrefixOf[T any](g *Generator) (string, error) {
	return g.Prefix(reflect.TypeFor[T]())
}

// Prefix is [PrefixOf] for an entity type known only at run time.
func (g *Generator) Prefix(t reflect.Type) (string, error) {
	t = entityKey(t)
	if prefix, ok := g.prefixes[t]; ok {
		return prefix, nil
	}
	return "", &PrefixNotRegisteredError{EntityType: t}
}

// LookupPrefix returns the prefix registered for t, without the trailing separator, and whether
// there was one. Pointer types are reduced to their element type; a nil type is never
// registered.
func (g *Generator) LookupPrefix(t reflect.Type) (prefix string, ok bool) {
	prefix, ok = g.prefixes[entityKey(t)]
	return prefix, ok
}

// Registrations yields every registered entity type and its prefix, in the order they were
// registered (option order; within one [WithEntities], by fully qualified type name). The
// registry is immutable, so the sequence is the same every time it is iterated. Collect it
// with maps.Collect when a map is more convenient.
func (g *Generator) Registrations() iter.Seq2[reflect.Type, string] {
	return func(yield func(reflect.Type, string) bool) {
		for _, r := range g.order {
			if !yield(r.entityType, r.prefix) {
				return
			}
		}
	}
}

// compose builds "<prefix>_<value>" in a stack buffer and converts it to a string once, so the
// returned string is the only allocation.
func (g *Generator) compose(prefix string) string {
	var buf [maxIDLen]byte
	n := copy(buf[:], prefix)
	buf[n] = Separator
	n++
	now := nowMillis()
	if g.strategy == UUIDv7 {
		var random [uuidV7RandomLen]byte
		g.drawUUIDv7(&random)
		encodeUUIDv7(buf[n:], now, &random)
		n += uuidV7Len
	} else {
		s := g.nextULID(now)
		encodeULID(buf[n:], s.ms, s.hi, s.lo)
		n += ulidLen
	}
	return string(buf[:n])
}

// nextULID advances the Generator's ULID stream. The clock is read by the caller, outside the
// critical section; a reading that loses the race to a later one is simply treated like a clock
// that stepped backwards, which keeps the stream increasing.
func (g *Generator) nextULID(now uint64) ulidState {
	g.mu.Lock()
	defer g.mu.Unlock()
	next, reseed := stepULID(g.last, now)
	if reseed {
		r := g.entropy.take(10)
		next.hi = binary.BigEndian.Uint16(r)
		next.lo = binary.BigEndian.Uint64(r[2:])
	}
	g.last = next
	return next
}

// drawUUIDv7 copies the random octets of a UUIDv7 out of a pooled entropy buffer.
func (g *Generator) drawUUIDv7(dst *[uuidV7RandomLen]byte) {
	e := entropyPool.Get().(*entropyBuffer)
	copy(dst[:], e.take(uuidV7RandomLen))
	entropyPool.Put(e)
}

// nowMillis reads the wall clock as milliseconds since the Unix epoch. UnixMilli is defined in
// UTC and independent of the time zone; a clock reading before 1970 is clamped to zero, since
// both formats store an unsigned timestamp.
func nowMillis() uint64 {
	ms := time.Now().UnixMilli()
	if ms < 0 {
		return 0
	}
	return uint64(ms)
}
