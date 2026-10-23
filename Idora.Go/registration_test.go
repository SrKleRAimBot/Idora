package idora_test

import (
	"errors"
	"reflect"
	"strings"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

func typeOf[T any]() reflect.Type { return reflect.TypeFor[T]() }

func mustPrefixOfID(t *testing.T, id string) string {
	t.Helper()
	prefix, _, ok := strings.Cut(id, "_")
	if !ok {
		t.Fatalf("%q has no separator", id)
	}
	return prefix
}

func TestNewWithNoOptions(t *testing.T) {
	gen, err := idora.New()
	if err != nil {
		t.Fatal(err)
	}
	if gen.Strategy() != idora.ULID {
		t.Errorf("default strategy = %v, want ULID", gen.Strategy())
	}
	id, err := gen.GenerateWithPrefix("tmp")
	if err != nil || len(id) != len("tmp_")+26 {
		t.Errorf("GenerateWithPrefix = %q, %v", id, err)
	}
	if _, err := idora.Generate[User](gen); !errors.Is(err, idora.ErrPrefixNotRegistered) {
		t.Errorf("Generate with nothing registered: err = %v, want ErrPrefixNotRegistered", err)
	}
}

func TestZeroGeneratorIsUsable(t *testing.T) {
	var gen idora.Generator
	a, err := gen.GenerateWithPrefix("tmp")
	if err != nil {
		t.Fatal(err)
	}
	b, _ := gen.GenerateWithPrefix("tmp")
	if !(a < b) {
		t.Errorf("zero Generator produced %s then %s", a, b)
	}
	if _, ok := gen.LookupPrefix(typeOf[User]()); ok {
		t.Errorf("zero Generator has a registration")
	}
}

func TestStrategyOptions(t *testing.T) {
	tests := []struct {
		name string
		opts []idora.Option
		want idora.Strategy
		len  int
	}{
		{"default", nil, idora.ULID, 26},
		{"WithULID", []idora.Option{idora.WithULID()}, idora.ULID, 26},
		{"WithUUIDv7", []idora.Option{idora.WithUUIDv7()}, idora.UUIDv7, 32},
		{"WithStrategy", []idora.Option{idora.WithStrategy(idora.UUIDv7)}, idora.UUIDv7, 32},
		{"last wins", []idora.Option{idora.WithUUIDv7(), idora.WithULID()}, idora.ULID, 26},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			gen := idora.MustNew(tt.opts...)
			if gen.Strategy() != tt.want {
				t.Errorf("Strategy() = %v, want %v", gen.Strategy(), tt.want)
			}
			id, _ := gen.GenerateWithPrefix("x")
			if len(id) != 2+tt.len {
				t.Errorf("%q has a %d-character value, want %d", id, len(id)-2, tt.len)
			}
		})
	}
}

func TestWithStrategyUndefinedFails(t *testing.T) {
	_, err := idora.New(idora.WithStrategy(idora.Strategy(42)))
	if err == nil || !strings.Contains(err.Error(), "Strategy(42)") || !strings.Contains(err.Error(), "idora.ULID") {
		t.Errorf("err = %v, want an error naming the strategy and the alternatives", err)
	}
}

func TestStrategyString(t *testing.T) {
	for s, want := range map[idora.Strategy]string{idora.ULID: "ULID", idora.UUIDv7: "UUIDv7", 9: "Strategy(9)"} {
		if s.String() != want {
			t.Errorf("String() = %q, want %q", s.String(), want)
		}
	}
}

func TestZeroOptionIsIgnored(t *testing.T) {
	if _, err := idora.New(idora.Option{}, idora.WithEntity[User]("usr")); err != nil {
		t.Errorf("err = %v", err)
	}
}

func TestWithEntityRegistersEachTypeWithItsOwnPrefix(t *testing.T) {
	for _, strategy := range []idora.Strategy{idora.ULID, idora.UUIDv7} {
		gen := idora.MustNew(
			idora.WithStrategy(strategy),
			idora.WithEntity[User]("usr"),
			idora.WithEntity[Order]("ord"),
			idora.WithEntity[Invoice]("inv"),
		)
		for want, got := range map[string]string{
			"usr": idora.MustGenerate[User](gen),
			"ord": idora.MustGenerate[Order](gen),
			"inv": idora.MustGenerate[Invoice](gen),
		} {
			if p := mustPrefixOfID(t, got); p != want {
				t.Errorf("%v: got prefix %q, want %q", strategy, p, want)
			}
		}
	}
}

