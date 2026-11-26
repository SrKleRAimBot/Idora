//! **idora-axum**: [axum](https://docs.rs/axum) extractors for [Idora](https://docs.rs/idora)
//! identifiers.
//!
//! A route such as `/users/{id}` receives whatever text a client puts in the URL. These extractors
//! turn that segment into an [`idora::Id`] before your handler runs, and refuse the request with a
//! status and a message that say what is wrong when they cannot:
//!
//! | Extractor | Accepts | Otherwise |
//! | --- | --- | --- |
//! | [`PathId`] | any identifier | `400 Bad Request` |
//! | [`IdFor<T>`](IdFor) | an identifier with the prefix registered for `T` in the [`Idora`] in your state | `400` if malformed, `404 Not Found` if it belongs to another entity |
//!
//! ```
//! use axum::{Router, routing::get};
//! use idora::{Entity, Idora};
//! use idora_axum::IdFor;
//!
//! struct User;
//! impl Entity for User {
//!     const PREFIX: &'static str = "usr";
//! }
//!
//! async fn show_user(id: IdFor<User>) -> String {
//!     // Already parsed and checked: the prefix is "usr", and the timestamp is to hand.
//!     format!("user {id} was created at {} ms", id.timestamp_ms())
//! }
//!
//! let ids = Idora::builder().register_entity::<User>()?.build();
//! let app: Router = Router::new()
//!     .route("/users/{id}", get(show_user))
//!     .with_state(ids);
//! # Ok::<(), idora::Error>(())
//! ```
//!
//! `GET /users/usr_01k226e0g0008j4ct4ank7f24s` reaches the handler. `GET /users/ord_01k2…` is a
//! `404`, because no user can have an order's identifier, and `GET /users/42` is a `400`. The body
//! is the plain-text message of the underlying [`idora::InvalidIdError`], with the client's text
//! escaped and truncated.
//!
//! The state only needs to *contain* an [`Idora`]: `IdFor` finds it with [`FromRef`], so a larger
//! application state works as long as it implements `FromRef<AppState> for Idora` (or derives
//! `FromRef`).
//!
//! # Several identifiers in one route
//!
//! Both extractors read a route's **only** path parameter, and reject a route with several with a
//! `500` that says so: picking one by position or by name behind your back would be a bug waiting
//! to happen. For `/orgs/{org_id}/users/{user_id}`, enable `idora`'s `serde` feature and use
//! axum's own [`Path`](axum::extract::Path) with [`idora::Id`] or [`idora::TypedId`], which
//! deserialize with the same strict parse:
//!
//! ```
//! use axum::{Router, extract::Path, routing::get};
//! use idora::TypedId;
//!
//! struct Org;
//! struct User;
//! idora::entity! { Org => "org", User => "usr" }
//!
//! async fn show(Path((org, user)): Path<(TypedId<Org>, TypedId<User>)>) -> String {
//!     format!("{user} in {org}")
//! }
//!
//! let app: Router = Router::new().route("/orgs/{org_id}/users/{user_id}", get(show));
//! ```
//!
//! A malformed or mismatched identifier there is axum's usual `400` path rejection, carrying the
//! same message.
//!
//! # Customising the rejection
//!
//! [`IdRejection`] is an ordinary error type with the status it maps to. To answer differently, a
//! JSON problem document for instance, take `Result<IdFor<T>, IdRejection>` in the handler, or wrap
//! the extractor with `axum_extra::extract::WithRejection`.
//!
//! # Request ids
//!
//! With the `request-id` feature, [`MakeIdoraRequestId`] makes tower-http's `SetRequestIdLayer`
//! stamp every request with an identifier such as `req_01k2f9v8ta7m3x0qbrn4dzeh5c`: unique, and
//! sortable into arrival order in your logs.

#![cfg_attr(docsrs, feature(doc_cfg))]

use core::fmt;
use core::marker::PhantomData;
use core::ops::Deref;

use axum::extract::rejection::RawPathParamsRejection;
use axum::extract::{FromRef, FromRequestParts, RawPathParams};
use axum::http::StatusCode;
use axum::http::request::Parts;
use axum::response::{IntoResponse, Response};
use idora::{Id, Idora, InvalidIdError, InvalidIdReason, PrefixNotRegisteredError};

#[cfg(feature = "request-id")]
mod request_id;
#[cfg(feature = "request-id")]
pub use request_id::MakeIdoraRequestId;

