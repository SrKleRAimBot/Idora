package idoragorm_test

import (
	"errors"
	"path/filepath"
	"strings"
	"testing"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idoragorm"
	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
)

// User is registered explicitly and has a string key.
type User struct {
	ID     string
	Name   string
	Orders []Order
}

// Order declares its prefix and has an idora.ID key and an idora.ID foreign key.
type Order struct {
	ID     idora.ID
	UserID string
	Total  int
}

func (Order) IDPrefix() string { return "ord" }

// Note has an auto-incrementing integer key, which the plugin leaves alone.
type Note struct {
	ID   uint
	Text string
}

// Draft has a string key but no prefix, registered or declared.
type Draft struct {
	ID   string
	Body string
}

// Account is a named string key type with a declared pointer-receiver prefix.
type (
	AccountID string
	Account   struct {
		ID    AccountID
		Owner string
	}
)

func (*Account) IDPrefix() string { return "acc" }

// Admin embeds Order, so its IDPrefix method is promoted, and must be rejected.
type Admin struct {
	Order
	Level int
}

// Impostor declares a prefix that collides with a registered one, ignoring case.
type Impostor struct{ ID string }

func (Impostor) IDPrefix() string { return "USR" }

// BadPrefix declares an invalid prefix.
type BadPrefix struct{ ID string }

func (BadPrefix) IDPrefix() string { return "bad_prefix" }

