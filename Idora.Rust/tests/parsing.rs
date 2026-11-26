//! Reading and comparing identifiers (specification §6) through every public route, beyond the
//! shared vectors in `tests/conformance.rs`.

use std::collections::{BTreeSet, HashSet};
use std::time::{Duration, UNIX_EPOCH};

use idora::{Entity, Error, Id, Idora, InvalidIdReason, InvalidPrefixReason, Strategy, TypedId};

const USER_ID: &str = "usr_01k226e0g0008j4ct4ank7f24s"; // 2025-08-07T12:00:00.000Z
const ORDER_ID: &str = "ord_01k226e0g1zzqdvk5vnacrgxv6"; // one millisecond later
const INVOICE_ID: &str = "inv_0198846702007011a233445566778899"; // the same millisecond as USER_ID

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}
struct Order;
struct Comment;

fn id(text: &str) -> Id {
    Id::parse(text).unwrap_or_else(|e| panic!("{e}"))
}

fn ids() -> Idora {
    Idora::builder()
        .register_entity::<User>()
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .build()
}

#[test]
fn generated_identifiers_parse_back_in_both_formats() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = Idora::builder()
            .strategy(strategy)
            .register_entity::<User>()
            .unwrap()
            .build();
        for _ in 0..1_000 {
            let text = ids.generate::<User>().unwrap();
            let parsed = ids.parse::<User>(&text).unwrap();
            assert_eq!(parsed, text);
            assert_eq!(parsed.format(), strategy);

            let direct = ids.generate_id::<User>().unwrap();
            assert_eq!(Id::parse(direct.as_str()), Ok(direct));
            assert_eq!(direct.format(), strategy);
        }
    }
}

#[test]
fn a_generated_id_carries_the_current_time() {
    let ids = Idora::builder().uuid_v7().build();
    let before = support_now_ms();
    let id = ids.generate_id_with_prefix("tmp").unwrap();
    let after = support_now_ms();
    assert!(before <= id.timestamp_ms() && id.timestamp_ms() <= after);
    assert_eq!(id.prefix(), "tmp");
}

fn support_now_ms() -> u64 {
    u64::try_from(
        std::time::SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis(),
    )
    .unwrap()
}

#[test]
fn ulids_generated_on_one_thread_parse_into_strictly_increasing_ids() {
    let ids = ids();
    let mut previous = ids.generate_id::<User>().unwrap();
    for _ in 0..10_000 {
        let next = ids.generate_id::<User>().unwrap();
        assert!(next.is_after(&previous), "{next} <= {previous}");
        previous = next;
    }
}

#[test]
fn generate_id_reports_the_same_errors_as_generate() {
    let ids = ids();
    let error = ids.generate_id::<Comment>().unwrap_err();
    assert!(error.entity_type_name().ends_with("Comment"));
    let error = ids.generate_id_with_prefix("bad_").unwrap_err();
    assert_eq!(error.reason(), InvalidPrefixReason::Separator { index: 3 });
}

#[test]
fn the_natural_order_interleaves_entities_and_formats_by_time() {
    let (user, order, invoice) = (id(USER_ID), id(ORDER_ID), id(INVOICE_ID));
    // "ord" < "usr" as text, but the order was made later.
    assert!(ORDER_ID < USER_ID);
    assert!(user < order);
    // The same millisecond: the value decides, and "0198…" < "01k2…".
    assert!(invoice < user);
    let sorted: Vec<Id> = BTreeSet::from([order, user, invoice]).into_iter().collect();
    assert_eq!(sorted, [invoice, user, order]);
}

#[test]
fn the_prefix_breaks_ties_last() {
    let lower = id("Usr_01k226e0g0008j4ct4ank7f24s");
    let upper = id(USER_ID);
    assert!(lower < upper, "'U' < 'u'");
    assert!(lower.same_millisecond(&upper));
    assert_ne!(lower, upper);
}

