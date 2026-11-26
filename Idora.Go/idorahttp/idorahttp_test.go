package idorahttp_test

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
	"github.com/SrKleRAimBot/Idora/Idora.Go/idorahttp"
)

type (
	User    struct{}
	Order   struct{}
	Comment struct{} // never registered
)

const (
	userID  = "usr_01k226e0g0008j4ct4ank7f24s"
	orderID = "ord_01k226e0g1zzqdvk5vnacrgxv6"
)

var gen = idora.MustNew(idora.WithEntity[User]("usr"), idora.WithEntity[Order]("ord"))

// echo writes the identifiers the middleware stored, by wildcard name.
func echo(names ...string) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var parts []string
		for _, name := range names {
			id, ok := idorahttp.FromContext(r.Context(), name)
			if !ok {
				parts = append(parts, name+"=<none>")
				continue
			}
			parts = append(parts, name+"="+id.String())
		}
		_, _ = io.WriteString(w, strings.Join(parts, " "))
	})
}

func serve(t *testing.T, h http.Handler, target string) (int, string) {
	t.Helper()
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, target, nil))
	return rec.Code, strings.TrimSpace(rec.Body.String())
}

func TestPathID(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /users/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := idorahttp.PathID(r, "id", "usr")
		if err != nil {
			http.Error(w, err.Error(), http.StatusBadRequest)
			return
		}
		_, _ = io.WriteString(w, id.Prefix()+" "+id.Time().Format("2006-01-02"))
	})
	if code, body := serve(t, mux, "/users/"+userID); code != 200 || body != "usr 2025-08-07" {
		t.Errorf("valid: %d %q", code, body)
	}
	if code, body := serve(t, mux, "/users/"+orderID); code != 400 || !strings.Contains(body, `but the prefix "usr" was expected`) {
		t.Errorf("wrong prefix: %d %q", code, body)
	}
}

func TestPathIDFor(t *testing.T) {
	r := httptest.NewRequest(http.MethodGet, "/", nil)
	r.SetPathValue("id", orderID)
	if id, err := idorahttp.PathIDFor[Order](r, gen, "id"); err != nil || id.String() != orderID {
		t.Errorf("PathIDFor[Order] = %v, %v", id, err)
	}
	if _, err := idorahttp.PathIDFor[User](r, gen, "id"); !errors.Is(err, idora.ErrInvalidID) {
		t.Errorf("PathIDFor[User] on an order identifier: %v", err)
	}
	if _, err := idorahttp.PathIDFor[Comment](r, gen, "id"); !errors.Is(err, idora.ErrPrefixNotRegistered) {
		t.Errorf("PathIDFor[Comment]: %v", err)
	}
	// An absent wildcard reads as the empty string, which is not an identifier.
	if _, err := idorahttp.PathIDFor[Order](r, gen, "other"); !errors.Is(err, idora.ErrInvalidID) {
		t.Errorf("absent wildcard: %v", err)
	}
}

func TestRequire(t *testing.T) {
	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.Require("id", "usr")(echo("id")))
	mux.Handle("GET /orders/{id}", idorahttp.RequireFor[Order](gen, "id")(echo("id")))

	cases := []struct {
		target string
		code   int
		body   string
	}{
		{"/users/" + userID, 200, "id=" + userID},
		{"/orders/" + orderID, 200, "id=" + orderID},
		{"/users/" + orderID, 400, `has the prefix "ord", but the prefix "usr" was expected`},
		{"/users/USR_01k226e0g0008j4ct4ank7f24s", 400, `has the prefix "USR", but the prefix "usr" was expected`},
		{"/users/usr_nope", 400, `the value "nope" has a length of 4`},
		{"/users/42", 400, "contains no separator"},
		{"/orders/" + userID, 400, `but the prefix "ord" was expected`},
	}
	for _, c := range cases {
		code, body := serve(t, mux, c.target)
		if code != c.code {
			t.Errorf("%s: status %d, want %d (%s)", c.target, code, c.code, body)
		}
		if c.code == 200 && body != c.body {
			t.Errorf("%s: body %q, want %q", c.target, body, c.body)
		}
		if c.code == 400 && !strings.Contains(body, c.body) {
			t.Errorf("%s: body %q, want it to contain %q", c.target, body, c.body)
		}
	}
}

func TestRequireWithStatusNotFound(t *testing.T) {
	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.Require("id", "usr", idorahttp.WithStatus(http.StatusNotFound))(echo("id")))
	code, body := serve(t, mux, "/users/"+orderID)
	if code != 404 || body != "Not Found" {
		t.Errorf("got %d %q, want 404 with no detail", code, body)
	}
	if code, _ := serve(t, mux, "/users/"+userID); code != 200 {
		t.Errorf("valid identifier: %d", code)
	}
}

