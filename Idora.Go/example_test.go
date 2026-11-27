package idora_test

import (
	"encoding/json"
	"errors"
	"fmt"
	"maps"
	"reflect"
	"slices"
	"strings"
	"time"

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

func ExampleParse() {
	id, err := idora.Parse("usr_01k226e0g0008j4ct4ank7f24s")
	if err != nil {
		panic(err)
	}
	fmt.Println(id.Prefix(), id.ValuePart(), id.Strategy())
	fmt.Println(id.UnixMilli(), id.Time())

	// Parsing is strict: no trimming, no case folding, no hyphens.
	_, err = idora.Parse("usr_01K226E0G0008J4CT4ANK7F24S")
	var invalid *idora.InvalidIDError
	if errors.As(err, &invalid) {
		fmt.Println(invalid.Reason)
	}
	// Output:
	// usr 01k226e0g0008j4ct4ank7f24s ULID
	// 1754568000000 2025-08-07 12:00:00 +0000 UTC
	// value
}

func ExampleParsePrefixed() {
	_, err := idora.ParsePrefixed("usr_01k226e0g0008j4ct4ank7f24s", "ord")
	fmt.Println(err)
	// Output:
	// idora: "usr_01k226e0g0008j4ct4ank7f24s" has the prefix "usr", but the prefix "ord" was expected; prefixes are compared exactly, case included, so the identifier belongs to a different entity type
}

func ExampleParseFor() {
	gen := idora.MustNew(
		idora.WithEntity[ExampleOrder]("ord"),
		idora.WithEntities(ExampleUser{}),
	)

	id, err := idora.ParseFor[ExampleUser](gen, "usr_01k226e0g0008j4ct4ank7f24s")
	fmt.Println(id, err)

	_, err = idora.ParseFor[ExampleOrder](gen, "usr_01k226e0g0008j4ct4ank7f24s")
	fmt.Println(errors.Is(err, idora.ErrInvalidID))

	// An unregistered type is the program's mistake, not the identifier's.
	_, err = idora.ParseFor[ExampleInvoice](gen, "inv_0198846702007011a233445566778899")
	fmt.Println(errors.Is(err, idora.ErrPrefixNotRegistered))
	// Output:
	// usr_01k226e0g0008j4ct4ank7f24s <nil>
	// true
	// true
}

func ExampleIsValid() {
	fmt.Println(idora.IsValid("inv_0198846702007011a233445566778899"))
	fmt.Println(idora.IsValid("inv_01988467-0200-7011-a233-445566778899"))
	fmt.Println(idora.IsValidPrefixed("inv_0198846702007011a233445566778899", "ord"))
	// Output:
	// true
	// false
	// false
}

// The natural order is chronological, whatever the prefix or the format: an order created after
// a user sorts after it, although "ord" < "usr" as strings.
func ExampleID_Compare() {
	ids := []idora.ID{
		idora.MustParse("ord_01k226e0g1zzqdvk5vnacrgxv6"),       // 12:00:00.001
		idora.MustParse("inv_0198846702007011a233445566778899"), // 12:00:00.000, UUIDv7
		idora.MustParse("usr_01hf7yat0004hmasw9nf6yy093"),       // 2023
	}
	slices.SortFunc(ids, idora.ID.Compare)
	for _, id := range ids {
		fmt.Println(id, id.Time().Format(time.RFC3339Nano))
	}
	// Output:
	// usr_01hf7yat0004hmasw9nf6yy093 2023-11-14T22:13:20Z
	// inv_0198846702007011a233445566778899 2025-08-07T12:00:00Z
	// ord_01k226e0g1zzqdvk5vnacrgxv6 2025-08-07T12:00:00.001Z
}

func ExampleID_CreatedAfter() {
	id := idora.MustParse("usr_01k226e0g0008j4ct4ank7f24s")
	cutoff := time.Date(2025, 8, 1, 0, 0, 0, 0, time.UTC)
	fmt.Println(id.CreatedAfter(cutoff), id.CreatedBefore(cutoff))
	// Output: true false
}

func ExampleID_json() {
	type Order struct {
		ID       idora.ID `json:"id"`
		Customer idora.ID `json:"customer"`
	}
	var o Order
	err := json.Unmarshal([]byte(`{"id":"ord_01k226e0g1zzqdvk5vnacrgxv6","customer":"cus_01k226e0g0008j4ct4ank7f24s"}`), &o)
	fmt.Println(o.ID.Prefix(), o.Customer.Prefix(), err)

	err = json.Unmarshal([]byte(`{"id":"ord_1"}`), &o)
	fmt.Println(errors.Is(err, idora.ErrInvalidID))
	// Output:
	// ord cus <nil>
	// true
}

func ExampleLowerBound() {
	from := time.Date(2025, 8, 7, 12, 0, 0, 0, time.UTC)
	to := from.Add(time.Hour)

	lo, _ := idora.LowerBound("usr", from, idora.ULID)
	hi, _ := idora.UpperBound("usr", to, idora.ULID)
	fmt.Println(lo)
	fmt.Println(hi)

	// Every "usr" ULID minted in [from, to] sorts between them as a plain string, so a database
	// can answer "WHERE id >= $1 AND id <= $2" from the primary key index alone.
	id := "usr_01k226e0g0008j4ct4ank7f24s"
	fmt.Println(lo.String() <= id && id <= hi.String())
	// Output:
	// usr_01k226e0g00000000000000000
	// usr_01k229vw40zzzzzzzzzzzzzzzz
	// true
}

func ExampleInvalidIDError() {
	_, err := idora.Parse("us-r_01k226e0g0008j4ct4ank7f24s")

	var invalid *idora.InvalidIDError
	if errors.As(err, &invalid) {
		fmt.Println(invalid.Reason, invalid.Cause.Index)
	}
	// The prefix error is the cause, so it matches both sentinels.
	fmt.Println(errors.Is(err, idora.ErrInvalidID), errors.Is(err, idora.ErrInvalidPrefix))
	// Output:
	// prefix 2
	// true true
}

func ExampleGenerateID() {
	gen := idora.MustNew(idora.WithUUIDv7(), idora.WithEntity[ExampleInvoice]("inv"))

	id, err := idora.GenerateID[ExampleInvoice](gen)
	fmt.Println(id.Prefix(), id.Strategy(), len(id.ValuePart()), err)
	// Output: inv UUIDv7 32 <nil>
}
