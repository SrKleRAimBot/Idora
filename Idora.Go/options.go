package idora

import (
	"cmp"
	"errors"
	"fmt"
	"reflect"
	"slices"
)

// Option configures a [Generator] built by [New]. Options are applied in the order given, and
// the first one that fails stops construction and is returned by New.
type Option struct {
	apply func(*builder) error
}

// builder accumulates the configuration while options are applied.
type builder struct {
	strategy Strategy
	prefixes map[reflect.Type]string
	byFolded map[string]reflect.Type
	order    []registration
}

type registration struct {
	entityType reflect.Type
	prefix     string
}

// WithStrategy selects the value format. The default is [ULID]; when several strategy options
// are given the last one wins. An undefined Strategy makes [New] fail.
func WithStrategy(s Strategy) Option {
	return Option{func(b *builder) error {
		if !s.valid() {
			return fmt.Errorf("idora: %s is not a defined strategy; use idora.ULID or idora.UUIDv7", s)
		}
		b.strategy = s
		return nil
	}}
}

// WithULID selects [ULID] values, the default. It is shorthand for WithStrategy(ULID).
func WithULID() Option { return WithStrategy(ULID) }

// WithUUIDv7 selects [UUIDv7] values. It is shorthand for WithStrategy(UUIDv7).
func WithUUIDv7() Option { return WithStrategy(UUIDv7) }

// WithEntity registers prefix for entity type T. T is used purely as a key: it needs no methods
// and no particular shape. A pointer type registers its element type, so WithEntity[*User] and
// WithEntity[User] are the same registration.
//
// The prefix must be 1 to [MaxPrefixLen] ASCII letters or digits ([ErrInvalidPrefix]), and
// neither T nor a prefix equal to this one ignoring ASCII case may already be registered
// ([ErrDuplicatePrefix]).
func WithEntity[T any](prefix string) Option {
	return WithEntityType(reflect.TypeFor[T](), prefix)
}

// WithEntityType is [WithEntity] for a type known only at run time, for configuration driven by
// reflection. A nil type makes [New] fail.
func WithEntityType(t reflect.Type, prefix string) Option {
	return Option{func(b *builder) error {
		if t == nil {
			return errors.New("idora: WithEntityType was given a nil reflect.Type; pass the entity's " +
				"type, for example reflect.TypeFor[User]()")
		}
		if err := validatePrefix(prefix); err != nil {
			return err
		}
		return b.register(entityKey(t), prefix)
	}}
}

// WithEntities registers types that declare their own prefix by implementing [Entity]. Pass one
// value of each type; only its type matters, and a pointer registers its element type, so
// User{}, &User{} and (*User)(nil) are equivalent.
//
// Go cannot enumerate the types of a package at run time, and the specification forbids
// discovering entities implicitly in any case, so the list you pass is the source: it is
// explicit, and nothing outside it is registered.
//
// The types are registered in ordinal order of their fully qualified name (package path, ".",
// type name), whatever order they are passed in, so a clash between two of them is reported the
// same way round on every run.
//
// Registration fails with a [*PromotedPrefixError] for a type whose IDPrefix method is promoted
// from an embedded field, with an [*InvalidPrefixError] naming the type when a declared prefix
// is invalid, and with a [*DuplicatePrefixError] exactly as [WithEntity] does.
func WithEntities(entities ...Entity) Option {
	entities = slices.Clone(entities)
	return Option{func(b *builder) error {
		type declared struct {
			t      reflect.Type
			name   string
			entity Entity
		}
		list := make([]declared, len(entities))
		for i, e := range entities {
			list[i].entity = e
			if e != nil {
				list[i].t = entityKey(reflect.TypeOf(e))
				list[i].name = qualifiedName(list[i].t)
			}
		}
		slices.SortStableFunc(list, func(a, b declared) int { return cmp.Compare(a.name, b.name) })

		for _, d := range list {
			t, prefix, err := declaredPrefix(d.entity)
			if err != nil {
				return err
			}
			if err := validatePrefix(prefix); err != nil {
				var invalid *InvalidPrefixError
				if errors.As(err, &invalid) {
					invalid.EntityType = t
				}
				return err
			}
			if err := b.register(t, prefix); err != nil {
				return err
			}
		}
		return nil
	}}
}

// qualifiedName is the sort key for WithEntities: package path, a dot, and the type name, or the
// type's string form for unnamed types.
func qualifiedName(t reflect.Type) string {
	if t.Name() == "" {
		return t.String()
	}
	return t.PkgPath() + "." + t.Name()
}

// register applies the collision rules of spec §4.3 and records the registration.
func (b *builder) register(t reflect.Type, prefix string) error {
	// Checked first because "you already registered this type" is the more useful message when
	// both problems are true.
	if existing, ok := b.prefixes[t]; ok {
		return &DuplicatePrefixError{
			Prefix:                prefix,
			ExistingPrefix:        existing,
			EntityType:            t,
			ConflictingEntityType: t,
		}
	}
	folded := foldPrefix(prefix)
	if other, ok := b.byFolded[folded]; ok {
		return &DuplicatePrefixError{
			Prefix:                prefix,
			ExistingPrefix:        b.prefixes[other],
			EntityType:            t,
			ConflictingEntityType: other,
		}
	}
	if b.prefixes == nil {
		b.prefixes = make(map[reflect.Type]string)
		b.byFolded = make(map[string]reflect.Type)
	}
	b.prefixes[t] = prefix
	b.byFolded[folded] = t
	b.order = append(b.order, registration{entityType: t, prefix: prefix})
	return nil
}
