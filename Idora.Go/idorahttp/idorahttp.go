// Package idorahttp reads Idora identifiers out of net/http route wildcards.
//
// Go 1.22's ServeMux matches wildcards such as {id} in a pattern and exposes them with
// [http.Request.PathValue]. This package parses them strictly into [idora.ID] values, either in
// the handler:
//
//	mux.HandleFunc("GET /users/{id}", func(w http.ResponseWriter, r *http.Request) {
//		id, err := idorahttp.PathIDFor[User](r, gen, "id")
//		if err != nil {
//			http.Error(w, err.Error(), http.StatusBadRequest)
//			return
//		}
//		…
//	})
//
// or in middleware that rejects a malformed identifier before the handler runs, and hands the
// parsed one over through the request context:
//
//	mux.Handle("GET /users/{id}", idorahttp.RequireFor[User](gen, "id")(showUser))
//
//	func showUser(w http.ResponseWriter, r *http.Request) {
//		id, _ := idorahttp.FromContext(r.Context(), "id")
//		…
//	}
//
// ServeMux sets path values when it dispatches, so the middleware must wrap the handler a
// pattern is registered with, as above, not the mux itself.
//
// The package uses only the standard library, and is part of the core Idora.Go module.
package idorahttp

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"reflect"
	"strings"

	idora "github.com/SrKleRAimBot/Idora/Idora.Go"
)

// PathID parses the path wildcard name of r as an identifier whose prefix is exactly prefix,
// with [idora.ParsePrefixed]. An absent or malformed value returns an [*idora.InvalidIDError],
// and an invalid prefix an [*idora.InvalidPrefixError].
func PathID(r *http.Request, name, prefix string) (idora.ID, error) {
	return idora.ParsePrefixed(r.PathValue(name), prefix)
}

// PathIDFor parses the path wildcard name of r as an identifier for entity type T, with
// [idora.ParseFor]. It returns an [*idora.InvalidIDError] for an absent or malformed value, and
// an [*idora.PrefixNotRegisteredError] if T has no registered prefix.
func PathIDFor[T any](r *http.Request, g *idora.Generator, name string) (idora.ID, error) {
	return idora.ParseFor[T](g, r.PathValue(name))
}

// Option configures the middleware returned by [Require] and [RequireFor].
type Option struct {
	apply func(*config)
}

type config struct {
	status  int
	onError ErrorHandler
}

// ErrorHandler writes the response for a request whose identifier was rejected. err is an
// [*idora.InvalidIDError] when the identifier is malformed or has the wrong prefix, and an
// error matching [ErrMissingWildcard] when the route has no wildcard by that name.
type ErrorHandler func(w http.ResponseWriter, r *http.Request, err error)

// WithStatus sets the status code for a malformed identifier or one with the wrong prefix. The
// default is 400 Bad Request; 404 Not Found suits public routes that should not distinguish a
// malformed identifier from one that does not exist. The response body is the error message
// for 400 and the standard status text otherwise.
//
// The status must be a 4xx code; WithStatus panics otherwise.
func WithStatus(code int) Option {
	if code < 400 || code > 499 {
		panic(fmt.Sprintf("idorahttp: WithStatus(%d): a rejected identifier is a client error, so the "+
			"status must be between 400 and 499", code))
	}
	return Option{func(c *config) { c.status = code }}
}

// WithErrorHandler replaces the default response for a rejected identifier, for example to
// write a JSON problem document. It overrides [WithStatus].
func WithErrorHandler(h ErrorHandler) Option {
	return Option{func(c *config) { c.onError = h }}
}

// ErrMissingWildcard reports middleware asked for a wildcard that the matched route pattern does
// not contain: a mistake in the program, answered with 500 Internal Server Error by default.
var ErrMissingWildcard = errors.New("idorahttp: no such wildcard in the route pattern")

