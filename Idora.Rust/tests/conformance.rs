//! Holds Idora.Rust to the shared cross-language vectors in `spec/test-vectors.json`
//! (specification §8). Every Idora package runs the same file; passing it is what lets an
//! identifier minted in one language be sorted, split and validated by another.

#[path = "support/json.rs"]
mod json;
mod support;

use idora::__private::{encode_ulid, encode_uuid_v7, ulid_step};
use idora::{DuplicateKind, Error, Idora, InvalidPrefixReason, validate_prefix};
use json::Json;
use support::hex;

const VECTORS: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../spec/test-vectors.json"
));

fn vectors() -> Json {
    Json::parse(VECTORS).unwrap_or_else(|e| panic!("spec/test-vectors.json: {e}"))
}

fn random_u80(text: &str) -> u128 {
    assert_eq!(text.len(), 20, "an 80-bit random field is 20 hex digits");
    u128::from_str_radix(text, 16).unwrap()
}

fn as_text(bytes: &[u8]) -> &str {
    std::str::from_utf8(bytes).unwrap()
}

#[test]
fn every_ulid_vector_encodes_exactly() {
    let doc = vectors();
    let cases = doc.get("ulid").as_array();
    assert_ne!(cases, []);
    for case in cases {
        let timestamp = case.get("timestampMs").as_u64();
        let random = random_u80(case.get("randomHex").as_str());
        let expected = case.get("expected").as_str();
        assert_eq!(
            as_text(&encode_ulid(timestamp, random)),
            expected,
            "ulid vector {case:?}"
        );
    }
}

#[test]
fn every_uuidv7_vector_lays_out_exactly() {
    let doc = vectors();
    let cases = doc.get("uuidv7").as_array();
    assert_ne!(cases, []);
    for case in cases {
        let timestamp = case.get("timestampMs").as_u64();
        let random: [u8; 10] = hex(case.get("randomHex").as_str()).try_into().unwrap();
        let expected = case.get("expected").as_str();
        assert_eq!(
            as_text(&encode_uuid_v7(timestamp, random)),
            expected,
            "uuidv7 vector {case:?}"
        );
    }
}

#[test]
fn every_monotonic_vector_steps_exactly() {
    const FRESH: u128 = 0x1234_5678_9abc_def0_1234;
    let doc = vectors();
    let cases = doc.get("ulidMonotonic").as_array();
    assert_ne!(cases, []);
    for case in cases {
        let description = case.get("description").as_str();
        let previous = case.get("previous");
        let expected = case.get("expected");
        let expected_random = expected.get("randomHex");

        let mut drew = false;
        let (last_ms, random) = ulid_step(
            previous.get("timestampMs").as_u64(),
            random_u80(previous.get("randomHex").as_str()),
            case.get("nowMs").as_u64(),
            || {
                drew = true;
                FRESH
            },
        );

        assert_eq!(
            last_ms,
            expected.get("timestampMs").as_u64(),
            "{description}"
        );
        if expected_random.is_null() {
            // "Freshly drawn": the value is not comparable, but it must have been drawn.
            assert!(drew, "{description}: expected a fresh random field");
            assert_eq!(random, FRESH, "{description}");
        } else {
            assert!(
                !drew,
                "{description}: drew randomness where it should have counted"
            );
            assert_eq!(
                random,
                random_u80(expected_random.as_str()),
                "{description}"
            );
        }
        if let Some(encoded) = expected.try_get("encoded") {
            assert_eq!(
                as_text(&encode_ulid(last_ms, random)),
                encoded.as_str(),
                "{description}"
            );
        }
    }
}

#[test]
fn every_valid_prefix_is_accepted_everywhere() {
    struct Entity;
    let doc = vectors();
    for prefix in doc.get("prefixes").get("valid").as_array() {
        let prefix = prefix.as_str();
        assert!(validate_prefix(prefix).is_ok(), "{prefix}");
        assert!(idora::is_valid_prefix(prefix), "{prefix}");

        let id = Idora::new().generate_with_prefix(prefix).unwrap();
        assert_eq!(support::split(&id).0, prefix);

        let ids = Idora::builder().register::<Entity>(prefix).unwrap().build();
        assert_eq!(ids.prefix_of::<Entity>().unwrap(), prefix);
    }
}

