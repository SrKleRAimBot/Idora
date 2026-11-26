// Package idoragorm connects Idora identifiers to GORM: a plugin that assigns primary keys when
// rows are created, and scopes that select the rows created during a span of time straight from
// the primary key index.
//
//	gen := idora.MustNew(idora.WithEntities(User{}, Order{}))
//	db, err := gorm.Open(postgres.Open(dsn))
//	if err := db.Use(idoragorm.New(gen)); err != nil { … }
//
//	u := User{Name: "Ada"}
//	db.Create(&u) // u.ID is now "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
//
//	var recent []User
//	db.Scopes(idoragorm.CreatedBetweenFor[User](gen, "", from, to)).Find(&recent)
//
// The primary key may be a string (or a type whose underlying type is string) or an [idora.ID],
// which GORM stores as a string column and scans back without any tags.
//
// This is a separate module from Idora.Go so that the core stays free of dependencies.
package idoragorm

import (
	"fmt"
	"reflect"
	"sync"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"gorm.io/gorm"
	"gorm.io/gorm/clause"
)

// callbackName is the name the plugin registers its create callback under.
const callbackName = "idora:assign_primary_key"

var idType = reflect.TypeFor[idora.ID]()

// Plugin assigns Idora identifiers to new rows. Register it with [gorm.DB.Use]. Build it with
// [New].
type Plugin struct {
	gen      *idora.Generator
	declared bool

	// prefixes caches, per model type, the outcome of checking an IDPrefix declaration for a type
	// the generator does not know: a declaredPrefix.
	prefixes sync.Map
}

type declaredPrefix struct {
	prefix string
	err    error
}

// Option configures a [Plugin].
type Option struct {
	apply func(*Plugin)
}

// WithoutDeclaredPrefixes limits the plugin to the entity types registered with the generator.
// By default a model the generator does not know, but which declares its prefix by implementing
// [idora.Entity], gets identifiers too.
func WithoutDeclaredPrefixes() Option {
	return Option{func(p *Plugin) { p.declared = false }}
}

// New returns a plugin that assigns identifiers from gen. It panics if gen is nil.
//
// When a row is created, through Create, CreateInBatches, Save or an association, the plugin
// looks at the model's primary key. If the key is a string or an [idora.ID] and is still empty,
// and the model type has a prefix, the plugin fills it with a new identifier before GORM's
// BeforeCreate hooks run, so hooks see the key and can override it. Keys that are already set
// are left alone, as are models without a prefix and keys of any other type, such as an
// auto-incrementing integer.
//
// A model type has a prefix if it was registered with gen, with [idora.WithEntity] or
// [idora.WithEntities]. Unless [WithoutDeclaredPrefixes] is given, a type that gen does not know
// but which implements [idora.Entity] also has one: its declaration is checked once, exactly as
// [idora.WithEntities] would check it alongside gen's registrations, so an invalid prefix, a
// promoted IDPrefix method or a prefix that collides with a registered one fails the create
// with the corresponding idora error instead of producing an ambiguous identifier.
func New(gen *idora.Generator, opts ...Option) *Plugin {
	if gen == nil {
		panic("idoragorm: New was given a nil *idora.Generator")
	}
	p := &Plugin{gen: gen, declared: true}
	for _, opt := range opts {
		if opt.apply != nil {
			opt.apply(p)
		}
	}
	return p
}

// Name implements [gorm.Plugin].
func (p *Plugin) Name() string { return "idora" }

// Initialize implements [gorm.Plugin] by registering the create callback.
func (p *Plugin) Initialize(db *gorm.DB) error {
	return db.Callback().Create().Before("gorm:before_create").Register(callbackName, p.assign)
}

