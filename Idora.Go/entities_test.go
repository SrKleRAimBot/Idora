package idora_test

import idora "github.com/SrKleRAimBot/Idora/Idora.Go"

// Plain key types for explicit registration. They need no methods.
type (
	User    struct{ ID string }
	Order   struct{ ID string }
	Invoice struct{ ID string }
	Account struct{ ID string }
	Accrual struct{ ID string }
	Comment struct{ ID string } // never registered
	Point   struct{ X, Y int }  // a small value type
)

// Declarative entities.

// Customer declares its prefix with a value receiver.
type Customer struct{ ID string }

func (Customer) IDPrefix() string { return "cus" }

// Shipment declares its prefix with a pointer receiver.
type Shipment struct{ ID string }

func (*Shipment) IDPrefix() string { return "shp" }

// Tag is a non-struct type declaring a prefix.
type Tag string

func (Tag) IDPrefix() string { return "tag" }

// Box is a generic type declaring a prefix.
type Box[T any] struct{ V T }

func (Box[T]) IDPrefix() string { return "box" }

// VIPCustomer embeds a value-receiver declaration and declares nothing itself: promoted.
type VIPCustomer struct{ Customer }

// CustomerRef embeds a pointer to a value-receiver declaration: promoted.
type CustomerRef struct{ *Customer }

// ExpressShipment embeds a pointer-receiver declaration: promoted into *ExpressShipment only.
type ExpressShipment struct{ Shipment }

// GoldCustomer is promoted through two levels of embedding.
type GoldCustomer struct{ VIPCustomer }

// Wrapped embeds the Entity interface itself: promoted.
type Wrapped struct{ idora.Entity }

// unexportedEmbed is promoted through an unexported embedded field.
type unexportedEmbed struct{ customerAlias }

type customerAlias = Customer

// GenericPromoted is a generic type whose prefix is promoted.
type GenericPromoted[T any] struct{ Customer }

// Reseller embeds Customer but declares its own prefix with a value receiver: accepted.
type Reseller struct{ Customer }

func (Reseller) IDPrefix() string { return "rsl" }

// ReturnShipment embeds Shipment but declares its own prefix with a pointer receiver: accepted.
type ReturnShipment struct{ Shipment }

func (*ReturnShipment) IDPrefix() string { return "rsh" }

// Partner embeds a value-receiver declaration and shadows it with a pointer receiver: accepted.
type Partner struct{ Customer }

func (*Partner) IDPrefix() string { return "prt" }

// GenericShadow is a generic type that embeds a declaration and shadows it: accepted.
type GenericShadow[T any] struct{ Customer }

func (GenericShadow[T]) IDPrefix() string { return "gsh" }

// Entities declaring invalid prefixes.

type BadEmpty struct{}

func (BadEmpty) IDPrefix() string { return "" }

type BadLong struct{}

func (BadLong) IDPrefix() string { return "abcdefghijklmnopq" }

type BadSeparator struct{}

func (BadSeparator) IDPrefix() string { return "user_account" }

type BadCharacter struct{}

func (BadCharacter) IDPrefix() string { return "usr!" }

// Entities that clash with each other, ignoring case.

type ClashAlpha struct{}

func (ClashAlpha) IDPrefix() string { return "dup" }

type ClashBeta struct{}

func (ClashBeta) IDPrefix() string { return "DUP" }

// CustomerTwin claims Customer's prefix.
type CustomerTwin struct{}

func (CustomerTwin) IDPrefix() string { return "cus" }