#[test]
fn timestamp_comparisons_ignore_everything_but_the_millisecond() {
    let (user, order, invoice) = (id(USER_ID), id(ORDER_ID), id(INVOICE_ID));
    assert_eq!(user.cmp_timestamp(&invoice), core::cmp::Ordering::Equal);
    assert_eq!(user.cmp_timestamp(&order), core::cmp::Ordering::Less);
    assert_eq!(order.cmp_timestamp(&user), core::cmp::Ordering::Greater);
    assert!(user.same_millisecond(&invoice));
    assert!(!user.same_millisecond(&order));
}

#[test]
fn created_before_and_after_are_strict_at_millisecond_precision() {
    let user = id(USER_ID);
    let minted = UNIX_EPOCH + Duration::from_millis(user.timestamp_ms());
    assert_eq!(user.created_at(), minted);

    for same_ms in [
        minted,
        minted + Duration::from_nanos(1),
        minted + Duration::from_micros(999),
    ] {
        assert!(!user.created_before(same_ms));
        assert!(!user.created_after(same_ms));
    }
    assert!(user.created_before(minted + Duration::from_millis(1)));
    assert!(user.created_after(minted - Duration::from_nanos(1)));
    assert!(!user.created_before(minted - Duration::from_millis(1)));
}

#[test]
fn the_epoch_and_times_before_it_compare_sensibly() {
    let epoch = id("ord_00000000000000000000000000");
    assert_eq!(epoch.created_at(), UNIX_EPOCH);
    assert!(!epoch.created_after(UNIX_EPOCH));
    // Half a millisecond before 1970 truncates to the millisecond before.
    assert!(epoch.created_after(UNIX_EPOCH - Duration::from_micros(500)));
    assert!(epoch.created_after(UNIX_EPOCH - Duration::from_secs(86_400 * 365)));
    assert!(!epoch.created_before(UNIX_EPOCH - Duration::from_secs(1)));
}

#[test]
fn has_prefix_is_exact_and_case_sensitive() {
    let user = id(USER_ID);
    assert!(user.has_prefix("usr"));
    for other in ["USR", "us", "usr_", "", "usrx"] {
        assert!(!user.has_prefix(other), "{other:?}");
    }
}

#[test]
fn equality_and_hashing_follow_the_text() {
    let a = id(USER_ID);
    let b = id(&String::from(USER_ID));
    assert_eq!(a, b);
    assert_eq!(HashSet::from([a, b, id(ORDER_ID)]).len(), 2);
    assert_eq!(a, USER_ID);
    assert_eq!(USER_ID, a);
    assert_eq!(a, String::from(USER_ID));
    assert_eq!(String::from(USER_ID), a);
    assert_eq!(*USER_ID, a);
    assert_ne!(a, "USR_01k226e0g0008j4ct4ank7f24s");
}

#[test]
fn an_id_converts_to_and_from_strings() {
    let user = Id::try_from(String::from(USER_ID)).unwrap();
    let text: String = user.into();
    assert_eq!(text, USER_ID);
    let as_ref: &str = user.as_ref();
    assert_eq!(as_ref, USER_ID);
    assert_eq!(format!("{user:?}"), format!("Id({USER_ID:?})"));
    assert_eq!(format!("{user}"), USER_ID);
}

#[test]
fn parsing_is_strict_about_case_whitespace_and_hyphens() {
    for text in [
        "usr_01K226E0G0008J4CT4ANK7F24S",
        "usr_01k226e0g0008j4ct4ank7f24s\n",
        "usr_ 01k226e0g0008j4ct4ank7f24s",
        "inv_01988467-0200-7011-a233-445566778899",
        "inv_0198846702007011A233445566778899",
    ] {
        assert_eq!(
            Id::parse(text).unwrap_err().reason(),
            &InvalidIdReason::Value,
            "{text:?}"
        );
    }
}

#[test]
fn only_the_first_underscore_separates() {
    // The prefix can never contain one, so the rest is the value, and here a malformed one.
    let error = Id::parse("usr_01k226e0g0008j4ct4ank7f24s_x").unwrap_err();
    assert_eq!(error.reason(), &InvalidIdReason::Value);
}

#[test]
fn an_overlong_prefix_is_a_prefix_failure_with_the_prefix_reason() {
    let error = Id::parse("abcdefghijklmnopq_01k226e0g0008j4ct4ank7f24s").unwrap_err();
    match error.reason() {
        InvalidIdReason::Prefix(e) => {
            assert_eq!(e.reason(), InvalidPrefixReason::TooLong { len: 17 });
        }
        other => panic!("{other:?}"),
    }
}