// assign is the create callback.
func (p *Plugin) assign(db *gorm.DB) {
	stmt := db.Statement
	if db.Error != nil || stmt.Schema == nil {
		return
	}
	field := stmt.Schema.PrioritizedPrimaryField
	if field == nil || !field.Creatable {
		return
	}
	asID := field.FieldType == idType
	if !asID && field.FieldType.Kind() != reflect.String {
		return
	}
	prefix, ok, err := p.prefixFor(stmt.Schema.ModelType)
	if err != nil {
		_ = db.AddError(err)
		return
	}
	if !ok {
		return
	}

	next := func() (any, error) {
		id, err := p.gen.GenerateWithPrefix(prefix)
		if err != nil || !asID {
			return id, err
		}
		return idora.MustParse(id), nil
	}
	fill := func(row reflect.Value) {
		if _, zero := field.ValueOf(stmt.Context, row); !zero {
			return
		}
		value, err := next()
		if err == nil {
			err = field.Set(stmt.Context, row, value)
		}
		if err != nil {
			_ = db.AddError(err)
		}
	}
	// db.Model(&User{}).Create(map[string]any{...}) creates from a map, keyed by field or column
	// name: GORM accepts either, and sends both to the same column, so both are looked at. An
	// absent, nil or empty entry counts as missing, as a zero struct field does, and is removed, so
	// that the key GORM sends cannot depend on which of two entries it meets first. A supplied key
	// is kept; two different ones are an error.
	aliases := []string{field.DBName}
	if field.Name != field.DBName {
		aliases = append(aliases, field.Name)
	}
	fillMap := func(row reflect.Value) {
		keyType := row.Type().Key()
		if keyType.Kind() != reflect.String {
			return
		}
		if row.IsNil() {
			// A nil row in a slice is an empty row to GORM; give it a map to hold the key.
			if !row.CanSet() {
				_ = db.AddError(fmt.Errorf("idoragorm: cannot assign a primary key to %s in a nil map; "+
					"pass an empty map instead", stmt.Schema.ModelType))
				return
			}
			row.Set(reflect.MakeMap(row.Type()))
		}
		var supplied []reflect.Value
		var suppliedNames, emptyNames []string
		for _, name := range aliases {
			v := row.MapIndex(reflect.ValueOf(name).Convert(keyType))
			switch {
			case !v.IsValid():
			case isEmpty(v):
				emptyNames = append(emptyNames, name)
			default:
				supplied = append(supplied, v)
				suppliedNames = append(suppliedNames, name)
			}
		}
		if len(supplied) == 2 && fmt.Sprint(supplied[0].Interface()) != fmt.Sprint(supplied[1].Interface()) {
			// The values are not quoted: they may be untrusted input of any length.
			_ = db.AddError(fmt.Errorf("idoragorm: the map for %s gives the primary key twice, as %q and %q, "+
				"with different values; give it once", stmt.Schema.ModelType, suppliedNames[0], suppliedNames[1]))
			return
		}
		for _, name := range emptyNames {
			row.SetMapIndex(reflect.ValueOf(name).Convert(keyType), reflect.Value{})
		}
		if len(supplied) > 0 {
			return
		}
		value, err := next()
		if err != nil {
			_ = db.AddError(err)
			return
		}
		name := field.DBName
		if len(emptyNames) > 0 {
			name = emptyNames[0]
		}
		if v := reflect.ValueOf(value); v.Type().AssignableTo(row.Type().Elem()) {
			row.SetMapIndex(reflect.ValueOf(name).Convert(keyType), v)
		}
	}

	switch rv := stmt.ReflectValue; rv.Kind() {
	case reflect.Slice, reflect.Array:
		for i := range rv.Len() {
			switch row := reflect.Indirect(rv.Index(i)); row.Kind() {
			case reflect.Struct:
				fill(row)
			case reflect.Map:
				fillMap(row)
			}
		}
	case reflect.Struct:
		fill(rv)
	case reflect.Map:
		fillMap(rv)
	}
}

// isEmpty reports whether a map value holds no key: nil, or the zero value of what it holds,
// looking through interfaces and pointers, as a map[string]any value is an interface.
func isEmpty(v reflect.Value) bool {
	for v.IsValid() && (v.Kind() == reflect.Interface || v.Kind() == reflect.Pointer) {
		if v.IsNil() {
			return true
		}
		v = v.Elem()
	}
	return !v.IsValid() || v.IsZero()
}

