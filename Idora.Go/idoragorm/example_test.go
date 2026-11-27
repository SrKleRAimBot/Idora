package idoragorm_test

import (
	"fmt"
	"os"
	"path/filepath"
	"time"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idoragorm"
	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
)

type Customer struct {
	ID   idora.ID
	Name string
}

func (Customer) IDPrefix() string { return "cus" }

func exampleDB() (*gorm.DB, func()) {
	dir, _ := os.MkdirTemp("", "idoragorm")
	db, err := gorm.Open(sqlite.Open(filepath.Join(dir, "example.db")), &gorm.Config{Logger: logger.Discard})
	if err != nil {
		panic(err)
	}
	return db, func() {
		sqlDB, _ := db.DB()
		_ = sqlDB.Close()
		_ = os.RemoveAll(dir)
	}
}

func Example() {
	gen := idora.MustNew(idora.WithEntities(Customer{}))
	db, done := exampleDB()
	defer done()

	if err := db.Use(idoragorm.New(gen)); err != nil {
		panic(err)
	}
	_ = db.AutoMigrate(&Customer{})

	c := Customer{Name: "Ada"}
	db.Create(&c)
	fmt.Println(c.ID.Prefix(), c.ID.Strategy(), time.Since(c.ID.Time()) < time.Minute)

	batch := []Customer{{Name: "Grace"}, {Name: "Edsger"}}
	db.Create(&batch)
	fmt.Println(batch[0].ID.Before(batch[1].ID))
	// Output:
	// cus ULID true
	// true
}

func ExampleCreatedBetweenFor() {
	gen := idora.MustNew(idora.WithEntities(Customer{}))
	db, done := exampleDB()
	defer done()
	_ = db.Use(idoragorm.New(gen))
	_ = db.AutoMigrate(&Customer{})

	from := time.Now()
	db.Create(&[]Customer{{Name: "Ada"}, {Name: "Grace"}})

	// The condition is a range over the primary key, so it is answered from its index.
	var recent []Customer
	db.Scopes(idoragorm.CreatedBetweenFor[Customer](gen, "", from, time.Now())).Order("id").Find(&recent)
	for _, c := range recent {
		fmt.Println(c.Name)
	}
	// Output:
	// Ada
	// Grace
}