/// Extracts the route's only path parameter as an [`Id`], whatever its prefix.
///
/// Use it where any entity's identifier is acceptable, such as an audit-log lookup. To accept
/// only one entity's identifiers, use [`IdFor`].
///
/// ```
/// use axum::{Router, routing::get};
/// use idora_axum::PathId;
///
/// async fn audit(PathId(id): PathId) -> String {
///     format!("{} created at {} ms", id.prefix(), id.timestamp_ms())
/// }
///
/// let app: Router = Router::new().route("/audit/{id}", get(audit));
/// ```
///
/// # Rejections
///
/// `400 Bad Request` if the segment is not an identifier; `500 Internal Server Error` if the route
/// does not have exactly one path parameter. See [`IdRejection`].
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct PathId(pub Id);

impl Deref for PathId {
    type Target = Id;

    fn deref(&self) -> &Id {
        &self.0
    }
}

impl<S: Send + Sync> FromRequestParts<S> for PathId {
    type Rejection = IdRejection;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        let params = RawPathParams::from_request_parts(parts, state).await?;
        let text = only_parameter(&params)?;
        Ok(Self(Id::parse(text)?))
    }
}

/// Extracts the route's only path parameter as an [`Id`] carrying the prefix registered for `T`.
///
/// The prefix is looked up in the [`Idora`] found in the router's state with [`FromRef`], and
/// compared exactly, case included, as [`Idora::parse`] does. `T` is any registered type: it
/// needs no trait of its own.
///
/// It dereferences to the [`Id`], so `id.timestamp_ms()` and `id.to_string()` work directly;
/// [`into_id`](Self::into_id) unwraps it.
///
/// ```
/// use axum::{Router, extract::FromRef, routing::delete};
/// use idora::Idora;
/// use idora_axum::IdFor;
///
/// struct Order;
///
/// #[derive(Clone)]
/// struct AppState {
///     ids: Idora,
///     // ...a database pool, configuration...
/// }
///
/// impl FromRef<AppState> for Idora {
///     fn from_ref(state: &AppState) -> Idora {
///         state.ids.clone()
///     }
/// }
///
/// async fn cancel(order: IdFor<Order>) -> String {
///     format!("cancelled {order}")
/// }
///
/// let ids = Idora::builder().register::<Order>("ord")?.build();
/// let app: Router = Router::new()
///     .route("/orders/{id}", delete(cancel))
///     .with_state(AppState { ids });
/// # Ok::<(), idora::Error>(())
/// ```
///
/// # Rejections
///
/// - `400 Bad Request` if the segment is not an identifier at all;
/// - `404 Not Found` if it is a well-formed identifier of another entity: there is no `T` with it;
/// - `500 Internal Server Error` if `T` is not registered, or the route does not have exactly one
///   path parameter. Both are mistakes in the program, not in the request.
///
/// See [`IdRejection`].
pub struct IdFor<T: ?Sized> {
    id: Id,
    // `fn(&T)` rather than `T`: the extractor owns no `T`, so it inherits none of its auto traits,
    // and `T` may be unsized, as `Builder::register` allows.
    entity: PhantomData<fn(&T)>,
}

impl<T: ?Sized> IdFor<T> {
    /// The identifier.
    #[must_use]
    pub fn id(&self) -> &Id {
        &self.id
    }

    /// Unwraps the identifier.
    #[must_use]
    pub fn into_id(self) -> Id {
        self.id
    }
}

impl<T: ?Sized> Deref for IdFor<T> {
    type Target = Id;

    fn deref(&self) -> &Id {
        &self.id
    }
}

impl<T: ?Sized> Clone for IdFor<T> {
    fn clone(&self) -> Self {
        *self
    }
}

impl<T: ?Sized> Copy for IdFor<T> {}

impl<T: ?Sized> PartialEq for IdFor<T> {
    fn eq(&self, other: &Self) -> bool {
        self.id == other.id
    }
}

impl<T: ?Sized> Eq for IdFor<T> {}

impl<T: ?Sized> fmt::Debug for IdFor<T> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_tuple("IdFor").field(&self.id.as_str()).finish()
    }
}

impl<T: ?Sized> fmt::Display for IdFor<T> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Display::fmt(&self.id, f)
    }
}

impl<T: ?Sized> From<IdFor<T>> for Id {
    fn from(id: IdFor<T>) -> Self {
        id.id
    }
}

impl<S, T> FromRequestParts<S> for IdFor<T>
where
    Idora: FromRef<S>,
    S: Send + Sync,
    T: ?Sized + 'static,
{
    type Rejection = IdRejection;

    async fn from_request_parts(parts: &mut Parts, state: &S) -> Result<Self, Self::Rejection> {
        // The registry first: an unregistered type is the program's fault, whatever the URL says.
        let ids = Idora::from_ref(state);
        let expected = ids.prefix_of::<T>()?;
        let params = RawPathParams::from_request_parts(parts, state).await?;
        let text = only_parameter(&params)?;
        Ok(Self {
            id: Id::parse_with_prefix(text, expected)?,
            entity: PhantomData,
        })
    }
}