func TestWithEntityKeepsThePrefixExactlyAsWritten(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("USR"))
	if id := idora.MustGenerate[User](gen); !strings.HasPrefix(id, "USR_") {
		t.Errorf("id = %q, want the prefix emitted unchanged", id)
	}
	if p, _ := idora.PrefixOf[User](gen); p != "USR" {
		t.Errorf("PrefixOf = %q", p)
	}
}

func TestWithEntityForAValueType(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[Point]("pt"), idora.WithEntity[int]("int"))
	if id := idora.MustGenerate[Point](gen); !strings.HasPrefix(id, "pt_") {
		t.Errorf("id = %q", id)
	}
	if id := idora.MustGenerate[int](gen); !strings.HasPrefix(id, "int_") {
		t.Errorf("id = %q", id)
	}
}

func TestPointerTypesAreKeyedByTheirElementType(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[*User]("usr"))
	for name, gotFn := range map[string]func() (string, error){
		"Generate[User]":  func() (string, error) { return idora.Generate[User](gen) },
		"Generate[*User]": func() (string, error) { return idora.Generate[*User](gen) },
		"Generate[**User]": func() (string, error) {
			return idora.Generate[**User](gen)
		},
	} {
		id, err := gotFn()
		if err != nil || !strings.HasPrefix(id, "usr_") {
			t.Errorf("%s = %q, %v", name, id, err)
		}
	}
	if p, ok := gen.LookupPrefix(typeOf[*User]()); !ok || p != "usr" {
		t.Errorf("LookupPrefix(*User) = %q, %v", p, ok)
	}
	for rt := range gen.Registrations() {
		if rt != typeOf[User]() {
			t.Errorf("registered key = %v, want the element type", rt)
		}
	}
	// User and *User are one registration, so registering both is a duplicate.
	_, err := idora.New(idora.WithEntity[User]("usr"), idora.WithEntity[*User]("usr"))
	if !errors.Is(err, idora.ErrDuplicatePrefix) {
		t.Errorf("User then *User: err = %v, want ErrDuplicatePrefix", err)
	}
}

func TestWithEntityTypeBehavesLikeWithEntity(t *testing.T) {
	gen := idora.MustNew(idora.WithEntityType(typeOf[User](), "usr"))
	if id := idora.MustGenerate[User](gen); !strings.HasPrefix(id, "usr_") {
		t.Errorf("id = %q", id)
	}
}

func TestWithEntityTypeNilFails(t *testing.T) {
	_, err := idora.New(idora.WithEntityType(nil, "usr"))
	if err == nil || !strings.Contains(err.Error(), "nil reflect.Type") {
		t.Errorf("err = %v", err)
	}
}

func TestWithEntityInvalidPrefixFails(t *testing.T) {
	for _, p := range []string{"", "abcdefghijklmnopq", "user_account", "us-r", "café"} {
		_, err := idora.New(idora.WithEntity[User](p))
		var invalid *idora.InvalidPrefixError
		if !errors.As(err, &invalid) || invalid.Prefix != p || invalid.EntityType != nil {
			t.Errorf("WithEntity(%q): err = %#v", p, err)
		}
	}
}

func TestPrefixAlreadyGivenToAnotherTypeNamesBothTypes(t *testing.T) {
	_, err := idora.New(idora.WithEntity[Account]("acc"), idora.WithEntity[Accrual]("acc"))
	var dup *idora.DuplicatePrefixError
	if !errors.As(err, &dup) {
		t.Fatalf("err = %v, want *DuplicatePrefixError", err)
	}
	if dup.Prefix != "acc" || dup.EntityType != typeOf[Accrual]() || dup.ConflictingEntityType != typeOf[Account]() {
		t.Errorf("details = %+v", dup)
	}
	for _, want := range []string{`"acc"`, "idora_test.Account", "idora_test.Accrual", "ignoring case", "choose a different prefix"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("message %q lacks %q", err, want)
		}
	}
}

func TestPrefixDifferingOnlyByCaseIsADuplicate(t *testing.T) {
	for _, pair := range [][2]string{{"usr", "USR"}, {"USR", "usr"}, {"Usr", "uSR"}} {
		_, err := idora.New(idora.WithEntity[User](pair[0]), idora.WithEntity[Account](pair[1]))
		var dup *idora.DuplicatePrefixError
		if !errors.As(err, &dup) || dup.Prefix != pair[1] || dup.ExistingPrefix != pair[0] {
			t.Errorf("%q then %q: err = %v", pair[0], pair[1], err)
		}
	}
}

func TestSameTypeTwiceWithTheSamePrefix(t *testing.T) {
	_, err := idora.New(idora.WithEntity[User]("usr"), idora.WithEntity[User]("usr"))
	var dup *idora.DuplicatePrefixError
	if !errors.As(err, &dup) || dup.EntityType != typeOf[User]() || dup.ConflictingEntityType != typeOf[User]() {
		t.Fatalf("err = %v", err)
	}
	if !strings.Contains(err.Error(), "registered a second time") || strings.Contains(err.Error(), "cannot also be registered with") {
		t.Errorf("message = %q, want the 'second time' wording", err)
	}
}