// Require returns middleware that parses the path wildcard name as an identifier whose prefix is
// exactly prefix. A malformed identifier, or one with a different prefix, is answered with 400
// Bad Request (see [WithStatus] and [WithErrorHandler]) and never reaches next; a valid one is
// stored in the request context, where [FromContext] finds it.
//
// It panics if prefix is not a valid prefix, since that is a mistake in the program, and is best
// found when the routes are set up.
func Require(name, prefix string, opts ...Option) func(http.Handler) http.Handler {
	if err := idora.ValidatePrefix(prefix); err != nil {
		panic(fmt.Sprintf("idorahttp: Require(%q, %q): %v", name, prefix, err))
	}
	return middleware(name, prefix, opts)
}

// RequireFor is [Require] for entity type T: the prefix is the one T was registered with. It
// panics with an [*idora.PrefixNotRegisteredError] if T has none, so a missing registration
// stops the program while it sets up its routes rather than failing every request.
func RequireFor[T any](g *idora.Generator, name string, opts ...Option) func(http.Handler) http.Handler {
	prefix, err := g.Prefix(reflect.TypeFor[T]())
	if err != nil {
		panic(err)
	}
	return middleware(name, prefix, opts)
}

func middleware(name, prefix string, opts []Option) func(http.Handler) http.Handler {
	c := config{status: http.StatusBadRequest}
	for _, opt := range opts {
		if opt.apply != nil {
			opt.apply(&c)
		}
	}
	if c.onError == nil {
		c.onError = defaultErrorHandler(c.status)
	}
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			text := r.PathValue(name)
			id, err := idora.ParsePrefixed(text, prefix)
			if err != nil {
				if text == "" && r.Pattern != "" && !hasWildcard(r.Pattern, name) {
					err = fmt.Errorf("%w: the pattern %q has no wildcard {%s}; wrap the handler "+
						"registered with a pattern that has one", ErrMissingWildcard, r.Pattern, name)
				}
				c.onError(w, r, err)
				return
			}
			next.ServeHTTP(w, r.WithContext(NewContext(r.Context(), name, id)))
		})
	}
}

func defaultErrorHandler(status int) ErrorHandler {
	return func(w http.ResponseWriter, r *http.Request, err error) {
		switch {
		case errors.Is(err, ErrMissingWildcard):
			http.Error(w, http.StatusText(http.StatusInternalServerError), http.StatusInternalServerError)
		case status == http.StatusBadRequest:
			http.Error(w, err.Error(), status)
		default:
			http.Error(w, http.StatusText(status), status)
		}
	}
}

// hasWildcard reports whether a ServeMux pattern contains the wildcard {name} or {name...}.
func hasWildcard(pattern, name string) bool {
	return strings.Contains(pattern, "{"+name+"}") || strings.Contains(pattern, "{"+name+"...}")
}

// contextKey is the type of the single context key under which the identifiers are stored.
type contextKey struct{}

// pathIDs is what the context holds: the identifiers stored so far, by wildcard name. Each
// [NewContext] copies it, so a context never sees identifiers stored by a context derived from
// it.
type pathIDs []pathID

type pathID struct {
	name string
	id   idora.ID
}

// NewContext returns a copy of ctx that carries id under the wildcard name, as the middleware
// stores it. It is useful for testing handlers without routing a request.
func NewContext(ctx context.Context, name string, id idora.ID) context.Context {
	prev, _ := ctx.Value(contextKey{}).(pathIDs)
	next := make(pathIDs, 0, len(prev)+1)
	for _, p := range prev {
		if p.name != name {
			next = append(next, p)
		}
	}
	return context.WithValue(ctx, contextKey{}, append(next, pathID{name, id}))
}

// FromContext returns the identifier the middleware stored under the wildcard name, and whether
// there was one. Middleware for several wildcards, such as /users/{user}/orders/{order}, can be
// stacked, and each identifier is found by its own name.
func FromContext(ctx context.Context, name string) (idora.ID, bool) {
	ids, _ := ctx.Value(contextKey{}).(pathIDs)
	for _, p := range ids {
		if p.name == name {
			return p.id, true
		}
	}
	return idora.ID{}, false
}
