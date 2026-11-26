//! Idora identifiers as request ids, for tower-http's `SetRequestIdLayer`.

use std::sync::Arc;

use axum::http::{HeaderValue, Request};
use idora::{Idora, InvalidPrefixError};
use tower_http::request_id::{MakeRequestId, RequestId};

/// Makes tower-http's [`SetRequestIdLayer`](tower_http::request_id::SetRequestIdLayer) stamp each
/// request with a fresh identifier such as `req_01k2f9v8ta7m3x0qbrn4dzeh5c`.
///
/// Compared with a random UUID, the request id says what it is when it turns up in a log line,
/// sorts into arrival order (strictly so for requests handled on one thread), and carries the
/// moment the request arrived. Generating one takes about 50 nanoseconds and no lock.
///
/// ```
/// use axum::{Router, routing::get};
/// use tower_http::request_id::{PropagateRequestIdLayer, SetRequestIdLayer};
/// use idora_axum::MakeIdoraRequestId;
///
/// let app: Router = Router::new()
///     .route("/", get(|| async { "hello" }))
///     .layer(PropagateRequestIdLayer::x_request_id())
///     .layer(SetRequestIdLayer::x_request_id(MakeIdoraRequestId::new("req")?));
/// # Ok::<(), idora::InvalidPrefixError>(())
/// ```
///
/// A request that already carries the header keeps its id: that is how `SetRequestIdLayer`
/// behaves, so an id assigned at the edge survives every hop.
#[derive(Clone, Debug)]
pub struct MakeIdoraRequestId {
    ids: Idora,
    prefix: Arc<str>,
}

impl MakeIdoraRequestId {
    /// Request ids with `prefix` and the default ULID value.
    ///
    /// # Errors
    ///
    /// [`InvalidPrefixError`] if `prefix` is not 1 to 16 ASCII letters or digits. It is checked
    /// once, here, rather than on every request.
    pub fn new(prefix: &str) -> Result<Self, InvalidPrefixError> {
        Self::with_generator(Idora::new(), prefix)
    }

    /// Request ids with `prefix`, in the value format `ids` is configured with.
    ///
    /// # Errors
    ///
    /// As [`new`](Self::new).
    ///
    /// ```
    /// let ids = idora::Idora::builder().uuid_v7().build();
    /// let make = idora_axum::MakeIdoraRequestId::with_generator(ids, "req")?;
    /// assert_eq!(make.prefix(), "req");
    /// # Ok::<(), idora::InvalidPrefixError>(())
    /// ```
    pub fn with_generator(ids: Idora, prefix: &str) -> Result<Self, InvalidPrefixError> {
        idora::validate_prefix(prefix)?;
        Ok(Self {
            ids,
            prefix: prefix.into(),
        })
    }

    /// The prefix every request id carries.
    #[must_use]
    pub fn prefix(&self) -> &str {
        &self.prefix
    }
}

impl MakeRequestId for MakeIdoraRequestId {
    fn make_request_id<B>(&mut self, _request: &Request<B>) -> Option<RequestId> {
        let id = self.ids.generate_id_with_prefix(&self.prefix).ok()?;
        // Identifiers are visible ASCII, which is always a valid header value.
        HeaderValue::from_str(id.as_str()).ok().map(RequestId::new)
    }
}
