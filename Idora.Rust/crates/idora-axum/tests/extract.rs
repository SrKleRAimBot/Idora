//! The extractors, driven through a real `Router` with `tower::ServiceExt::oneshot`.

use axum::Router;
use axum::body::Body;
use axum::extract::{FromRef, Path};
use axum::http::{Request, StatusCode};
use axum::routing::get;
use http_body_util::BodyExt;
use idora::{Entity, Id, Idora, TypedId};
use idora_axum::{IdFor, IdRejection, PathId};
use tower::ServiceExt;

const USER_ID: &str = "usr_01k226e0g0008j4ct4ank7f24s";
const ORDER_ID: &str = "ord_01k226e0g1zzqdvk5vnacrgxv6";

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}
struct Order;
struct Comment;

#[derive(Clone)]
struct AppState {
    ids: Idora,
}

impl FromRef<AppState> for Idora {
    fn from_ref(state: &AppState) -> Idora {
        state.ids.clone()
    }
}

fn app() -> Router {
    let ids = Idora::builder()
        .register_entity::<User>()
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .build();
    Router::new()
        .route(
            "/users/{id}",
            get(|id: IdFor<User>| async move { format!("user {id} at {}", id.timestamp_ms()) }),
        )
        .route(
            "/orders/{id}",
            get(|id: IdFor<Order>| async move { id.into_id().to_string() }),
        )
        .route(
            "/comments/{id}",
            get(|id: IdFor<Comment>| async move { id.to_string() }),
        )
        .route(
            "/any/{id}",
            get(|PathId(id): PathId| async move { id.prefix().to_owned() }),
        )
        .route(
            "/users/{id}/orders/{order_id}",
            get(|id: IdFor<User>| async move { id.to_string() }),
        )
        .route(
            "/plain",
            get(|PathId(id): PathId| async move { id.to_string() }),
        )
        .route(
            "/custom/{id}",
            get(|id: Result<IdFor<User>, IdRejection>| async move {
                match id {
                    Ok(id) => (StatusCode::OK, id.to_string()),
                    Err(e) => (StatusCode::IM_A_TEAPOT, format!("{}: {e}", e.status())),
                }
            }),
        )
        .route(
            "/typed/{user}/{order}",
            get(
                |Path((user, order)): Path<(TypedId<User>, Id)>| async move {
                    format!("{user} {order}")
                },
            ),
        )
        .with_state(AppState { ids })
}

async fn get_text(app: Router, uri: &str) -> (StatusCode, String) {
    let response = app
        .oneshot(Request::get(uri).body(Body::empty()).unwrap())
        .await
        .unwrap();
    let status = response.status();
    if status != StatusCode::OK {
        assert_eq!(
            response.headers()["content-type"],
            "text/plain; charset=utf-8",
            "{uri}"
        );
    }
    let body = response.into_body().collect().await.unwrap().to_bytes();
    (status, String::from_utf8(body.to_vec()).unwrap())
}

#[tokio::test]
async fn id_for_accepts_an_identifier_with_the_registered_prefix() {
    let (status, body) = get_text(app(), &format!("/users/{USER_ID}")).await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(body, format!("user {USER_ID} at 1754568000000"));

    let (status, body) = get_text(app(), &format!("/orders/{ORDER_ID}")).await;
    assert_eq!((status, body.as_str()), (StatusCode::OK, ORDER_ID));
}

#[tokio::test]
async fn id_for_answers_404_for_another_entitys_identifier() {
    let (status, body) = get_text(app(), &format!("/users/{ORDER_ID}")).await;
    assert_eq!(status, StatusCode::NOT_FOUND);
    assert!(body.contains("has the prefix 'ord'"), "{body}");
    assert!(body.contains("the prefix 'usr' was expected"), "{body}");

    // Prefixes are compared exactly.
    let (status, _) = get_text(app(), "/users/USR_01k226e0g0008j4ct4ank7f24s").await;
    assert_eq!(status, StatusCode::NOT_FOUND);
}