#[test]
fn every_message_says_what_is_wrong_and_quotes_the_text() {
    let message = |text: &str| Id::parse(text).unwrap_err().to_string();
    assert!(message("nope").contains("contains no '_' separator"));
    assert!(message("us-r_x").contains("does not begin with a valid prefix"));
    assert!(message("usr_x").contains("26-character lowercase ULID"));
    assert!(message("usr_x").contains("'usr_x'"));
    let mismatch = Id::parse_with_prefix(USER_ID, "ord")
        .unwrap_err()
        .to_string();
    assert!(mismatch.contains("the prefix 'usr'"), "{mismatch}");
    assert!(
        mismatch.contains("the prefix 'ord' was expected"),
        "{mismatch}"
    );
}

#[test]
fn messages_escape_and_truncate_hostile_input() {
    let hostile = format!("usr_\u{1b}[31m\n{}", "a".repeat(10_000));
    let message = Id::parse(&hostile).unwrap_err().to_string();
    assert!(
        !message.contains('\n') && !message.contains('\u{1b}'),
        "{message}"
    );
    assert!(message.contains("\\u{1b}[31m\\n"), "{message}");
    assert!(message.contains("truncated, 10010 characters"), "{message}");
    assert!(message.len() < 400, "{message}");
}

#[test]
fn the_prefix_error_is_the_source_and_the_umbrella_error_keeps_the_chain() {
    let error = Id::parse("us r_01k226e0g0008j4ct4ank7f24s").unwrap_err();
    let source = std::error::Error::source(&error).unwrap();
    assert!(source.to_string().contains("' ' at index 2"), "{source}");

    let umbrella: Error = error.clone().into();
    assert_eq!(umbrella.to_string(), error.to_string());
    let chained = std::error::Error::source(&umbrella).unwrap();
    assert_eq!(chained.to_string(), source.to_string());

    for no_source in [
        Id::parse("nope").unwrap_err(),
        Id::parse("usr_x").unwrap_err(),
        Id::parse_with_prefix(USER_ID, "ord").unwrap_err(),
    ] {
        assert!(std::error::Error::source(&no_source).is_none());
        assert!(std::error::Error::source(&Error::from(no_source)).is_none());
    }
}

#[test]
fn an_invalid_expected_prefix_can_never_match() {
    let error = Id::parse_with_prefix(USER_ID, "us_r").unwrap_err();
    assert_eq!(error.expected_prefix(), Some("us_r"));
}

#[test]
fn the_registry_aware_parse_reports_an_unregistered_type_before_looking_at_the_text() {
    let ids = ids();
    for text in [USER_ID, "garbage"] {
        match ids.parse::<Comment>(text) {
            Err(Error::PrefixNotRegistered(e)) => {
                assert!(e.entity_type_name().ends_with("Comment"));
            }
            other => panic!("{other:?}"),
        }
        assert!(ids.is_id_for::<Comment>(text).is_err());
    }
}

#[test]
fn the_registry_aware_parse_uses_the_registered_prefix() {
    let ids = ids();
    assert_eq!(ids.parse::<User>(USER_ID), Ok(id(USER_ID)));
    assert_eq!(ids.parse::<Order>(ORDER_ID), Ok(id(ORDER_ID)));
    assert_eq!(ids.is_id_for::<Order>(USER_ID), Ok(false));
    match ids.parse::<Order>(USER_ID) {
        Err(Error::InvalidId(e)) => {
            assert_eq!(e.text(), USER_ID);
            assert_eq!(e.expected_prefix(), Some("ord"));
        }
        other => panic!("{other:?}"),
    }
}

