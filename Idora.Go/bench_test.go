package idora_test

import (
	"crypto/rand"
	"encoding/hex"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

var sink string

// newUUIDv4 is the baseline. The standard library has no UUID type, so this is the nearest fair
// stand-in for the language's built-in identifier: 16 bytes from crypto/rand with the version 4
// and variant bits set, hex-encoded without hyphens — what most hand-rolled Go UUIDs do.
func newUUIDv4() string {
	var b [16]byte
	_, _ = rand.Read(b[:])
	b[6] = 0x40 | (b[6] & 0x0f)
	b[8] = 0x80 | (b[8] & 0x3f)
	return hex.EncodeToString(b[:])
}

func benchGenerators() (ulid, uuid *idora.Generator) {
	return idora.MustNew(idora.WithEntity[User]("usr")),
		idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[User]("usr"))
}

func BenchmarkBaselineUUIDv4CryptoRandHex(b *testing.B) {
	b.ReportAllocs()
	for range b.N {
		sink = newUUIDv4()
	}
}

func BenchmarkULIDRegistered(b *testing.B) {
	gen, _ := benchGenerators()
	b.ReportAllocs()
	for range b.N {
		sink, _ = idora.Generate[User](gen)
	}
}

func BenchmarkULIDAdHoc(b *testing.B) {
	gen, _ := benchGenerators()
	b.ReportAllocs()
	for range b.N {
		sink, _ = gen.GenerateWithPrefix("usr")
	}
}

func BenchmarkUUIDv7Registered(b *testing.B) {
	_, gen := benchGenerators()
	b.ReportAllocs()
	for range b.N {
		sink, _ = idora.Generate[User](gen)
	}
}

func BenchmarkUUIDv7AdHoc(b *testing.B) {
	_, gen := benchGenerators()
	b.ReportAllocs()
	for range b.N {
		sink, _ = gen.GenerateWithPrefix("usr")
	}
}

// The parallel variants share one Generator across GOMAXPROCS goroutines, which is the
// contended case for its critical section.

func BenchmarkParallelBaselineUUIDv4CryptoRandHex(b *testing.B) {
	b.ReportAllocs()
	b.RunParallel(func(pb *testing.PB) {
		var s string
		for pb.Next() {
			s = newUUIDv4()
		}
		_ = s
	})
}

func BenchmarkParallelULIDRegistered(b *testing.B) {
	gen, _ := benchGenerators()
	b.ReportAllocs()
	b.RunParallel(func(pb *testing.PB) {
		var s string
		for pb.Next() {
			s, _ = idora.Generate[User](gen)
		}
		_ = s
	})
}

func BenchmarkParallelULIDAdHoc(b *testing.B) {
	gen, _ := benchGenerators()
	b.ReportAllocs()
	b.RunParallel(func(pb *testing.PB) {
		var s string
		for pb.Next() {
			s, _ = gen.GenerateWithPrefix("usr")
		}
		_ = s
	})
}

func BenchmarkParallelUUIDv7Registered(b *testing.B) {
	_, gen := benchGenerators()
	b.ReportAllocs()
	b.RunParallel(func(pb *testing.PB) {
		var s string
		for pb.Next() {
			s, _ = idora.Generate[User](gen)
		}
		_ = s
	})
}

func BenchmarkParallelUUIDv7AdHoc(b *testing.B) {
	_, gen := benchGenerators()
	b.ReportAllocs()
	b.RunParallel(func(pb *testing.PB) {
		var s string
		for pb.Next() {
			s, _ = gen.GenerateWithPrefix("usr")
		}
		_ = s
	})
}