// open returns a fresh SQLite database (pure Go, no cgo) with the plugin installed.
func open(t *testing.T, gen *idora.Generator, opts ...idoragorm.Option) *gorm.DB {
	t.Helper()
	db, err := gorm.Open(sqlite.Open(filepath.Join(t.TempDir(), "test.db")), &gorm.Config{
		Logger: logger.Discard,
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := db.Use(idoragorm.New(gen, opts...)); err != nil {
		t.Fatal(err)
	}
	if err := db.AutoMigrate(&User{}, &Order{}, &Note{}, &Draft{}, &Account{}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if sqlDB, err := db.DB(); err == nil {
			_ = sqlDB.Close()
		}
	})
	return db
}

func newGen(opts ...idora.Option) *idora.Generator {
	return idora.MustNew(append([]idora.Option{idora.WithEntity[User]("usr")}, opts...)...)
}

func TestCreateAssignsARegisteredPrefix(t *testing.T) {
	db := open(t, newGen())
	u := User{Name: "Ada"}
	if err := db.Create(&u).Error; err != nil {
		t.Fatal(err)
	}
	if _, err := idora.ParsePrefixed(u.ID, "usr"); err != nil {
		t.Fatalf("assigned %q: %v", u.ID, err)
	}
	var back User
	if err := db.First(&back, "id = ?", u.ID).Error; err != nil || back.Name != "Ada" {
		t.Errorf("reading the row back: %+v, %v", back, err)
	}
}

func TestCreateAssignsAnIDField(t *testing.T) {
	db := open(t, newGen())
	o := Order{Total: 42}
	if err := db.Create(&o).Error; err != nil {
		t.Fatal(err)
	}
	if !o.ID.HasPrefix("ord") {
		t.Fatalf("assigned %q", o.ID)
	}
	// The column is text, and scans back into an idora.ID.
	var back Order
	if err := db.First(&back, "id = ?", o.ID).Error; err != nil || back != o {
		t.Errorf("reading the row back: %+v, %v (want %+v)", back, err, o)
	}
	var raw string
	if err := db.Raw("SELECT id FROM orders").Scan(&raw).Error; err != nil || raw != o.ID.String() {
		t.Errorf("stored %q, %v", raw, err)
	}
}

func TestCreateUsesTheGeneratorsStrategy(t *testing.T) {
	db := open(t, newGen(idora.WithUUIDv7()))
	u, o := User{}, Order{}
	if err := db.Create(&u).Error; err != nil {
		t.Fatal(err)
	}
	if err := db.Create(&o).Error; err != nil {
		t.Fatal(err)
	}
	if id := idora.MustParse(u.ID); id.Strategy() != idora.UUIDv7 || o.ID.Strategy() != idora.UUIDv7 {
		t.Errorf("got %s and %s, want UUIDv7 values", u.ID, o.ID)
	}
}

func TestCreateKeepsAnExistingKey(t *testing.T) {
	db := open(t, newGen())
	u := User{ID: "usr_01k226e0g0008j4ct4ank7f24s"}
	o := Order{ID: idora.MustParse("ord_01k226e0g1zzqdvk5vnacrgxv6")}
	if err := db.Create(&u).Error; err != nil || u.ID != "usr_01k226e0g0008j4ct4ank7f24s" {
		t.Errorf("string key replaced: %q, %v", u.ID, err)
	}
	if err := db.Create(&o).Error; err != nil || o.ID.String() != "ord_01k226e0g1zzqdvk5vnacrgxv6" {
		t.Errorf("ID key replaced: %q, %v", o.ID, err)
	}
}

func TestBatchCreate(t *testing.T) {
	db := open(t, newGen())

	users := make([]User, 50)
	if err := db.Create(&users).Error; err != nil {
		t.Fatal(err)
	}
	ptrs := []*Order{{Total: 1}, {Total: 2}, {ID: idora.MustParse("ord_01k226e0g1zzqdvk5vnacrgxv6")}}
	if err := db.Create(&ptrs).Error; err != nil {
		t.Fatal(err)
	}
	batched := make([]User, 25)
	if err := db.CreateInBatches(&batched, 10).Error; err != nil {
		t.Fatal(err)
	}

	seen := map[string]bool{}
	for _, u := range append(users, batched...) {
		if !idora.IsValidPrefixed(u.ID, "usr") || seen[u.ID] {
			t.Fatalf("bad or repeated key %q", u.ID)
		}
		seen[u.ID] = true
	}
	// One generator is one ULID stream, so a batch is assigned in increasing order.
	for i := 1; i < len(users); i++ {
		if users[i].ID <= users[i-1].ID {
			t.Errorf("users[%d] = %s is not after %s", i, users[i].ID, users[i-1].ID)
		}
	}
	if !ptrs[0].ID.HasPrefix("ord") || !ptrs[1].ID.HasPrefix("ord") || ptrs[2].ID.String() != "ord_01k226e0g1zzqdvk5vnacrgxv6" {
		t.Errorf("pointer batch: %v %v %v", ptrs[0].ID, ptrs[1].ID, ptrs[2].ID)
	}
	var count int64
	db.Model(&User{}).Count(&count)
	if count != 75 {
		t.Errorf("%d users stored, want 75", count)
	}
}

func TestCreateFromAMapAssignsTheKey(t *testing.T) {
	db := open(t, newGen())
	supplied := "usr_01k226e0g0008j4ct4ank7f24s"
	rows := []map[string]any{
		{"name": "Ada"},                     // no key at all
		{"name": "Grace", "id": ""},         // an empty key under the column name
		{"name": "Edsger", "ID": nil},       // a nil key under the field name
		{"name": "Barbara", "id": supplied}, // a key of its own, kept
	}
	if err := db.Model(&User{}).Create(map[string]any{"name": "Alan"}).Error; err != nil {
		t.Fatal(err)
	}
	if err := db.Model(&User{}).Create(rows).Error; err != nil {
		t.Fatal(err)
	}
	var users []User
	if err := db.Order("name").Find(&users).Error; err != nil || len(users) != 5 {
		t.Fatalf("read back %d users, %v", len(users), err)
	}
	for _, u := range users {
		if _, err := idora.ParsePrefixed(u.ID, "usr"); err != nil {
			t.Errorf("%s was stored with key %q: %v", u.Name, u.ID, err)
		}
		if u.Name == "Barbara" && u.ID != supplied {
			t.Errorf("a supplied key was replaced with %q", u.ID)
		}
	}
	for _, row := range rows[1:3] {
		if len(row) != 2 {
			t.Errorf("the key was added beside the empty one rather than in its place: %v", row)
		}
	}
}

func TestCreateFromMapsWithANilRowOrTwoNamesForTheKey(t *testing.T) {
	db := open(t, newGen())
	supplied := "usr_01k226e0g0008j4ct4ank7f24s"

	// A nil row in a batch is an empty row to GORM, and must not panic.
	rows := make([]map[string]any, 2)
	rows[1] = map[string]any{"name": "Ada"}
	if err := db.Model(&User{}).CreateInBatches(rows, 10).Error; err != nil {
		t.Fatal(err)
	}
	// An empty entry under one name must not override a key supplied under the other.
	merged := map[string]any{"name": "Grace", "ID": "", "id": supplied}
	if err := db.Model(&User{}).Create(merged).Error; err != nil {
		t.Fatal(err)
	}
	if _, empty := merged["ID"]; empty {
		t.Errorf("the empty alias was left beside the supplied key: %v", merged)
	}
	var grace User
	if err := db.First(&grace, "name = ?", "Grace").Error; err != nil || grace.ID != supplied {
		t.Errorf("stored %q, %v; want the supplied %q", grace.ID, err, supplied)
	}
	var users []User
	if err := db.Find(&users).Error; err != nil || len(users) != 3 {
		t.Fatalf("read back %d users, %v; want 3", len(users), err)
	}
	for _, u := range users {
		if _, err := idora.ParsePrefixed(u.ID, "usr"); err != nil {
			t.Errorf("%q was stored with key %q: %v", u.Name, u.ID, err)
		}
	}

	// Two different keys under the two names are a mistake, reported rather than guessed at.
	err := db.Model(&User{}).Create(map[string]any{"name": "Edsger", "ID": supplied, "id": "usr_01k226e0g1zzqdvk5vnacrgxv6"}).Error
	if err == nil || !strings.Contains(err.Error(), "gives the primary key twice") {
		t.Errorf("conflicting keys: %v", err)
	}
	// A nil map passed by value cannot be given a key.
	if err := db.Model(&User{}).Create(map[string]any(nil)).Error; err == nil || !strings.Contains(err.Error(), "nil map") {
		t.Errorf("nil map: %v", err)
	}
}

func TestCreateAssignsAssociations(t *testing.T) {
	db := open(t, newGen())
	u := User{Name: "Ada", Orders: []Order{{Total: 1}, {Total: 2}}}
	if err := db.Create(&u).Error; err != nil {
		t.Fatal(err)
	}
	for _, o := range u.Orders {
		if !o.ID.HasPrefix("ord") || o.UserID != u.ID {
			t.Errorf("order %+v of user %s", o, u.ID)
		}
	}
	var back User
	if err := db.Preload("Orders").First(&back, "id = ?", u.ID).Error; err != nil || len(back.Orders) != 2 {
		t.Errorf("preloaded %+v, %v", back, err)
	}
}

func TestSaveCreatesWithAKey(t *testing.T) {
	db := open(t, newGen())
	u := User{Name: "Ada"}
	if err := db.Save(&u).Error; err != nil || !idora.IsValidPrefixed(u.ID, "usr") {
		t.Fatalf("Save: %q, %v", u.ID, err)
	}
	first := u.ID
	u.Name = "Grace"
	if err := db.Save(&u).Error; err != nil || u.ID != first {
		t.Errorf("a second Save changed the key: %q, %v", u.ID, err)
	}
}

func TestBeforeCreateHooksSeeTheKey(t *testing.T) {
	db := open(t, newGen())
	if err := db.AutoMigrate(&Hooked{}); err != nil {
		t.Fatal(err)
	}
	h := Hooked{}
	if err := db.Create(&h).Error; err != nil {
		t.Fatal(err)
	}
	if h.SeenInHook == "" || h.SeenInHook != h.ID {
		t.Errorf("the hook saw %q, the row has %q", h.SeenInHook, h.ID)
	}
}

type Hooked struct {
	ID         string
	SeenInHook string
}

func (Hooked) IDPrefix() string { return "hk" }

func (h *Hooked) BeforeCreate(*gorm.DB) error {
	h.SeenInHook = h.ID
	return nil
}

func TestModelsWithoutAPrefixAreLeftAlone(t *testing.T) {
	db := open(t, newGen())
	n := Note{Text: "hello"}
	if err := db.Create(&n).Error; err != nil || n.ID == 0 {
		t.Errorf("auto-increment key: %d, %v", n.ID, err)
	}
	d := Draft{ID: "my-own-key"}
	if err := db.Create(&d).Error; err != nil || d.ID != "my-own-key" {
		t.Errorf("draft: %q, %v", d.ID, err)
	}
	empty := Draft{}
	if err := db.Create(&empty).Error; err != nil || empty.ID != "" {
		t.Errorf("a draft with no key was given %q, %v", empty.ID, err)
	}
}

func TestDeclaredPrefixOnANamedStringKey(t *testing.T) {
	db := open(t, newGen())
	a := Account{Owner: "Ada"}
	if err := db.Create(&a).Error; err != nil {
		t.Fatal(err)
	}
	if !idora.IsValidPrefixed(string(a.ID), "acc") {
		t.Errorf("assigned %q", a.ID)
	}
}

func TestWithoutDeclaredPrefixes(t *testing.T) {
	db := open(t, newGen(), idoragorm.WithoutDeclaredPrefixes())
	a := Account{ID: "acc_1"}
	o := Order{Total: 1}
	if err := db.Create(&a).Error; err != nil {
		t.Fatal(err)
	}
	// Order is not registered, so its key stays the zero ID, which is stored as NULL. SQLite
	// accepts a NULL in a text primary key; the point is that nothing was invented.
	_ = db.Create(&o).Error
	if !o.ID.IsZero() {
		t.Errorf("an undeclared model was given %q", o.ID)
	}
	u := User{}
	if err := db.Create(&u).Error; err != nil || !idora.IsValidPrefixed(u.ID, "usr") {
		t.Errorf("a registered model: %q, %v", u.ID, err)
	}
}

func TestBadDeclarationsFailTheCreate(t *testing.T) {
	db := open(t, newGen())
	if err := db.AutoMigrate(&Admin{}, &Impostor{}, &BadPrefix{}); err != nil {
		t.Fatal(err)
	}
	cases := []struct {
		model any
		is    error
		msg   string
	}{
		{&Admin{}, idora.ErrPromotedPrefix, "idoragorm: cannot assign a primary key to idoragorm_test.Admin"},
		{&Impostor{}, idora.ErrDuplicatePrefix, `the entity prefix "USR" is already registered for idoragorm_test.User`},
		{&BadPrefix{}, idora.ErrInvalidPrefix, "the IDPrefix method of idoragorm_test.BadPrefix"},
	}
	for _, c := range cases {
		for range 2 { // the second attempt comes from the cache
			err := db.Create(c.model).Error
			if !errors.Is(err, c.is) || !strings.Contains(err.Error(), c.msg) {
				t.Errorf("%T: %v, want %v containing %q", c.model, err, c.is, c.msg)
			}
		}
	}
	var count int64
	db.Model(&Impostor{}).Count(&count)
	if count != 0 {
		t.Errorf("%d impostors were stored", count)
	}
}

func TestNewPanicsOnANilGenerator(t *testing.T) {
	defer func() {
		if recover() == nil {
			t.Error("no panic")
		}
	}()
	idoragorm.New(nil)
}

// at returns a valid identifier minted in the millisecond of t: the lowest one, plus a variant.
func at(t *testing.T, prefix string, when time.Time, upper bool) string {
	t.Helper()
	bound := idora.LowerBound
	if upper {
		bound = idora.UpperBound
	}
	id, err := bound(prefix, when, idora.ULID)
	if err != nil {
		t.Fatal(err)
	}
	return id.String()
}

func TestCreatedBetween(t *testing.T) {
	gen := newGen()
	db := open(t, gen)
	base := time.Date(2025, 8, 7, 12, 0, 0, 0, time.UTC)
	rows := []User{
		{ID: at(t, "usr", base.Add(-time.Millisecond), true), Name: "just before"},
		{ID: at(t, "usr", base, false), Name: "first"},
		{ID: at(t, "usr", base.Add(time.Minute), false), Name: "middle"},
		{ID: at(t, "usr", base.Add(time.Hour), true), Name: "last"},
		{ID: at(t, "usr", base.Add(time.Hour+time.Millisecond), false), Name: "just after"},
		{ID: at(t, "ord", base.Add(time.Minute), false), Name: "another prefix"},
	}
	if err := db.Create(&rows).Error; err != nil {
		t.Fatal(err)
	}

	names := func(scope func(*gorm.DB) *gorm.DB) ([]string, error) {
		var got []User
		err := db.Scopes(scope).Order("id").Find(&got).Error
		var out []string
		for _, u := range got {
			out = append(out, u.Name)
		}
		return out, err
	}
	want := "first,middle,last"
	for name, scope := range map[string]func(*gorm.DB) *gorm.DB{
		"primary key":    idoragorm.CreatedBetween("", "usr", idora.ULID, base, base.Add(time.Hour)),
		"named column":   idoragorm.CreatedBetween("id", "usr", idora.ULID, base, base.Add(time.Hour)),
		"qualified":      idoragorm.CreatedBetween("users.id", "usr", idora.ULID, base, base.Add(time.Hour)),
		"for a type":     idoragorm.CreatedBetweenFor[User](gen, "", base, base.Add(time.Hour)),
		"sub-ms is lost": idoragorm.CreatedBetween("", "usr", idora.ULID, base.Add(999*time.Microsecond), base.Add(time.Hour+999*time.Microsecond)),
	} {
		got, err := names(scope)
		if err != nil || strings.Join(got, ",") != want {
			t.Errorf("%s: %v, %v; want %s", name, got, err, want)
		}
	}

	// A single millisecond.
	if got, _ := names(idoragorm.CreatedBetween("", "usr", idora.ULID, base, base)); strings.Join(got, ",") != "first" {
		t.Errorf("one millisecond: %v", got)
	}

	// The scope composes with other conditions.
	var n int64
	db.Model(&User{}).Scopes(idoragorm.CreatedBetweenFor[User](gen, "", base, base.Add(time.Hour))).Where("name <> ?", "middle").Count(&n)
	if n != 2 {
		t.Errorf("count = %d, want 2", n)
	}
}

func TestCreatedBetweenPrintsARangeCondition(t *testing.T) {
	db := open(t, newGen())
	from := time.Date(2025, 8, 7, 12, 0, 0, 0, time.UTC)
	sql := db.ToSQL(func(tx *gorm.DB) *gorm.DB {
		return tx.Model(&User{}).Scopes(idoragorm.CreatedBetween("", "usr", idora.ULID, from, from)).Find(&[]User{})
	})
	want := "SELECT * FROM `users` WHERE `users`.`id` >= \"usr_01k226e0g00000000000000000\" AND `users`.`id` <= \"usr_01k226e0g0zzzzzzzzzzzzzzzz\""
	if sql != want {
		t.Errorf("got  %s\nwant %s", sql, want)
	}
}

func TestCreatedBetweenFailsTheQuery(t *testing.T) {
	gen := newGen()
	db := open(t, gen)
	now := time.Now()
	cases := map[string]struct {
		scope func(*gorm.DB) *gorm.DB
		is    error
	}{
		"invalid prefix":    {idoragorm.CreatedBetween("", "us_r", idora.ULID, now, now), idora.ErrInvalidPrefix},
		"before the epoch":  {idoragorm.CreatedBetween("", "usr", idora.ULID, time.Time{}, now), nil},
		"after the range":   {idoragorm.CreatedBetween("", "usr", idora.ULID, now, time.Date(20000, 1, 1, 0, 0, 0, 0, time.UTC)), nil},
		"undefined format":  {idoragorm.CreatedBetween("", "usr", idora.Strategy(7), now, now), nil},
		"unregistered type": {idoragorm.CreatedBetweenFor[Draft](gen, "", now, now), idora.ErrPrefixNotRegistered},
	}
	for name, c := range cases {
		var got []User
		err := db.Scopes(c.scope).Find(&got).Error
		if err == nil || (c.is != nil && !errors.Is(err, c.is)) || !strings.HasPrefix(err.Error(), "idoragorm: CreatedBetween") {
			t.Errorf("%s: %v", name, err)
		}
	}
}