#[test]
fn bounds_bracket_every_identifier_minted_in_their_window() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = Idora::builder()
            .strategy(strategy)
            .register_entity::<User>()
            .unwrap()
            .build();
        let first = ids.generate_id::<User>().unwrap();
        std::thread::sleep(Duration::from_millis(3));
        let last = ids.generate_id::<User>().unwrap();
        let lower = Id::lower_bound("usr", first.created_at(), strategy).unwrap();
        let upper = Id::upper_bound("usr", last.created_at(), strategy).unwrap();
        for minted in [first, last] {
            assert!(lower.as_str() <= minted.as_str() && minted.as_str() <= upper.as_str());
        }
        // Bounds are identifiers too, and sort at the edges of their milliseconds.
        assert!(lower <= first && last <= upper);
        assert_eq!(lower.format(), strategy);

        let before = Id::upper_bound(
            "usr",
            first.created_at() - Duration::from_millis(1),
            strategy,
        );
        assert!(before.unwrap().as_str() < first.as_str());
    }
}

#[test]
fn bounds_validate_their_prefix() {
    let error = Id::lower_bound("us_r", UNIX_EPOCH, Strategy::Ulid).unwrap_err();
    assert_eq!(error.reason(), InvalidPrefixReason::Separator { index: 2 });
    assert!(Id::upper_bound("", UNIX_EPOCH, Strategy::UuidV7).is_err());
}

#[test]
fn the_largest_timestamp_is_a_valid_bound() {
    let max = UNIX_EPOCH + Duration::from_millis((1 << 48) - 1) + Duration::from_micros(999);
    assert_eq!(
        Id::upper_bound_value(max, Strategy::Ulid),
        "7zzzzzzzzzzzzzzzzzzzzzzzzz"
    );
}

#[test]
#[should_panic(expected = "between the Unix epoch")]
fn a_bound_after_the_largest_timestamp_panics() {
    let _ = Id::lower_bound_value(UNIX_EPOCH + Duration::from_millis(1 << 48), Strategy::Ulid);
}

#[test]
#[should_panic(expected = "between the Unix epoch")]
fn a_bound_before_the_epoch_panics() {
    let _ = Id::upper_bound(
        "usr",
        UNIX_EPOCH - Duration::from_nanos(1),
        Strategy::UuidV7,
    );
}

#[test]
fn a_typed_id_accepts_only_its_entitys_prefix() {
    let user: TypedId<User> = USER_ID.parse().unwrap();
    assert_eq!(user, USER_ID);
    assert_eq!(user.id(), &id(USER_ID));
    assert_eq!(user.prefix(), "usr");
    assert_eq!(Id::from(user), id(USER_ID));
    assert_eq!(user.into_id(), id(USER_ID));
    assert_eq!(String::from(user), USER_ID);
    assert_eq!(
        format!("{user} {user:?}"),
        format!("{USER_ID} TypedId({USER_ID:?})")
    );

    let error = TypedId::<User>::parse(ORDER_ID).unwrap_err();
    assert_eq!(error.expected_prefix(), Some("usr"));
    let error = TypedId::<User>::try_from(id(ORDER_ID)).unwrap_err();
    assert_eq!(error.expected_prefix(), Some("usr"));
    assert_eq!(error.text(), ORDER_ID);
    assert!(TypedId::<User>::try_from(String::from("usr_x")).is_err());
    assert!(TypedId::<User>::try_from(USER_ID).is_ok());
}

#[test]
fn a_typed_id_compares_and_hashes_like_its_id() {
    let a: TypedId<User> = USER_ID.parse().unwrap();
    let b: TypedId<User> = "usr_01k226e0g1zzqdvk5vnacrgxv6".parse().unwrap();
    assert!(a < b && a.is_before(&b));
    assert_eq!(a, id(USER_ID));
    assert_eq!(id(USER_ID), a);
    assert_eq!(HashSet::from([a, a, b]).len(), 2);
}

#[test]
fn a_typed_id_is_copy_send_and_sync_whatever_its_entity() {
    // A marker that is neither Send nor Sync nor Clone.
    struct Unshareable(#[allow(dead_code)] std::rc::Rc<()>);
    impl Entity for Unshareable {
        const PREFIX: &'static str = "uns";
    }
    fn assert_traits<T: Copy + Send + Sync + 'static>() {}
    assert_traits::<TypedId<Unshareable>>();
    assert_traits::<Id>();
    assert_eq!(size_of::<TypedId<Unshareable>>(), size_of::<Id>());
}
