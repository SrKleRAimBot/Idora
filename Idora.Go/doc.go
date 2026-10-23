// Package idora generates entity identifiers that say what they are and sort by when they were
// made:
//
//	usr_01k2f9v8ta7m3x0qbrn4dzeh5c
//	ord_01k2f9v8tb9p5r2xnc7fkw3m0d
//
// An identifier is a prefix registered once per entity type, an underscore, and a lowercase
// [ULID] (the default) or a hyphen-free [UUIDv7]. Because both values begin with a millisecond
// timestamp, identifiers sort by creation time as plain strings and insert at the end of a
// database index rather than all over it.
//
// # Building a generator
//
// A [Generator] is configured once, at start-up, with functional options, and is immutable and
// safe for concurrent use afterwards:
//
//	gen, err := idora.New(
//		idora.WithEntity[Order]("ord"),       // explicit registration
//		idora.WithEntities(User{}, Invoice{}), // types that declare their own prefix
//	)
//
// A type declares its prefix by implementing [Entity]:
//
//	type User struct{ ID string }
//
//	func (User) IDPrefix() string { return "usr" }
//
// Declarations are not inherited: a struct that embeds User does not silently take "usr".
// [WithEntities] detects an IDPrefix method promoted from an embedded field and rejects it with
// a [*PromotedPrefixError].
//
// # Generating identifiers
//
// Go methods cannot have type parameters, so generation for a registered type is a generic
// function that takes the generator:
//
//	id, err := idora.Generate[User](gen)  // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
//	id := idora.MustGenerate[User](gen)   // panics instead; for init and tests
//	id, err := gen.GenerateWithPrefix("tmp") // ad-hoc prefix, validated on every call
//
// Pointer types are keyed by their element type, so Generate[*User] and Generate[User] are the
// same call.
//
// # Errors
//
// Failures are values. Every error this package returns can be matched with [errors.Is] against
// one of the sentinels ([ErrInvalidPrefix], [ErrDuplicatePrefix], [ErrPrefixNotRegistered],
// [ErrPromotedPrefix]) or unpacked with [errors.As] into the matching typed error, which carries
// the details as fields.
//
// # Concurrency and monotonicity
//
// Go has no thread-local storage, so each Generator guards its ULID state and its 1 KiB entropy
// buffer with a short critical section. The consequence is a stronger guarantee than per-thread
// state gives: every ULID from one Generator sorts strictly after the previous one, across all
// goroutines that share it. UUIDv7 values are ordered by millisecond only, so UUIDv7 generation
// needs no lock and draws its entropy from per-P buffers instead.
//
// This package implements the Idora specification, shared with the Idora packages for .NET,
// JavaScript, Java and Rust: https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md
//
// [ULID]: https://github.com/ulid/spec
// [UUIDv7]: https://www.rfc-editor.org/rfc/rfc9562
package idora
