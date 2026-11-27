# idora-axum

[axum](https://docs.rs/axum) extractors for [Idora](https://github.com/SrKleRAimBot/Idora#readme)
identifiers: the `{id}` in `/users/{id}` arrives in your handler already parsed, and already checked
to be a *user's* identifier.

```text
GET /users/usr_01k226e0g0008j4ct4ank7f24s   200  the handler runs
GET /users/ord_01k226e0g1zzqdvk5vnacrgxv6   404  that is an order's identifier
GET /users/42                               400  that is not an identifier at all
```

Part of [Idora.Rust](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.Rust). It adds no
dependencies beyond `idora` and `axum` themselves, and `tower-http` only with the `request-id`
feature.

---

## Install

```bash
cargo add idora idora-axum
```

Edition 2024, MSRV **1.85**, axum **0.8**.

| Feature | Adds |
| --- | --- |
| `request-id` | `MakeIdoraRequestId`, for tower-http's `SetRequestIdLayer` |

---

## Quick start

Put your `Idora` in the router's state, and ask for `IdFor<T>`:

```rust
use axum::{Router, routing::get};
use idora::{Entity, Idora};
use idora_axum::IdFor;

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

async fn show_user(id: IdFor<User>) -> String {
    // `id` dereferences to an idora::Id: prefix, value, format and timestamp, no parsing.
    format!("user {id}, created {:?}", id.created_at())
}

fn app() -> Result<Router, idora::Error> {
    let ids = Idora::builder().register_entity::<User>()?.build();
    Ok(Router::new()
        .route("/users/{id}", get(show_user))
        .with_state(ids))
}

fn main() {
    let _app = app().expect("prefixes are valid and unique");
}
```

`IdFor<T>` works for any registered type, including ones registered with `register::<T>("…")`
rather than through `Entity`. The prefix comes from the registry, so it is written down exactly
once, in your start-up code, and the extractor compares it exactly, case included.

The state only has to *contain* an `Idora`. `IdFor` finds it with axum's `FromRef`, so the usual
application-state struct works:

```rust
use axum::{Router, extract::FromRef, routing::delete};
use idora::Idora;
use idora_axum::IdFor;

struct Order;

#[derive(Clone)]
struct AppState {
    ids: Idora,
    // ...a database pool, configuration...
}

impl FromRef<AppState> for Idora {
    fn from_ref(state: &AppState) -> Idora {
        state.ids.clone() // a reference-count increment
    }
}

async fn cancel(order: IdFor<Order>) -> String {
    format!("cancelled {order}")
}

fn main() -> Result<(), idora::Error> {
    let ids = Idora::builder().register::<Order>("ord")?.build();
    let _app: Router = Router::new()
        .route("/orders/{id}", delete(cancel))
        .with_state(AppState { ids });
    Ok(())
}
```

Where any entity's identifier will do, an audit-log lookup say, `PathId` parses without checking
the prefix and needs no state:

```rust
use axum::{Router, routing::get};
use idora_axum::PathId;

async fn audit(PathId(id): PathId) -> String {
    format!("a {} made at {} ms", id.prefix(), id.timestamp_ms())
}

fn main() {
    let _app: Router = Router::new().route("/audit/{id}", get(audit));
}
```

---

## Rejections

| Request | Status | Why |
| --- | --- | --- |
| `/users/usr_01k226e0g0008j4ct4ank7f24s` | `200` | |
| `/users/ord_01k226e0g1zzqdvk5vnacrgxv6` | **`404 Not Found`** | A well-formed identifier of another entity: there is no user with it |
| `/users/42`, `/users/usr_01K226…`, `/users/usr_01k2…%20` | **`400 Bad Request`** | Not an identifier: parsing is strict, so no trimming and no case folding |
| `IdFor<T>` for an unregistered `T` | `500` | A bug in the program, not the request |
| A route with more or fewer than one path parameter | `500` | Likewise |

The body is plain text. For a `400` or `404` it is Idora's own message, which says what is wrong
and quotes the client's text with control characters escaped and anything past 64 characters cut
off, so it is safe to return and to log:

```text
The identifier 'ord_01k226e0g1zzqdvk5vnacrgxv6' has the prefix 'ord', but an identifier with the
prefix 'usr' was expected. Prefixes are compared exactly, case included; check that the identifier
refers to the right kind of entity.
```

A `500` body says only that the server could not check the identifier, without naming your types;
the rejection's `Display` has the details for your logs.

### Answering differently

`IdRejection` is an ordinary error, with `status()` and `body_text()`. To answer with a JSON
problem document, a redirect or anything else, take a `Result` and decide yourself:

```rust
use axum::{Json, http::StatusCode};
use idora_axum::{IdFor, IdRejection};

struct User;

async fn show_user(
    id: Result<IdFor<User>, IdRejection>,
) -> Result<String, (StatusCode, Json<Vec<String>>)> {
    let id = id.map_err(|e| (e.status(), Json(vec![e.body_text()])))?;
    Ok(id.to_string())
}

fn main() {}
```

`axum_extra::extract::WithRejection` does the same with a conversion of your choosing.

---

## Several identifiers in one route

`IdFor` and `PathId` read a route's **only** path parameter. For a route with several, turn on
`idora`'s `serde` feature and use axum's own `Path` with `idora::Id` or `idora::TypedId<E>`, which
deserialize with the same strict parse (and `TypedId<E>` with `E::PREFIX` checked):

```toml
idora = { version = "1.1", features = ["serde"] }
```

```rust
use axum::{Router, extract::Path, routing::get};
use idora::TypedId;

struct Org;
struct User;
idora::entity! { Org => "org", User => "usr" }

async fn show(Path((org, user)): Path<(TypedId<Org>, TypedId<User>)>) -> String {
    format!("{user} in {org}")
}

fn main() {
    let _app: Router = Router::new().route("/orgs/{org_id}/users/{user_id}", get(show));
}
```

A bad identifier there is axum's usual `400` path rejection, carrying the same message.

Why not pick a parameter by name or position? Because a route renamed from `{id}` to `{user_id}`,
or a parameter added in front, would silently change which segment is checked. Reading the only
parameter, and refusing anything else loudly, cannot go wrong that way.

---

## Request ids

With the `request-id` feature, `MakeIdoraRequestId` gives tower-http's request-id layers
identifiers that say what they are and sort into arrival order:

```rust
use axum::{Router, routing::get};
use idora_axum::MakeIdoraRequestId;
use tower_http::request_id::{PropagateRequestIdLayer, SetRequestIdLayer};

fn main() -> Result<(), idora::InvalidPrefixError> {
    let _app: Router = Router::new()
        .route("/", get(|| async { "hello" }))
        .layer(PropagateRequestIdLayer::x_request_id())
        .layer(SetRequestIdLayer::x_request_id(MakeIdoraRequestId::new("req")?));
    Ok(())
}
```

```text
x-request-id: req_01k2f9v8ta7m3x0qbrn4dzeh5c
```

The prefix is validated once, when the maker is built. A request that already carries an
`x-request-id` keeps it, as `SetRequestIdLayer` always does, so an id assigned at the edge survives
every hop. `MakeIdoraRequestId::with_generator(ids, "req")` uses a generator of your own, for
UUIDv7 request ids.

---

## Contributing

From `Idora.Rust/`:

```bash
cargo fmt --all --check
cargo clippy -p idora-axum --all-targets --all-features -- -D warnings
cargo test -p idora-axum --all-features
```

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/Idora.Rust/crates/idora-axum/LICENSE).