fn only_parameter(params: &RawPathParams) -> Result<&str, IdRejection> {
    let mut iter = params.iter();
    match (iter.next(), iter.next()) {
        (Some((_, value)), None) => Ok(value),
        _ => Err(IdRejection::ParameterCount {
            count: params.iter().count(),
        }),
    }
}

/// Why [`PathId`] or [`IdFor`] refused a request, and the response it becomes.
///
/// | Variant | Status |
/// | --- | --- |
/// | [`InvalidId`](Self::InvalidId) with [`PrefixMismatch`](InvalidIdReason::PrefixMismatch) | `404 Not Found` |
/// | [`InvalidId`](Self::InvalidId), any other reason | `400 Bad Request` |
/// | [`PrefixNotRegistered`](Self::PrefixNotRegistered) | `500 Internal Server Error` |
/// | [`ParameterCount`](Self::ParameterCount) | `500 Internal Server Error` |
/// | [`Path`](Self::Path) | as axum decides: `400` for a parameter that is not UTF-8 |
///
/// The response body is plain text. For a `400` or `404` it is the [`InvalidIdError`]'s message,
/// which quotes the client's text escaped and truncated. A `500` says only that the server is at
/// fault, without naming your types; [`Display`](fmt::Display) gives the full story for your logs.
///
/// ```
/// use axum::http::StatusCode;
/// use idora::Id;
/// use idora_axum::IdRejection;
///
/// let rejection = IdRejection::from(Id::parse_with_prefix("ord_01k226e0g0008j4ct4ank7f24s", "usr").unwrap_err());
/// assert_eq!(rejection.status(), StatusCode::NOT_FOUND);
/// ```
#[derive(Debug)]
#[non_exhaustive]
pub enum IdRejection {
    /// The path parameter is not an identifier, or is one with another prefix.
    InvalidId(InvalidIdError),
    /// [`IdFor<T>`](IdFor) was used for a type that the [`Idora`] in the state does not register.
    PrefixNotRegistered(PrefixNotRegisteredError),
    /// The route does not have exactly one path parameter.
    ParameterCount {
        /// How many path parameters the matched route has.
        count: usize,
    },
    /// axum could not provide the path parameters at all.
    Path(RawPathParamsRejection),
}

impl IdRejection {
    /// The status this rejection responds with.
    #[must_use]
    pub fn status(&self) -> StatusCode {
        match self {
            Self::InvalidId(e) => match e.reason() {
                InvalidIdReason::PrefixMismatch { .. } => StatusCode::NOT_FOUND,
                _ => StatusCode::BAD_REQUEST,
            },
            Self::PrefixNotRegistered(_) | Self::ParameterCount { .. } => {
                StatusCode::INTERNAL_SERVER_ERROR
            }
            Self::Path(e) => e.status(),
        }
    }

    /// The plain-text body this rejection responds with.
    #[must_use]
    pub fn body_text(&self) -> String {
        match self {
            Self::InvalidId(e) => e.to_string(),
            Self::PrefixNotRegistered(_) | Self::ParameterCount { .. } => {
                "The server could not check the identifier in this URL.".to_owned()
            }
            Self::Path(e) => e.body_text(),
        }
    }
}

impl fmt::Display for IdRejection {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidId(e) => e.fmt(f),
            Self::PrefixNotRegistered(e) => e.fmt(f),
            Self::ParameterCount { count } => write!(
                f,
                "PathId and IdFor read a route's only path parameter, but the matched route has \
                 {count}. For a route with several identifiers, use axum's Path with idora::Id or \
                 idora::TypedId (idora's serde feature), for example Path<(Id, Id)>."
            ),
            Self::Path(e) => e.fmt(f),
        }
    }
}

impl std::error::Error for IdRejection {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::InvalidId(e) => Some(e),
            Self::PrefixNotRegistered(e) => Some(e),
            Self::ParameterCount { .. } => None,
            Self::Path(e) => Some(e),
        }
    }
}

impl IntoResponse for IdRejection {
    fn into_response(self) -> Response {
        (self.status(), self.body_text()).into_response()
    }
}

impl From<InvalidIdError> for IdRejection {
    fn from(error: InvalidIdError) -> Self {
        Self::InvalidId(error)
    }
}

impl From<PrefixNotRegisteredError> for IdRejection {
    fn from(error: PrefixNotRegisteredError) -> Self {
        Self::PrefixNotRegistered(error)
    }
}

impl From<RawPathParamsRejection> for IdRejection {
    fn from(error: RawPathParamsRejection) -> Self {
        Self::Path(error)
    }
}

// Compiles every Rust example in the README as a doctest, so the README cannot drift from the API.
// The README shows every feature, so it is only compiled with all of them.
#[cfg(all(doctest, feature = "request-id"))]
#[doc = include_str!("../README.md")]
struct ReadmeDoctests;