func TestSameTypeTwiceWithDifferentPrefixes(t *testing.T) {
	_, err := idora.New(idora.WithEntity[User]("usr"), idora.WithEntity[User]("acc"))
	var dup *idora.DuplicatePrefixError
	if !errors.As(err, &dup) || dup.Prefix != "acc" || dup.ExistingPrefix != "usr" {
		t.Fatalf("err = %v", err)
	}
	if !strings.Contains(err.Error(), `cannot also be registered with "acc"`) {
		t.Errorf("message = %q, want the 'cannot also be registered with' wording", err)
	}
}

func TestSameTypeIsCheckedBeforeThePrefix(t *testing.T) {
	// User is already registered AND "acc" belongs to Account: the type error wins.
	_, err := idora.New(idora.WithEntity[User]("usr"), idora.WithEntity[Account]("acc"), idora.WithEntity[User]("acc"))
	var dup *idora.DuplicatePrefixError
	if !errors.As(err, &dup) || dup.ConflictingEntityType != typeOf[User]() {
		t.Errorf("err = %v, want the same-type error", err)
	}
}

func TestFirstErrorWinsInOptionOrder(t *testing.T) {
	_, err := idora.New(
		idora.WithEntity[User]("usr"),
		idora.WithEntity[Order]("bad!"),
		idora.WithEntity[Account]("usr"),
	)
	if !errors.Is(err, idora.ErrInvalidPrefix) || errors.Is(err, idora.ErrDuplicatePrefix) {
		t.Errorf("err = %v, want the invalid prefix error first", err)
	}
}

func TestMustNewPanicsWithTheError(t *testing.T) {
	defer func() {
		r := recover()
		err, ok := r.(error)
		if !ok || !errors.Is(err, idora.ErrInvalidPrefix) {
			t.Errorf("recovered %v, want an ErrInvalidPrefix error", r)
		}
	}()
	idora.MustNew(idora.WithEntity[User](""))
}

func TestGenerateForAnUnregisteredTypeNamesTheType(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	_, err := idora.Generate[Comment](gen)
	var missing *idora.PrefixNotRegisteredError
	if !errors.As(err, &missing) || missing.EntityType != typeOf[Comment]() {
		t.Fatalf("err = %v", err)
	}
	for _, want := range []string{"idora_test.Comment", `idora.WithEntity[Comment]("abc")`, "GenerateWithPrefix"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("message %q lacks %q", err, want)
		}
	}
}

func TestMustGeneratePanicsForAnUnregisteredType(t *testing.T) {
	defer func() {
		if err, _ := recover().(error); !errors.Is(err, idora.ErrPrefixNotRegistered) {
			t.Errorf("recovered %v", err)
		}
	}()
	idora.MustGenerate[Comment](idora.MustNew())
}

func TestGenerateForRuntimeType(t *testing.T) {
	gen := idora.MustNew(idora.WithEntity[User]("usr"))
	if id, err := gen.GenerateFor(typeOf[User]()); err != nil || !strings.HasPrefix(id, "usr_") {
		t.Errorf("GenerateFor = %q, %v", id, err)
	}
	if _, err := gen.GenerateFor(nil); !errors.Is(err, idora.ErrPrefixNotRegistered) {
		t.Errorf("GenerateFor(nil): err = %v", err)
	}
}

func TestGenerateWithPrefix(t *testing.T) {
	gen := idora.MustNew()
	for _, p := range []string{"a", "tmp", "USR", "Usr1", "0", "abcdefghijklmnop"} {
		id, err := gen.GenerateWithPrefix(p)
		if err != nil || mustPrefixOfID(t, id) != p {
			t.Errorf("GenerateWithPrefix(%q) = %q, %v", p, id, err)
		}
	}
	for _, p := range []string{"", "abcdefghijklmnopq", "a_b", "a b", "é"} {
		id, err := gen.GenerateWithPrefix(p)
		var invalid *idora.InvalidPrefixError
		if id != "" || !errors.As(err, &invalid) || invalid.Prefix != p {
			t.Errorf("GenerateWithPrefix(%q) = %q, %v", p, id, err)
		}
	}
}

func TestFailedConstructionReturnsNilGenerator(t *testing.T) {
	gen, err := idora.New(idora.WithEntity[User](""))
	if gen != nil || err == nil {
		t.Errorf("New = %v, %v", gen, err)
	}
}
