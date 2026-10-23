package idora_test

import (
	"errors"
	"strings"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

func newRegistry(t *testing.T) *idora.Generator {
	t.Helper()
	return idora.MustNew(
		idora.WithEntity[User]("usr"),
		idora.WithEntity[Order]("ord"),
		idora.WithEntities(Customer{}),
	)
}

func TestPrefixOfReturnsThePrefixWithoutTheSeparator(t *testing.T) {
	gen := newRegistry(t)
	p, err := idora.PrefixOf[User](gen)
	if err != nil || p != "usr" {
		t.Errorf("PrefixOf[User] = %q, %v", p, err)
	}
}

func TestPrefixByRuntimeTypeMatchesTheGenericForm(t *testing.T) {
	gen := newRegistry(t)
	a, _ := idora.PrefixOf[Order](gen)
	b, err := gen.Prefix(typeOf[Order]())
	if err != nil || a != b {
		t.Errorf("Prefix = %q, %v; PrefixOf = %q", b, err, a)
	}
}

func TestPrefixForAnUnregisteredType(t *testing.T) {
	gen := newRegistry(t)
	_, err := idora.PrefixOf[Comment](gen)
	var missing *idora.PrefixNotRegisteredError
	if !errors.As(err, &missing) || missing.EntityType != typeOf[Comment]() {
		t.Errorf("err = %v", err)
	}
	if _, err := gen.Prefix(nil); !errors.Is(err, idora.ErrPrefixNotRegistered) || !strings.Contains(err.Error(), "<nil>") {
		t.Errorf("Prefix(nil): err = %v", err)
	}
}

func TestLookupPrefix(t *testing.T) {
	gen := newRegistry(t)
	if p, ok := gen.LookupPrefix(typeOf[Customer]()); !ok || p != "cus" {
		t.Errorf("LookupPrefix(Customer) = %q, %v", p, ok)
	}
	if p, ok := gen.LookupPrefix(typeOf[Comment]()); ok || p != "" {
		t.Errorf("LookupPrefix(Comment) = %q, %v", p, ok)
	}
	if p, ok := gen.LookupPrefix(nil); ok || p != "" {
		t.Errorf("LookupPrefix(nil) = %q, %v", p, ok)
	}
}

func TestRegistrationsYieldsEverythingInRegistrationOrder(t *testing.T) {
	gen := newRegistry(t)
	var got []string
	for rt, p := range gen.Registrations() {
		got = append(got, rt.Name()+"="+p)
	}
	if s := strings.Join(got, ","); s != "User=usr,Order=ord,Customer=cus" {
		t.Errorf("registrations = %s", s)
	}
	// Iterating again gives the same sequence, and stopping early is honoured.
	n := 0
	for range gen.Registrations() {
		n++
		break
	}
	if n != 1 {
		t.Errorf("early break iterated %d times", n)
	}
}

func TestRegistrationsMatchTheIdentifiersProduced(t *testing.T) {
	gen := newRegistry(t)
	for rt, p := range gen.Registrations() {
		id, err := gen.GenerateFor(rt)
		if err != nil || !strings.HasPrefix(id, p+"_") {
			t.Errorf("%v: id %q, err %v, registered %q", rt, id, err, p)
		}
	}
}