#[tokio::test]
async fn id_for_answers_400_for_text_that_is_not_an_identifier() {
    for (segment, fragment) in [
        ("42", "contains no '_' separator"),
        (
            "us-r_01k226e0g0008j4ct4ank7f24s",
            "does not begin with a valid prefix",
        ),
        (
            "usr_01K226E0G0008J4CT4ANK7F24S",
            "does not end in a valid value",
        ),
        (
            "usr_01k226e0g0008j4ct4ank7f24s%20",
            "does not end in a valid value",
        ),
    ] {
        let (status, body) = get_text(app(), &format!("/users/{segment}")).await;
        assert_eq!(status, StatusCode::BAD_REQUEST, "{segment}");
        assert!(body.contains(fragment), "{segment}: {body}");
    }
}

#[tokio::test]
async fn percent_encoded_segments_are_decoded_before_parsing() {
    let (status, body) = get_text(app(), "/orders/ord%5F01k226e0g1zzqdvk5vnacrgxv6").await;
    assert_eq!((status, body.as_str()), (StatusCode::OK, ORDER_ID));
}

#[tokio::test]
async fn the_client_text_is_escaped_in_the_body() {
    let (status, body) = get_text(app(), "/users/usr_%0Ainjected").await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(body.contains("'usr_\\ninjected'"), "{body}");
    assert!(!body.contains('\n'), "{body}");
}

#[tokio::test]
async fn an_unregistered_type_is_a_500_without_internals_in_the_body() {
    let (status, body) = get_text(app(), &format!("/comments/{USER_ID}")).await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert!(!body.contains("Comment"), "{body}");
}

#[tokio::test]
async fn path_id_accepts_any_prefix_and_rejects_malformed_text() {
    for id in [USER_ID, ORDER_ID, "inv_0198846702007011a233445566778899"] {
        let (status, body) = get_text(app(), &format!("/any/{id}")).await;
        assert_eq!(status, StatusCode::OK);
        assert_eq!(body, id.split_once('_').unwrap().0);
    }
    let (status, _) = get_text(app(), "/any/nope").await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
}

#[tokio::test]
async fn a_route_without_exactly_one_parameter_is_a_500() {
    let (status, _) = get_text(app(), &format!("/users/{USER_ID}/orders/{ORDER_ID}")).await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    let (status, _) = get_text(app(), "/plain").await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
}

#[tokio::test]
async fn the_rejection_can_be_handled_by_the_handler() {
    let (status, body) = get_text(app(), &format!("/custom/{ORDER_ID}")).await;
    assert_eq!(status, StatusCode::IM_A_TEAPOT);
    assert!(body.starts_with("404 Not Found: The identifier"), "{body}");
}

#[tokio::test]
async fn several_identifiers_work_through_path_with_the_serde_feature() {
    let (status, body) = get_text(app(), &format!("/typed/{USER_ID}/{ORDER_ID}")).await;
    assert_eq!(
        (status, body),
        (StatusCode::OK, format!("{USER_ID} {ORDER_ID}"))
    );

    let (status, body) = get_text(app(), &format!("/typed/{ORDER_ID}/{USER_ID}")).await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(body.contains("the prefix 'usr' was expected"), "{body}");
}

#[test]
fn rejections_map_to_their_statuses_and_keep_their_source() {
    let mismatch = IdRejection::from(Id::parse_with_prefix(USER_ID, "ord").unwrap_err());
    assert_eq!(mismatch.status(), StatusCode::NOT_FOUND);
    assert!(std::error::Error::source(&mismatch).is_some());

    let malformed = IdRejection::from(Id::parse("nope").unwrap_err());
    assert_eq!(malformed.status(), StatusCode::BAD_REQUEST);
    assert_eq!(malformed.to_string(), malformed.body_text());

    let unregistered = IdRejection::from(Idora::new().prefix_of::<Comment>().unwrap_err());
    assert_eq!(unregistered.status(), StatusCode::INTERNAL_SERVER_ERROR);
    // The full story is in Display, for logs, but not in the body.
    assert!(unregistered.to_string().contains("Comment"));
    assert!(!unregistered.body_text().contains("Comment"));

    let count = IdRejection::ParameterCount { count: 2 };
    assert!(count.to_string().contains("has 2"), "{count}");
    assert!(std::error::Error::source(&count).is_none());
}

#[test]
fn extractors_are_copy_and_compare_like_their_id() {
    let id = Id::parse(USER_ID).unwrap();
    let path = PathId(id);
    let copied = path;
    assert_eq!(path, copied);
    assert_eq!(path.timestamp_ms(), 1_754_568_000_000);
    assert_eq!(*path, id);
}
