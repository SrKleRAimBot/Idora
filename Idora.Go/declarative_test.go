package idora_test

import (
	"errors"
	"maps"
	"reflect"
	"strings"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

func TestWithEntitiesRegistersDeclaredPrefixes(t *testing.T) {
	gen, err := idora.New(idora.WithEntities(Customer{}, &Shipment{}, Tag(""), Box[int]{}))
	if err != nil {
		t.Fatal(err)
	}
	want := map[reflect.Type]string{
		typeOf[Customer](): "cus",
		typeOf[Shipment](): "shp",
		typeOf[Tag]():      "tag",
		typeOf[Box[int]](): "box",
	}
	if got := maps.Collect(gen.Registrations()); !maps.Equal(got, want) {
		t.Errorf("registrations = %v, want %v", got, want)
	}
	if id := idora.MustGenerate[Customer](gen); !strings.HasPrefix(id, "cus_") {
		t.Errorf("id = %q", id)
	}
	if id := idora.MustGenerate[*Shipment](gen); !strings.HasPrefix(id, "shp_") {
		t.Errorf("id = %q", id)
	}
}

func TestWithEntitiesAcceptsValuesPointersAndNilPointers(t *testing.T) {
	for name, e := range map[string]idora.Entity{
		"value":       Customer{},
		"pointer":     &Customer{},
		"nil pointer": (*Customer)(nil),
	} {
		gen, err := idora.New(idora.WithEntities(e))
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		if p, _ := idora.PrefixOf[Customer](gen); p != "cus" {
			t.Errorf("%s: prefix = %q", name, p)
		}
	}
	// A pointer-receiver declaration can be passed as a nil pointer: IDPrefix is never called on it.
	if _, err := idora.New(idora.WithEntities((*Shipment)(nil))); err != nil {
		t.Errorf("nil *Shipment: %v", err)
	}
}

func TestWithEntitiesWithNothingRegistersNothing(t *testing.T) {
	gen := idora.MustNew(idora.WithEntities())
	for range gen.Registrations() {
		t.Error("registered something")
	}
}

func TestWithEntitiesRejectsPromotedDeclarations(t *testing.T) {
	tests := []struct {
		name   string
		entity idora.Entity
		typ    reflect.Type
		field  string
	}{
		{"value receiver", VIPCustomer{}, typeOf[VIPCustomer](), "Customer"},
		{"embedded pointer", CustomerRef{}, typeOf[CustomerRef](), "Customer"},
		{"pointer receiver", &ExpressShipment{}, typeOf[ExpressShipment](), "Shipment"},
		{"two levels deep", GoldCustomer{}, typeOf[GoldCustomer](), "VIPCustomer"},
		{"embedded interface", Wrapped{Customer{}}, typeOf[Wrapped](), "Entity"},
		{"unexported field", unexportedEmbed{}, typeOf[unexportedEmbed](), "customerAlias"},
		{"generic type", GenericPromoted[int]{}, typeOf[GenericPromoted[int]](), "Customer"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			_, err := idora.New(idora.WithEntities(tt.entity))
			var promoted *idora.PromotedPrefixError
			if !errors.As(err, &promoted) || !errors.Is(err, idora.ErrPromotedPrefix) {
				t.Fatalf("err = %v, want *PromotedPrefixError", err)
			}
			if promoted.EntityType != tt.typ || promoted.Field != tt.field {
				t.Errorf("details = %+v, want %v from %s", promoted, tt.typ, tt.field)
			}
			for _, want := range []string{tt.typ.String(), "not inherited", "declare IDPrefix on", "idora.WithEntity["} {
				if !strings.Contains(err.Error(), want) {
					t.Errorf("message %q lacks %q", err, want)
				}
			}
		})
	}
}

