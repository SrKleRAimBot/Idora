//! `MakeIdoraRequestId` behind tower-http's request-id layers (the `request-id` feature).

#![cfg(feature = "request-id")]

use axum::Router;
use axum::body::Body;
use axum::http::Request;
use axum::routing::get;
use idora::{Id, Idora};
use idora_axum::MakeIdoraRequestId;
use tower::ServiceExt;
use tower_http::request_id::{PropagateRequestIdLayer, SetRequestIdLayer};

#[tokio::test]
async fn request_ids_are_idora_identifiers_and_existing_ones_are_kept() {
    let app = Router::new()
        .route("/", get(|| async { "ok" }))
        .layer(PropagateRequestIdLayer::x_request_id())
        .layer(SetRequestIdLayer::x_request_id(
            MakeIdoraRequestId::new("req").unwrap(),
        ));

    let mut seen = Vec::new();
    for _ in 0..3 {
        let response = app
            .clone()
            .oneshot(Request::get("/").body(Body::empty()).unwrap())
            .await
            .unwrap();
        let header = response.headers()["x-request-id"].to_str().unwrap();
        let id = Id::parse_with_prefix(header, "req").unwrap();
        seen.push(id);
    }
    // One thread, so strictly increasing.
    assert!(seen.windows(2).all(|pair| pair[0] < pair[1]), "{seen:?}");

    let response = app
        .oneshot(
            Request::get("/")
                .header("x-request-id", "upstream-123")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.headers()["x-request-id"], "upstream-123");
}

#[test]
fn request_id_prefixes_are_validated_once_up_front() {
    assert!(MakeIdoraRequestId::new("req_id").is_err());
    let uuid =
        MakeIdoraRequestId::with_generator(Idora::builder().uuid_v7().build(), "trace").unwrap();
    assert_eq!(uuid.prefix(), "trace");
}
