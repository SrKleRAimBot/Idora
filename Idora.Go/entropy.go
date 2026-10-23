package idora

import (
	"crypto/rand"
	"sync"
)

// entropyBufferSize is the block of operating-system entropy drawn at a time (spec §3.3).
const entropyBufferSize = 1024

// entropyBuffer hands out slices of a block of crypto/rand output, refilling the block when it
// runs low. Asking the operating system for ten bytes per identifier would cost more than
// everything else put together; buffering changes the cost, not the quality.
//
// The zero value is ready to use: it fills itself on first use. It is not safe for concurrent
// use; the Generator's mutex guards it.
type entropyBuffer struct {
	buf       [entropyBufferSize]byte
	remaining int
}

// take returns the next n unused bytes, n <= entropyBufferSize. The slice aliases the buffer
// and is valid only until the next call.
func (e *entropyBuffer) take(n int) []byte {
	if e.remaining < n {
		e.refill()
	}
	start := entropyBufferSize - e.remaining
	e.remaining -= n
	return e.buf[start : start+n : start+n]
}

func (e *entropyBuffer) refill() {
	// From Go 1.24 crypto/rand.Read never returns an error (it crashes the program instead);
	// on earlier releases an error means the operating system cannot supply entropy, and
	// handing out predictable identifiers would be worse than stopping.
	if _, err := rand.Read(e.buf[:]); err != nil {
		panic("idora: reading from crypto/rand failed: " + err.Error())
	}
	e.remaining = entropyBufferSize
}

// entropyPool holds entropy buffers for UUIDv7 generation, which has no ordering state to
// protect and so needs no lock: sync.Pool keeps a buffer per P, which is as close to
// thread-local storage as Go offers.
var entropyPool = sync.Pool{New: func() any { return new(entropyBuffer) }}