func TestWithEntitiesAcceptsADeclarationThatShadowsAnEmbeddedOne(t *testing.T) {
	tests := []struct {
		name   string
		entity idora.Entity
		typ    reflect.Type
		want   string
	}{
		{"value over value", Reseller{}, typeOf[Reseller](), "rsl"},
		{"pointer over pointer", &ReturnShipment{}, typeOf[ReturnShipment](), "rsh"},
		{"pointer over value", &Partner{}, typeOf[Partner](), "prt"},
		{"generic type", GenericShadow[string]{}, typeOf[GenericShadow[string]](), "gsh"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			gen, err := idora.New(idora.WithEntities(tt.entity))
			if err != nil {
				t.Fatal(err)
			}
			if p, ok := gen.LookupPrefix(tt.typ); !ok || p != tt.want {
				t.Errorf("prefix = %q, %v; want %q", p, ok, tt.want)
			}
		})
	}
}

func TestPromotedDeclarationCanBeRegisteredExplicitly(t *testing.T) {
	gen, err := idora.New(idora.WithEntities(Customer{}), idora.WithEntity[VIPCustomer]("vip"))
	if err != nil {
		t.Fatal(err)
	}
	if id := idora.MustGenerate[VIPCustomer](gen); !strings.HasPrefix(id, "vip_") {
		t.Errorf("id = %q", id)
	}
}

func TestEmbeddingTypeDoesNotTakeItsBasesPrefix(t *testing.T) {
	gen := idora.MustNew(idora.WithEntities(Customer{}))
	if _, ok := gen.LookupPrefix(typeOf[VIPCustomer]()); ok {
		t.Error("VIPCustomer inherited Customer's prefix")
	}
}

func TestWithEntitiesInvalidPrefixNamesTheType(t *testing.T) {
	tests := []struct {
		entity idora.Entity
		typ    reflect.Type
		reason idora.InvalidPrefixReason
	}{
		{BadEmpty{}, typeOf[BadEmpty](), idora.ReasonEmpty},
		{BadLong{}, typeOf[BadLong](), idora.ReasonTooLong},
		{BadSeparator{}, typeOf[BadSeparator](), idora.ReasonSeparator},
		{BadCharacter{}, typeOf[BadCharacter](), idora.ReasonCharacter},
	}
	for _, tt := range tests {
		t.Run(tt.typ.Name(), func(t *testing.T) {
			_, err := idora.New(idora.WithEntities(tt.entity))
			var invalid *idora.InvalidPrefixError
			if !errors.As(err, &invalid) {
				t.Fatalf("err = %v", err)
			}
			if invalid.EntityType != tt.typ || invalid.Reason != tt.reason || invalid.Prefix != tt.entity.IDPrefix() {
				t.Errorf("details = %+v", invalid)
			}
			if !strings.Contains(err.Error(), "the IDPrefix method of "+tt.typ.String()) {
				t.Errorf("message %q does not name the type", err)
			}
		})
	}
	_, err := idora.New(idora.WithEntities(BadSeparator{}))
	if !strings.Contains(err.Error(), "separator '_' at index 4") {
		t.Errorf("message %q lacks the separator detail", err)
	}
}

func TestWithEntitiesNilEntityFails(t *testing.T) {
	_, err := idora.New(idora.WithEntities(Customer{}, nil))
	if err == nil || !strings.Contains(err.Error(), "nil Entity") {
		t.Errorf("err = %v", err)
	}
}

func TestWithEntitiesReportsAClashTheSameWayWhateverTheOrder(t *testing.T) {
	for _, list := range [][]idora.Entity{{ClashAlpha{}, ClashBeta{}}, {ClashBeta{}, ClashAlpha{}}} {
		_, err := idora.New(idora.WithEntities(list...))
		var dup *idora.DuplicatePrefixError
		if !errors.As(err, &dup) {
			t.Fatalf("err = %v", err)
		}
		// ClashAlpha sorts first by fully qualified name, so it holds the prefix.
		if dup.EntityType != typeOf[ClashBeta]() || dup.ConflictingEntityType != typeOf[ClashAlpha]() ||
			dup.Prefix != "DUP" || dup.ExistingPrefix != "dup" {
			t.Errorf("details = %+v", dup)
		}
	}
}

