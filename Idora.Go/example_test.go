package idora_test

import (
	"errors"
	"fmt"
	"maps"
	"reflect"
	"strings"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

// Example types. A plain struct is registered explicitly; a type with an IDPrefix method
// declares its own prefix.
type (
	ExampleOrder   struct{ ID string }
	ExampleInvoice struct{ ID string }
)

type ExampleUser struct{ ID string }

func (ExampleUser) IDPrefix() string { return "usr" }

type ExampleAdmin struct{ ExampleUser }

// shape replaces the random part of an identifier so the examples print stable output.
func shape(id string) string {
	prefix, value, _ := strings.Cut(id, "_")
	return fmt.Sprintf("%s_<%d characters>", prefix, len(value))
}

func Example() {
	gen, err := idora.New(
		idora.WithEntity[ExampleOrder]("ord"),
		idora.WithEntities(ExampleUser{}),
	)
	if err != nil {
		panic(err) // a registration mistake: fail at start-up
	}

	id, err := idora.Generate[ExampleOrder](gen)
	if err != nil {
		panic(err)
	}
	fmt.Println(shape(id))
	fmt.Println(shape(idora.MustGenerate[ExampleUser](gen)))
	// Output:
	// ord_<26 characters>
	// usr_<26 characters>
}

func ExampleNew() {
	gen, err := idora.New(
		idora.WithUUIDv7(),
		idora.WithEntity[ExampleOrder]("ord"),
		idora.WithEntity[ExampleInvoice]("inv"),
	)
	if err != nil {
		panic(err)
	}
	fmt.Println(gen.Strategy())
	fmt.Println(shape(idora.MustGenerate[ExampleInvoice](gen)))
	// Output:
	// UUIDv7
	// inv_<32 characters>
}

func ExampleGenerate() {
	gen := idora.MustNew(idora.WithEntity[ExampleOrder]("ord"))

	id, err := idora.Generate[ExampleOrder](gen)
	fmt.Println(shape(id), err)

	// A type that was never registered is an error, never an invented prefix.
	_, err = idora.Generate[ExampleInvoice](gen)
	fmt.Println(errors.Is(err, idora.ErrPrefixNotRegistered))
	// Output:
	// ord_<26 characters> <nil>
	// true
}

func ExampleGenerator_GenerateWithPrefix() {
	gen := idora.MustNew()

	id, err := gen.GenerateWithPrefix("tmp")
	fmt.Println(shape(id), err)

	_, err = gen.GenerateWithPrefix("tmp_file")
	fmt.Println(err)
	// Output:
	// tmp_<26 characters> <nil>
	// idora: the entity prefix "tmp_file" contains the separator '_' at index 3; the separator divides the prefix from the value, so a prefix cannot contain one
}

func ExampleWithEntities() {
	gen, err := idora.New(idora.WithEntities(ExampleUser{}))
	fmt.Println(err)
	fmt.Println(idora.PrefixOf[ExampleUser](gen))

	// ExampleAdmin embeds ExampleUser, so Go promotes its IDPrefix method. Declarations are
	// not inherited, so it is rejected rather than silently given "usr".
	_, err = idora.New(idora.WithEntities(ExampleAdmin{}))
	fmt.Println(errors.Is(err, idora.ErrPromotedPrefix))
	// Output:
	// <nil>
	// usr <nil>
	// true
}

func ExamplePrefixOf() {
	gen := idora.MustNew(idora.WithEntity[ExampleOrder]("ord"))

	prefix, _ := idora.PrefixOf[ExampleOrder](gen)
	id := idora.MustGenerate[ExampleOrder](gen)
	fmt.Println(strings.HasPrefix(id, prefix+"_"))
	// Output: true
}

func ExampleGenerator_LookupPrefix() {
	gen := idora.MustNew(idora.WithEntity[ExampleOrder]("ord"))

	fmt.Println(gen.LookupPrefix(reflect.TypeFor[ExampleOrder]()))
	fmt.Println(gen.LookupPrefix(reflect.TypeFor[*ExampleOrder]()))
	fmt.Println(gen.LookupPrefix(reflect.TypeFor[ExampleInvoice]()))
	// Output:
	// ord true
	// ord true
	//  false
}

func ExampleGenerator_Registrations() {
	gen := idora.MustNew(
		idora.WithEntity[ExampleOrder]("ord"),
		idora.WithEntity[ExampleInvoice]("inv"),
	)
	for t, prefix := range gen.Registrations() {
		fmt.Println(t, prefix)
	}
	fmt.Println(len(maps.Collect(gen.Registrations())))
	// Output:
	// idora_test.ExampleOrder ord
	// idora_test.ExampleInvoice inv
	// 2
}

func ExampleDuplicatePrefixError() {
	_, err := idora.New(
		idora.WithEntity[ExampleOrder]("ord"),
		idora.WithEntity[ExampleInvoice]("ORD"),
	)
	var dup *idora.DuplicatePrefixError
	if errors.As(err, &dup) {
		fmt.Println(dup.Prefix, dup.EntityType, dup.ConflictingEntityType)
	}
	// Output: ORD idora_test.ExampleInvoice idora_test.ExampleOrder
}

func ExampleInvalidPrefixError() {
	_, err := idora.New(idora.WithEntity[ExampleOrder]("café"))
	var invalid *idora.InvalidPrefixError
	if errors.As(err, &invalid) {
		fmt.Println(invalid.Reason, invalid.Index, string(invalid.Char))
	}
	// Output: character 3 é
}

// Splitting on the first separator recovers both parts, because a prefix cannot contain one.
func Example_split() {
	gen := idora.MustNew(idora.WithEntity[ExampleOrder]("ord"))
	id := idora.MustGenerate[ExampleOrder](gen)

	prefix, value, _ := strings.Cut(id, "_")
	fmt.Println(prefix, len(value))
	// Output: ord 26
}
