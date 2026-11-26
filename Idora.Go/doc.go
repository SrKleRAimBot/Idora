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
// # Reading identifiers
//
// Identifiers come back, in a route, a message or a foreign key. [Parse] reads one into an [ID],
// a small comparable value holding its prefix, value, format and timestamp. Parsing is strict,
// accepts either format whatever the Generator is configured with, and does not allocate:
//
//	id, err := idora.Parse(s)                  // any valid identifier
//	id, err := idora.ParsePrefixed(s, "usr")   // one with exactly this prefix
//	id, err := idora.ParseFor[User](gen, s)    // one with User's registered prefix
//	ok := idora.IsValidFor[User](gen, s)
//
// IDs sort chronologically across prefixes and formats with [ID.Compare]
// (slices.SortFunc(ids, idora.ID.Compare)), report when they were minted with [ID.Time], and
// encode as text in JSON and as a string column in database/sql. [LowerBound] and [UpperBound]
// turn a span of time into a range of identifiers a database can answer from its primary key
// index.
//
// # Errors
//
// Failures are values. Every error this package returns can be matched with [errors.Is] against
// one of the sentinels ([ErrInvalidPrefix], [ErrDuplicatePrefix], [ErrPrefixNotRegistered],
// [ErrPromotedPrefix], [ErrInvalidID]) or unpacked with [errors.As] into the matching typed
// error, which carries the details as fields.
//
// # Extensions
//
// Package [github.com/SrKleRAimBot/Idora/Idora.Go/idorahttp], in this module, validates
// identifiers in net/http route wildcards. Integrations with third-party libraries are separate
// modules so that this one stays free of dependencies: idoragorm assigns primary keys and
// queries time ranges in GORM, and idorapgx registers ID as a PostgreSQL type with pgx.
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