func TestWithEntitiesRegistersInQualifiedNameOrder(t *testing.T) {
	gen := idora.MustNew(idora.WithEntities(Tag(""), &Shipment{}, Customer{}, Box[int]{}))
	var names []string
	for rt := range gen.Registrations() {
		names = append(names, rt.Name())
	}
	if got := strings.Join(names, ","); got != "Box[int],Customer,Shipment,Tag" {
		t.Errorf("order = %s", got)
	}
}

func TestWithEntitiesSameTypeTwice(t *testing.T) {
	_, err := idora.New(idora.WithEntities(Customer{}, &Customer{}))
	if !errors.Is(err, idora.ErrDuplicatePrefix) || !strings.Contains(err.Error(), "registered a second time") {
		t.Errorf("err = %v", err)
	}
}

func TestWithEntitiesCalledTwiceIsADuplicate(t *testing.T) {
	_, err := idora.New(idora.WithEntities(Customer{}), idora.WithEntities(Customer{}))
	if !errors.Is(err, idora.ErrDuplicatePrefix) {
		t.Errorf("err = %v", err)
	}
}

func TestDeclarativeAndExplicitRegistrationPoliceEachOther(t *testing.T) {
	tests := []struct {
		name     string
		opts     []idora.Option
		wantType reflect.Type
		wantWord string
	}{
		{"declared then explicit with the same prefix",
			[]idora.Option{idora.WithEntities(Customer{}), idora.WithEntity[Customer]("cus")},
			typeOf[Customer](), "registered a second time"},
		{"explicit then declared with the same prefix",
			[]idora.Option{idora.WithEntity[Customer]("cus"), idora.WithEntities(Customer{})},
			typeOf[Customer](), "registered a second time"},
		{"declared then explicit with a different prefix",
			[]idora.Option{idora.WithEntities(Customer{}), idora.WithEntity[Customer]("cust")},
			typeOf[Customer](), `cannot also be registered with "cust"`},
		{"prefix taken explicitly, then declared",
			[]idora.Option{idora.WithEntity[User]("CUS"), idora.WithEntities(Customer{})},
			typeOf[Customer](), "already registered for idora_test.User"},
		{"prefix taken by a declaration, then explicit",
			[]idora.Option{idora.WithEntities(Customer{}), idora.WithEntity[User]("cus")},
			typeOf[User](), "already registered for idora_test.Customer"},
		{"two declarations claiming one prefix",
			[]idora.Option{idora.WithEntities(Customer{}, CustomerTwin{})},
			typeOf[CustomerTwin](), "already registered for idora_test.Customer"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			_, err := idora.New(tt.opts...)
			var dup *idora.DuplicatePrefixError
			if !errors.As(err, &dup) || dup.EntityType != tt.wantType {
				t.Fatalf("err = %v, want a duplicate for %v", err, tt.wantType)
			}
			if !strings.Contains(err.Error(), tt.wantWord) {
				t.Errorf("message %q lacks %q", err, tt.wantWord)
			}
		})
	}
}

func TestMixingDeclarativeAndExplicitKeepsBoth(t *testing.T) {
	for _, opts := range [][]idora.Option{
		{idora.WithEntities(Customer{}), idora.WithEntity[User]("usr")},
		{idora.WithEntity[User]("usr"), idora.WithEntities(Customer{})},
	} {
		gen := idora.MustNew(opts...)
		if len(maps.Collect(gen.Registrations())) != 2 {
			t.Errorf("registrations = %v", maps.Collect(gen.Registrations()))
		}
	}
}

func TestWithEntitiesStopsAtTheFirstFailure(t *testing.T) {
	// BadSeparator sorts first by name, so its failure is reported rather than the clash
	// between Customer and CustomerTwin.
	_, err := idora.New(idora.WithEntities(CustomerTwin{}, Customer{}, BadSeparator{}))
	if !errors.Is(err, idora.ErrInvalidPrefix) {
		t.Errorf("err = %v, want the first failure in name order", err)
	}
}
