package idorahttp_test

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idorahttp"
)

type ExampleUser struct{}

func (ExampleUser) IDPrefix() string { return "usr" }

func get(h http.Handler, target string) {
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, target, nil))
	fmt.Print(rec.Code, " ", rec.Body.String())
}

func ExampleRequireFor() {
	gen := idora.MustNew(idora.WithEntities(ExampleUser{}))

	showUser := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		id, _ := idorahttp.FromContext(r.Context(), "id") // already parsed and checked
		fmt.Fprintln(w, "user", id, "created", id.Time().Format("2006-01-02"))
	})

	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.RequireFor[ExampleUser](gen, "id")(showUser))

	get(mux, "/users/usr_01k226e0g0008j4ct4ank7f24s")
	get(mux, "/users/ord_01k226e0g0008j4ct4ank7f24s")
	// Output:
	// 200 user usr_01k226e0g0008j4ct4ank7f24s created 2025-08-07
	// 400 idora: "ord_01k226e0g0008j4ct4ank7f24s" has the prefix "ord", but the prefix "usr" was expected; prefixes are compared exactly, case included, so the identifier belongs to a different entity type
}

// Public routes often answer a malformed identifier exactly as they answer an unknown one.
func ExampleWithStatus() {
	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.Require("id", "usr", idorahttp.WithStatus(http.StatusNotFound))(
		http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "found\n") })))

	get(mux, "/users/usr_01k226e0g0008j4ct4ank7f24s")
	get(mux, "/users/not-an-id")
	// Output:
	// 200 found
	// 404 Not Found
}

func ExamplePathID() {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /orders/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := idorahttp.PathID(r, "id", "ord")
		if err != nil {
			http.Error(w, "no such order", http.StatusNotFound)
			return
		}
		fmt.Fprintln(w, "order", id.ValuePart())
	})

	get(mux, "/orders/ord_01k226e0g1zzqdvk5vnacrgxv6")
	get(mux, "/orders/ord_01K226E0G1ZZQDVK5VNACRGXV6")
	// Output:
	// 200 order 01k226e0g1zzqdvk5vnacrgxv6
	// 404 no such order
}
