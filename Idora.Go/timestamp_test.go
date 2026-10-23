package idora_test

import (
	"encoding/binary"
	"encoding/hex"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

// withNonUTCLocalZone makes sure time.Local is a zone far from UTC for the duration of the test.
// Running the suite with TZ=Australia/Sydney exercises a real zone database entry; without it,
// a fixed +10:00 zone stands in.
func withNonUTCLocalZone(t *testing.T) {
	t.Helper()
	if _, offset := time.Now().Zone(); offset != 0 {
		return
	}
	saved := time.Local
	time.Local = time.FixedZone("AEST", 10*60*60)
	t.Cleanup(func() { time.Local = saved })
}

// localWallClockAsUTC is what a buggy implementation that read the local wall clock and treated
// it as UTC would encode.
func localWallClockAsUTC(now time.Time) int64 {
	l := now.Local()
	return time.Date(l.Year(), l.Month(), l.Day(), l.Hour(), l.Minute(), l.Second(), l.Nanosecond(), time.UTC).UnixMilli()
}

func assertUTCTimestamp(t *testing.T, decode func(string) int64, gen *idora.Generator) {
	t.Helper()
	withNonUTCLocalZone(t)
	before := time.Now().UTC()
	id, err := gen.GenerateWithPrefix("x")
	if err != nil {
		t.Fatal(err)
	}
	after := time.Now().UTC()
	ms := decode(id[2:])

	if ms < before.UnixMilli() || ms > after.UnixMilli() {
		t.Errorf("timestamp %d is outside the UTC window [%d, %d]", ms, before.UnixMilli(), after.UnixMilli())
	}
	local := localWallClockAsUTC(before)
	if _, offset := before.Local().Zone(); offset == 0 {
		t.Fatal("local zone is UTC; the test cannot tell UTC from local time")
	}
	if diff := ms - local; diff > -60_000 && diff < 60_000 {
		t.Errorf("timestamp %d matches the local wall clock %d; it must be UTC", ms, local)
	}
}

func TestULIDEncodesUTCRegardlessOfTheLocalZone(t *testing.T) {
	assertUTCTimestamp(t, func(v string) int64 {
		ms, _, _, err := idora.DecodeULID(v)
		if err != nil {
			t.Fatal(err)
		}
		return int64(ms)
	}, idora.MustNew())
}

func TestUUIDv7EncodesUTCRegardlessOfTheLocalZone(t *testing.T) {
	assertUTCTimestamp(t, func(v string) int64 {
		raw, err := hex.DecodeString(v)
		if err != nil {
			t.Fatal(err)
		}
		var b [8]byte
		copy(b[2:], raw[:6])
		return int64(binary.BigEndian.Uint64(b[:]))
	}, idora.MustNew(idora.WithUUIDv7()))
}

func TestIdentifiersOrderByInstantNotByZone(t *testing.T) {
	withNonUTCLocalZone(t)
	gen := idora.MustNew()
	first, _ := gen.GenerateWithPrefix("x")
	// Changing the local zone mid-stream must not move the timestamps.
	saved := time.Local
	time.Local = time.FixedZone("PDT", -7*60*60)
	time.Sleep(2 * time.Millisecond)
	second, _ := gen.GenerateWithPrefix("x")
	time.Local = saved
	a, _, _, _ := idora.DecodeULID(first[2:])
	b, _, _, _ := idora.DecodeULID(second[2:])
	if b < a || b-a > 60_000 {
		t.Errorf("timestamps %d then %d after a zone change; want a few milliseconds apart", a, b)
	}
}