// prefixFor returns the prefix for a model type: the registered one, or a checked declaration.
func (p *Plugin) prefixFor(t reflect.Type) (string, bool, error) {
	if prefix, ok := p.gen.LookupPrefix(t); ok {
		return prefix, true, nil
	}
	if !p.declared {
		return "", false, nil
	}
	if cached, ok := p.prefixes.Load(t); ok {
		d := cached.(declaredPrefix)
		return d.prefix, d.prefix != "", d.err
	}
	d := p.checkDeclaration(t)
	p.prefixes.Store(t, d)
	return d.prefix, d.prefix != "", d.err
}

// checkDeclaration validates the IDPrefix declaration of a type gen does not know by building a
// throwaway generator with gen's registrations and the declaring type: the same checks, in the
// same order, as the application would get by listing the type in idora.WithEntities.
func (p *Plugin) checkDeclaration(t reflect.Type) declaredPrefix {
	entity, ok := reflect.New(t).Interface().(idora.Entity)
	if !ok {
		return declaredPrefix{}
	}
	var opts []idora.Option
	for rt, prefix := range p.gen.Registrations() {
		opts = append(opts, idora.WithEntityType(rt, prefix))
	}
	check, err := idora.New(append(opts, idora.WithEntities(entity))...)
	if err != nil {
		return declaredPrefix{err: fmt.Errorf("idoragorm: cannot assign a primary key to %s: %w", t, err)}
	}
	prefix, _ := check.LookupPrefix(t)
	return declaredPrefix{prefix: prefix}
}

// CreatedBetween returns a scope that selects the rows whose identifier column was minted
// during [from, to], inclusive at millisecond precision, for identifiers with the given prefix
// in the given format. It compiles to
//
//	column >= lower AND column <= upper
//
// with the bounds of [idora.LowerBound] and [idora.UpperBound], so the database answers it from
// the column's index without reading a timestamp column or parsing a row. An empty column means
// the model's primary key.
//
// The bounds compare correctly only against identifiers of the same prefix and format, which is
// what a column holding one entity type contains. Use the format the rows were generated with;
// [CreatedBetweenFor] takes it from the generator.
//
// An invalid prefix, an undefined format, or a time outside the range of an identifier's
// timestamp is added to the query's errors, so the query fails rather than returning everything.
func CreatedBetween(column, prefix string, format idora.Strategy, from, to time.Time) func(*gorm.DB) *gorm.DB {
	return func(db *gorm.DB) *gorm.DB {
		lower, err := idora.LowerBound(prefix, from, format)
		if err != nil {
			_ = db.AddError(fmt.Errorf("idoragorm: CreatedBetween: %w", err))
			return db
		}
		upper, err := idora.UpperBound(prefix, to, format)
		if err != nil {
			_ = db.AddError(fmt.Errorf("idoragorm: CreatedBetween: %w", err))
			return db
		}
		col := clause.PrimaryColumn
		if column != "" {
			col = clause.Column{Name: column}
		}
		return db.Where(clause.Gte{Column: col, Value: lower.String()}).
			Where(clause.Lte{Column: col, Value: upper.String()})
	}
}

// CreatedBetweenFor is [CreatedBetween] for entity type T: the prefix is the one T was
// registered with in g, and the format is g's strategy. If T has no registered prefix, the
// [*idora.PrefixNotRegisteredError] is added to the query's errors.
func CreatedBetweenFor[T any](g *idora.Generator, column string, from, to time.Time) func(*gorm.DB) *gorm.DB {
	prefix, err := idora.PrefixOf[T](g)
	if err != nil {
		return func(db *gorm.DB) *gorm.DB {
			_ = db.AddError(fmt.Errorf("idoragorm: CreatedBetweenFor: %w", err))
			return db
		}
	}
	return CreatedBetween(column, prefix, g.Strategy(), from, to)
}

var _ gorm.Plugin = (*Plugin)(nil)
