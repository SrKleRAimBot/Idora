//! The shape of a generated identifier (specification §1, §3).

mod support;

use idora::{Idora, Strategy};
use support::{is_ulid, is_uuid_v7, split, ulid_timestamp, unix_ms, uuid_timestamp};

struct User;
struct Order;
#[derive(Clone, Copy)]
struct Point {
    _x: i32,
}
enum Status {}

fn with(strategy: Strategy) -> Idora {
    Idora::builder()
        .strategy(strategy)
        .register::<User>("usr")
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .build()
}

const BOTH: [Strategy; 2] = [Strategy::Ulid, Strategy::UuidV7];

#[test]
fn ulid_identifiers_are_prefix_underscore_and_26_base32_characters() {
    let id = with(Strategy::Ulid).generate::<User>().unwrap();
    let (prefix, value) = split(&id);
    assert_eq!(prefix, "usr");
    assert!(is_ulid(value), "{id}");
    assert_eq!(id.len(), 30);
}

#[test]
fn uuid_v7_identifiers_are_prefix_underscore_and_32_hex_characters() {
    let id = with(Strategy::UuidV7).generate::<User>().unwrap();
    let (prefix, value) = split(&id);
    assert_eq!(prefix, "usr");
    assert!(is_uuid_v7(value), "{id}");
    assert_eq!(id.len(), 36);
}

#[test]
fn values_are_lowercase_ascii_for_either_strategy() {
    for strategy in BOTH {
        let ids = with(strategy);
        for _ in 0..1_000 {
            let id = ids.generate::<User>().unwrap();
            assert!(id.is_ascii());
            assert_eq!(id, id.to_ascii_lowercase(), "{id}");
        }
    }
}

#[test]
fn identifiers_contain_exactly_one_separator() {
    for strategy in BOTH {
        let id = with(strategy).generate::<Order>().unwrap();
        assert_eq!(id.matches('_').count(), 1, "{id}");
    }
}

#[test]
fn each_type_gets_its_own_prefix() {
    for strategy in BOTH {
        let ids = with(strategy);
        assert_eq!(split(&ids.generate::<User>().unwrap()).0, "usr");
        assert_eq!(split(&ids.generate::<Order>().unwrap()).0, "ord");
    }
}

#[test]
fn ulid_timestamp_characters_encode_the_current_time() {
    let before = unix_ms();
    let id = with(Strategy::Ulid).generate::<User>().unwrap();
    let after = unix_ms();
    let stamped = ulid_timestamp(split(&id).1);
    assert!(
        before <= stamped && stamped <= after,
        "{before} <= {stamped} <= {after}"
    );
}

#[test]
fn uuid_v7_timestamp_octets_encode_the_current_time() {
    let before = unix_ms();
    let id = with(Strategy::UuidV7).generate::<User>().unwrap();
    let after = unix_ms();
    let stamped = uuid_timestamp(split(&id).1);
    assert!(
        before <= stamped && stamped <= after,
        "{before} <= {stamped} <= {after}"
    );
}

#[test]
fn uuid_v7_sets_the_version_and_variant_bits_of_rfc_9562() {
    let ids = with(Strategy::UuidV7);
    for _ in 0..1_000 {
        let id = ids.generate::<User>().unwrap();
        let bytes = support::hex(split(&id).1);
        assert_eq!(bytes[6] >> 4, 0x7, "{id}");
        assert_eq!(bytes[8] >> 6, 0b10, "{id}");
    }
}

#[test]
fn uuid_v7_values_parse_as_a_hyphenated_uuid_once_hyphens_are_inserted() {
    let id = with(Strategy::UuidV7).generate::<User>().unwrap();
    let v = split(&id).1;
    let hyphenated = format!(
        "{}-{}-{}-{}-{}",
        &v[..8],
        &v[8..12],
        &v[12..16],
        &v[16..20],
        &v[20..]
    );
    assert_eq!(hyphenated.len(), 36);
    assert_eq!(hyphenated.replace('-', ""), v);
    assert_eq!(hyphenated.as_bytes()[14], b'7');
}

#[test]
fn ulid_random_field_varies_within_one_millisecond() {
    let ids = with(Strategy::Ulid);
    let a = ids.generate::<User>().unwrap();
    let b = ids.generate::<User>().unwrap();
    assert_ne!(&split(&a).1[10..], &split(&b).1[10..]);
}

#[test]
fn the_default_strategy_is_ulid() {
    assert_eq!(Strategy::default(), Strategy::Ulid);
    assert_eq!(Idora::new().strategy(), Strategy::Ulid);
    assert_eq!(Idora::default().strategy(), Strategy::Ulid);
    let id = Idora::builder()
        .register::<User>("usr")
        .unwrap()
        .build()
        .generate::<User>()
        .unwrap();
    assert!(is_ulid(split(&id).1));
}

#[test]
fn ulid_method_is_the_same_as_the_default() {
    let ids = Idora::builder().uuid_v7().ulid().build();
    assert_eq!(ids.strategy(), Strategy::Ulid);
    assert!(is_ulid(split(&ids.generate_with_prefix("a").unwrap()).1));
}

#[test]
fn any_static_type_can_be_a_key_including_structs_enums_and_unsized_types() {
    let ids = Idora::builder()
        .register::<Point>("pt")
        .unwrap()
        .register::<Status>("st")
        .unwrap()
        .register::<str>("text")
        .unwrap()
        .register::<dyn std::any::Any>("any")
        .unwrap()
        .build();
    assert!(ids.generate::<Point>().unwrap().starts_with("pt_"));
    assert!(ids.generate::<Status>().unwrap().starts_with("st_"));
    assert!(ids.generate::<str>().unwrap().starts_with("text_"));
    assert!(
        ids.generate::<dyn std::any::Any>()
            .unwrap()
            .starts_with("any_")
    );
}

#[test]
fn an_uppercase_prefix_is_emitted_unchanged() {
    let ids = Idora::builder().register::<User>("USR").unwrap().build();
    assert!(ids.generate::<User>().unwrap().starts_with("USR_"));
    assert!(
        Idora::new()
            .generate_with_prefix("Tmp")
            .unwrap()
            .starts_with("Tmp_")
    );
}

#[test]
fn value_len_matches_what_is_generated() {
    for strategy in BOTH {
        let id = with(strategy).generate::<User>().unwrap();
        assert_eq!(split(&id).1.len(), strategy.value_len());
    }
}
