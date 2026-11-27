//! idora's `serde` feature: `Id` and `TypedId` as JSON strings, validated on the way in.

use idora::{Entity, Id, Idora, TypedId};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};

const USER_ID: &str = "usr_01k226e0g0008j4ct4ank7f24s";
const ORDER_ID: &str = "ord_01k226e0g1zzqdvk5vnacrgxv6";

const VECTORS: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../../spec/test-vectors.json"
));

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

#[derive(Debug, PartialEq, Serialize, Deserialize)]
struct Order {
    id: Id,
    customer: TypedId<User>,
    parent: Option<Id>,
    related: Vec<Id>,
}

#[test]
fn an_id_is_a_json_string_both_ways() {
    let id = Id::parse(USER_ID).unwrap();
    assert_eq!(
        serde_json::to_string(&id).unwrap(),
        format!("\"{USER_ID}\"")
    );
    assert_eq!(
        serde_json::from_str::<Id>(&format!("\"{USER_ID}\"")).unwrap(),
        id
    );
    // Owned strings, as from a `Value`, take the same route.
    assert_eq!(serde_json::from_value::<Id>(json!(USER_ID)).unwrap(), id);
}

#[test]
fn a_struct_of_identifiers_round_trips() {
    let order = Order {
        id: Id::parse(ORDER_ID).unwrap(),
        customer: TypedId::parse(USER_ID).unwrap(),
        parent: None,
        related: vec![Id::parse(USER_ID).unwrap(), Id::parse(ORDER_ID).unwrap()],
    };
    let text = serde_json::to_string(&order).unwrap();
    assert_eq!(
        serde_json::from_str::<Value>(&text).unwrap(),
        json!({ "id": ORDER_ID, "customer": USER_ID, "parent": null, "related": [USER_ID, ORDER_ID] })
    );
    assert_eq!(serde_json::from_str::<Order>(&text).unwrap(), order);
}

#[test]
fn deserializing_validates_and_says_why() {
    let error = serde_json::from_str::<Id>("\"usr_01K226E0G0008J4CT4ANK7F24S\"").unwrap_err();
    assert!(
        error.to_string().contains("does not end in a valid value"),
        "{error}"
    );

    let error = serde_json::from_str::<Id>("\" usr_01k226e0g0008j4ct4ank7f24s\"").unwrap_err();
    assert!(
        error
            .to_string()
            .contains("does not begin with a valid prefix"),
        "{error}"
    );
}

#[test]
fn a_typed_id_rejects_another_entitys_identifier() {
    let error = serde_json::from_value::<TypedId<User>>(json!(ORDER_ID)).unwrap_err();
    assert!(
        error.to_string().contains("the prefix 'usr' was expected"),
        "{error}"
    );

    let body = json!({ "id": ORDER_ID, "customer": ORDER_ID, "parent": null, "related": [] });
    assert!(serde_json::from_value::<Order>(body).is_err());
}

#[test]
fn values_that_are_not_strings_are_refused_with_what_was_expected() {
    let error = serde_json::from_str::<Id>("42").unwrap_err();
    assert!(error.to_string().contains("an Idora identifier"), "{error}");
    let error = serde_json::from_str::<TypedId<User>>("null").unwrap_err();
    assert!(
        error.to_string().contains("with the prefix \"usr\""),
        "{error}"
    );
}

#[test]
fn byte_strings_are_accepted_only_when_they_are_utf8() {
    use serde::de::IntoDeserializer;
    use serde::de::value::{BytesDeserializer, Error};

    let ok: BytesDeserializer<'_, Error> = USER_ID.as_bytes().into_deserializer();
    assert_eq!(Id::deserialize(ok).unwrap(), USER_ID);
    let bad: BytesDeserializer<'_, Error> = b"usr_\xff".as_slice().into_deserializer();
    assert!(Id::deserialize(bad).is_err());
}

#[test]
fn generated_identifiers_round_trip() {
    for ids in [Idora::new(), Idora::builder().uuid_v7().build()] {
        let id = ids.generate_id_with_prefix("evt").unwrap();
        let text = serde_json::to_string(&id).unwrap();
        assert_eq!(serde_json::from_str::<Id>(&text).unwrap(), id);
    }
}

#[test]
fn every_conformance_vector_deserializes_exactly_as_it_parses() {
    let vectors: Value = serde_json::from_str(VECTORS).unwrap();
    for case in vectors["ids"]["valid"].as_array().unwrap() {
        let text = case["id"].as_str().unwrap();
        let id: Id = serde_json::from_value(case["id"].clone()).unwrap();
        assert_eq!(id, Id::parse(text).unwrap());
        assert_eq!(id.timestamp_ms(), case["timestampMs"].as_u64().unwrap());
    }
    for case in vectors["ids"]["invalid"].as_array().unwrap() {
        if case.get("expectedPrefix").is_none() {
            assert!(
                serde_json::from_value::<Id>(case["id"].clone()).is_err(),
                "{case}"
            );
        }
    }
}