func TestRequireWithErrorHandler(t *testing.T) {
	handler := idorahttp.WithErrorHandler(func(w http.ResponseWriter, r *http.Request, err error) {
		var invalid *idora.InvalidIDError
		errors.As(err, &invalid)
		w.Header().Set("Content-Type", "application/problem+json")
		w.WriteHeader(http.StatusUnprocessableEntity)
		_ = json.NewEncoder(w).Encode(map[string]string{"reason": invalid.Reason.String(), "path": r.URL.Path})
	})
	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.Require("id", "usr", idorahttp.WithStatus(404), handler)(echo("id")))
	code, body := serve(t, mux, "/users/"+orderID)
	if code != 422 || body != `{"path":"/users/`+orderID+`","reason":"prefixMismatch"}` {
		t.Errorf("got %d %s", code, body)
	}
}

func TestRequireStacksForSeveralWildcards(t *testing.T) {
	users := idorahttp.RequireFor[User](gen, "user")
	orders := idorahttp.RequireFor[Order](gen, "order")
	mux := http.NewServeMux()
	mux.Handle("GET /users/{user}/orders/{order}", users(orders(echo("user", "order", "other"))))

	code, body := serve(t, mux, "/users/"+userID+"/orders/"+orderID)
	if want := "user=" + userID + " order=" + orderID + " other=<none>"; code != 200 || body != want {
		t.Errorf("got %d %q, want %q", code, body, want)
	}
	if code, _ := serve(t, mux, "/users/"+userID+"/orders/"+userID); code != 400 {
		t.Errorf("a user identifier in the order position: %d", code)
	}
}

func TestRequireReportsAMissingWildcardAsAServerError(t *testing.T) {
	var got error
	capture := idorahttp.WithErrorHandler(func(w http.ResponseWriter, r *http.Request, err error) { got = err })
	mux := http.NewServeMux()
	mux.Handle("GET /users/{id}", idorahttp.Require("user", "usr")(echo("user")))
	mux.Handle("GET /people/{id}", idorahttp.Require("user", "usr", capture)(echo("user")))

	if code, body := serve(t, mux, "/users/"+userID); code != 500 || body != "Internal Server Error" {
		t.Errorf("got %d %q, want 500", code, body)
	}
	serve(t, mux, "/people/"+userID)
	if !errors.Is(got, idorahttp.ErrMissingWildcard) || !strings.Contains(got.Error(), `the pattern "GET /people/{id}" has no wildcard {user}`) {
		t.Errorf("error handler got %v", got)
	}
}

func TestRequireAcceptsARemainderWildcard(t *testing.T) {
	mux := http.NewServeMux()
	mux.Handle("GET /files/{id...}", idorahttp.Require("id", "usr")(echo("id")))
	if code, body := serve(t, mux, "/files/"+userID); code != 200 || body != "id="+userID {
		t.Errorf("got %d %q", code, body)
	}
	if code, _ := serve(t, mux, "/files/"); code != 400 {
		t.Errorf("an empty remainder: %d, want 400", code)
	}
}

// Outside a ServeMux, whatever sets the path value is what the middleware reads.
func TestRequireWithoutAServeMux(t *testing.T) {
	h := idorahttp.Require("id", "usr")(echo("id"))
	r := httptest.NewRequest(http.MethodGet, "/", nil)
	r.SetPathValue("id", userID)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, r)
	if rec.Code != 200 || rec.Body.String() != "id="+userID {
		t.Errorf("got %d %q", rec.Code, rec.Body.String())
	}
}

func TestConstructorsPanicOnProgrammingErrors(t *testing.T) {
	cases := map[string]func(){
		"invalid prefix":    func() { idorahttp.Require("id", "us_r") },
		"unregistered type": func() { idorahttp.RequireFor[Comment](gen, "id") },
		"2xx status":        func() { idorahttp.WithStatus(200) },
		"5xx status":        func() { idorahttp.WithStatus(500) },
	}
	for name, fn := range cases {
		func() {
			defer func() {
				if recover() == nil {
					t.Errorf("%s: no panic", name)
				}
			}()
			fn()
		}()
	}
	defer func() {
		if err, _ := recover().(error); !errors.Is(err, idora.ErrPrefixNotRegistered) {
			t.Errorf("RequireFor panicked with %v, want ErrPrefixNotRegistered", err)
		}
	}()
	idorahttp.RequireFor[Comment](gen, "id")
}

func TestContext(t *testing.T) {
	ctx := context.Background()
	if _, ok := idorahttp.FromContext(ctx, "id"); ok {
		t.Error("empty context has an identifier")
	}
	u, o := idora.MustParse(userID), idora.MustParse(orderID)
	parent := idorahttp.NewContext(ctx, "id", u)
	child := idorahttp.NewContext(parent, "id", o) // replaces it in the child only
	other := idorahttp.NewContext(parent, "order", o)

	if id, ok := idorahttp.FromContext(parent, "id"); !ok || id != u {
		t.Errorf("parent: %v %v", id, ok)
	}
	if id, ok := idorahttp.FromContext(child, "id"); !ok || id != o {
		t.Errorf("child: %v %v", id, ok)
	}
	if id, ok := idorahttp.FromContext(other, "id"); !ok || id != u {
		t.Errorf("sibling lost the parent's identifier: %v %v", id, ok)
	}
	if _, ok := idorahttp.FromContext(parent, "order"); ok {
		t.Error("a derived context leaked into its parent")
	}
}
