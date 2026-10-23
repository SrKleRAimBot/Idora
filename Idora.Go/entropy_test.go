package idora

import (
	"bytes"
	"testing"
)

func TestEntropyBufferZeroValueFillsOnFirstUse(t *testing.T) {
	var e entropyBuffer
	b := e.take(10)
	if len(b) != 10 {
		t.Fatalf("take(10) returned %d bytes", len(b))
	}
	if e.remaining != entropyBufferSize-10 {
		t.Errorf("remaining = %d, want %d", e.remaining, entropyBufferSize-10)
	}
	if bytes.Equal(e.buf[:], make([]byte, entropyBufferSize)) {
		t.Errorf("buffer is still all zero after first use")
	}
}

func TestEntropyBufferHandsOutConsecutiveSlices(t *testing.T) {
	var e entropyBuffer
	a := bytes.Clone(e.take(10))
	b := bytes.Clone(e.take(10))
	if !bytes.Equal(a, e.buf[0:10]) || !bytes.Equal(b, e.buf[10:20]) {
		t.Errorf("take did not hand out consecutive slices of the block")
	}
	if bytes.Equal(a, b) {
		t.Errorf("successive takes returned the same bytes %x", a)
	}
}

func TestEntropyBufferSliceCannotGrowIntoTheNextDraw(t *testing.T) {
	var e entropyBuffer
	if b := e.take(10); cap(b) != 10 {
		t.Errorf("cap = %d, want 10", cap(b))
	}
}

func TestEntropyBufferRefillsWhenTooFewBytesRemain(t *testing.T) {
	var e entropyBuffer
	e.take(entropyBufferSize - 5)
	before := e.buf
	b := e.take(10)
	if e.remaining != entropyBufferSize-10 {
		t.Errorf("remaining = %d after a refill, want %d", e.remaining, entropyBufferSize-10)
	}
	if !bytes.Equal(b, e.buf[:10]) {
		t.Errorf("draw after a refill did not start at the beginning of the block")
	}
	if before == e.buf {
		t.Errorf("refill did not replace the block")
	}
}

func TestEntropyBufferExactFitDoesNotRefill(t *testing.T) {
	var e entropyBuffer
	e.take(entropyBufferSize - 10)
	before := e.buf
	e.take(10)
	if e.remaining != 0 || before != e.buf {
		t.Errorf("a draw that exactly fits refilled the block")
	}
}

func TestEntropyBufferNeverRepeatsABlockAcrossManyRefills(t *testing.T) {
	var e entropyBuffer
	seen := map[[16]byte]bool{}
	for range 64 * entropyBufferSize / 16 {
		var k [16]byte
		copy(k[:], e.take(16))
		if seen[k] {
			t.Fatalf("16-byte draw %x repeated", k)
		}
		seen[k] = true
	}
}