#[test]
fn every_invalid_prefix_is_rejected_for_the_stated_reason() {
    struct Entity;
    let doc = vectors();
    let cases = doc.get("prefixes").get("invalid").as_array();
    assert_ne!(cases, []);
    for case in cases {
        let prefix = case.get("prefix").as_str();
        let reason = case.get("reason").as_str();
        let index = case
            .try_get("index")
            .map(|i| usize::try_from(i.as_u64()).unwrap());

        let error = validate_prefix(prefix).unwrap_err();
        assert_eq!(error.prefix(), prefix);
        let actual = error.reason();
        match (reason, actual) {
            ("empty", InvalidPrefixReason::Empty)
            | ("tooLong", InvalidPrefixReason::TooLong { .. }) => {
                assert_eq!(index, None, "{prefix:?}");
            }
            ("separator", InvalidPrefixReason::Separator { index: i }) => {
                assert_eq!(Some(i), index, "{prefix:?}");
                assert!(error.to_string().contains("separator"), "{error}");
            }
            ("character", InvalidPrefixReason::Character { ch, index: i }) => {
                assert_eq!(Some(i), index, "{prefix:?}");
                assert_eq!(prefix.chars().nth(i), Some(ch), "{prefix:?}");
                assert!(
                    error.to_string().contains(&format!("'{ch}' at index {i}")),
                    "{error}"
                );
            }
            _ => panic!("{prefix:?}: expected {reason}, got {actual:?}"),
        }
        assert!(!idora::is_valid_prefix(prefix), "{prefix:?}");

        // The same verdict from both places a prefix is accepted.
        let adhoc = Idora::new().generate_with_prefix(prefix).unwrap_err();
        assert_eq!(adhoc.reason(), actual, "{prefix:?}");
        match Idora::builder().register::<Entity>(prefix) {
            Err(Error::InvalidPrefix(e)) => assert_eq!(e.reason(), actual, "{prefix:?}"),
            other => panic!("{prefix:?}: expected InvalidPrefix, got {other:?}"),
        }
    }
}

struct First;
struct Second;

#[test]
fn every_colliding_pair_collides() {
    let doc = vectors();
    for pair in doc.get("prefixes").get("collidingPairs").as_array() {
        let (first, second) = (pair.as_array()[0].as_str(), pair.as_array()[1].as_str());
        let mut builder = Idora::builder();
        builder.register::<First>(first).unwrap();
        match builder.register::<Second>(second) {
            Err(Error::DuplicatePrefix(e)) => {
                assert_eq!(e.kind(), DuplicateKind::PrefixTaken);
                assert_eq!(e.prefix(), second);
                assert_eq!(e.existing_prefix(), first);
                assert!(e.entity_type_name().ends_with("Second"));
                assert!(e.conflicting_type_name().ends_with("First"));
            }
            other => panic!("{first} / {second}: expected a collision, got {other:?}"),
        }
    }
}

#[test]
fn every_distinct_pair_coexists() {
    let doc = vectors();
    for pair in doc.get("prefixes").get("distinctPairs").as_array() {
        let (first, second) = (pair.as_array()[0].as_str(), pair.as_array()[1].as_str());
        let ids = Idora::builder()
            .register::<First>(first)
            .unwrap()
            .register::<Second>(second)
            .unwrap()
            .build();
        assert_eq!(ids.get_prefix::<First>(), Some(first));
        assert_eq!(ids.get_prefix::<Second>(), Some(second));
    }
}

#[test]
fn the_json_parser_handles_the_vectors_shapes() {
    let doc = Json::parse(
        r#" { "a": [1, -2, 3.5e2, true, false, null], "b": "x\u00e9\ud83d\ude00\n", "c": {} } "#,
    )
    .unwrap();
    assert_eq!(doc.get("a").as_array().len(), 6);
    assert_eq!(doc.get("a").as_array()[0].as_u64(), 1);
    assert_eq!(doc.get("b").as_str(), "x\u{e9}\u{1F600}\n");
    assert_eq!(doc.get("c"), &Json::Object(vec![]));
}

#[test]
fn the_json_parser_rejects_malformed_input() {
    for bad in [
        "",
        "{",
        "[1,]",
        "{\"a\" 1}",
        "\"abc",
        "nul",
        "[1] x",
        "\"\\ud800\"",
    ] {
        assert!(Json::parse(bad).is_err(), "{bad:?}");
    }
}
