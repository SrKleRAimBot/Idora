//! Reading the registry back (specification §5.3).

use std::any::{TypeId, type_name};

use idora::{Error, Idora};

struct User;
struct Order;
struct Comment;

fn ids() -> Idora {
    Idora::builder()
        .register::<User>("usr")
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .build()
}

#[test]
fn prefix_of_returns_the_prefix_without_the_separator() {
    assert_eq!(ids().prefix_of::<User>().unwrap(), "usr");
}

#[test]
fn prefix_of_an_unregistered_type_fails_naming_it() {
    let error = ids().prefix_of::<Comment>().unwrap_err();
    assert_eq!(error.entity_type_id(), TypeId::of::<Comment>());
    assert_eq!(error.entity_type_name(), type_name::<Comment>());
    let as_umbrella: Error = error.into();
    assert!(matches!(as_umbrella, Error::PrefixNotRegistered(_)));
}

#[test]
fn get_prefix_returns_some_for_registered_and_none_otherwise() {
    let ids = ids();
    assert_eq!(ids.get_prefix::<Order>(), Some("ord"));
    assert_eq!(ids.get_prefix::<Comment>(), None);
}

#[test]
fn get_prefix_by_type_id_matches_the_generic_form() {
    let ids = ids();
    assert_eq!(ids.get_prefix_by_type_id(TypeId::of::<User>()), Some("usr"));
    assert_eq!(ids.get_prefix_by_type_id(TypeId::of::<Comment>()), None);
}

#[test]
fn registrations_lists_every_type_and_prefix_in_registration_order() {
    let ids = ids();
    let entries: Vec<(TypeId, &str, &str)> = ids
        .registrations()
        .iter()
        .map(|r| (r.type_id(), r.type_name(), r.prefix()))
        .collect();
    assert_eq!(
        entries,
        [
            (TypeId::of::<User>(), type_name::<User>(), "usr"),
            (TypeId::of::<Order>(), type_name::<Order>(), "ord"),
        ]
    );
}

#[test]
fn registrations_is_empty_when_nothing_was_registered() {
    assert_eq!(Idora::new().registrations(), []);
}

#[test]
fn registrations_match_the_identifiers_actually_produced() {
    let ids = ids();
    for registration in ids.registrations() {
        let id = if registration.type_id() == TypeId::of::<User>() {
            ids.generate::<User>()
        } else {
            ids.generate::<Order>()
        }
        .unwrap();
        assert_eq!(id.split_once('_').unwrap().0, registration.prefix());
    }
}

#[test]
fn a_clone_shares_the_same_registry() {
    let ids = ids();
    let clone = ids.clone();
    assert_eq!(clone.registrations(), ids.registrations());
    assert_eq!(clone.strategy(), ids.strategy());
}

#[test]
fn debug_output_shows_the_strategy_and_prefixes() {
    let text = format!("{:?}", ids());
    assert!(text.contains("Ulid"), "{text}");
    assert!(text.contains("\"usr\""), "{text}");
    assert!(text.contains("User"), "{text}");
}
