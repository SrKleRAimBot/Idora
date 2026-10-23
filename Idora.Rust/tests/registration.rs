//! Explicit registration (specification §4.1, §4.3, §5.1).

use std::any::TypeId;

use idora::{DuplicateKind, Error, Idora};

struct User;
struct Order;
struct Invoice;
struct Account;
struct Accrual;
struct Comment;

#[test]
fn a_registered_type_uses_its_prefix() {
    let ids = Idora::builder().register::<User>("usr").unwrap().build();
    assert!(ids.generate::<User>().unwrap().starts_with("usr_"));
}

#[test]
fn several_types_each_keep_their_own_prefix() {
    let ids = Idora::builder()
        .register::<User>("usr")
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .register::<Invoice>("inv")
        .unwrap()
        .build();
    assert!(ids.generate::<User>().unwrap().starts_with("usr_"));
    assert!(ids.generate::<Order>().unwrap().starts_with("ord_"));
    assert!(ids.generate::<Invoice>().unwrap().starts_with("inv_"));
}

#[test]
fn an_unregistered_type_fails_and_names_the_type() {
    let ids = Idora::builder().register::<User>("usr").unwrap().build();
    let error = ids.generate::<Comment>().unwrap_err();
    assert_eq!(error.entity_type_id(), TypeId::of::<Comment>());
    assert!(error.entity_type_name().ends_with("Comment"));
    let message = error.to_string();
    assert!(
        message.contains(std::any::type_name::<Comment>()),
        "{message}"
    );
    assert!(message.contains("register::<Comment>"), "{message}");
    assert!(message.contains("generate_with_prefix"), "{message}");
}

#[test]
fn with_nothing_registered_the_typed_form_always_fails() {
    assert!(Idora::new().generate::<User>().is_err());
    assert!(Idora::builder().build().generate::<User>().is_err());
}

#[test]
fn a_prefix_given_to_another_type_fails_and_names_both() {
    let mut builder = Idora::builder();
    builder.register::<Account>("acc").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<Accrual>("acc") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::PrefixTaken);
    assert_eq!(e.prefix(), "acc");
    assert_eq!(e.entity_type_id(), TypeId::of::<Accrual>());
    assert_eq!(e.conflicting_type_id(), TypeId::of::<Account>());
    let message = e.to_string();
    assert!(
        message.contains(std::any::type_name::<Account>()),
        "{message}"
    );
    assert!(
        message.contains(std::any::type_name::<Accrual>()),
        "{message}"
    );
    assert!(message.contains("ignoring ASCII case"), "{message}");
}

#[test]
fn prefixes_differing_only_by_case_collide() {
    for (first, second) in [("usr", "USR"), ("Acc", "aCC"), ("abc1", "ABC1")] {
        let mut builder = Idora::builder();
        builder.register::<Account>(first).unwrap();
        let Err(Error::DuplicatePrefix(e)) = builder.register::<Accrual>(second) else {
            panic!("{first}/{second} should collide")
        };
        assert_eq!(e.existing_prefix(), first);
        assert_eq!(e.prefix(), second);
    }
}

#[test]
fn the_same_type_twice_with_different_prefixes_fails() {
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<User>("person") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredWithDifferentPrefix);
    assert_eq!(e.entity_type_id(), e.conflicting_type_id());
    assert_eq!(e.existing_prefix(), "usr");
    assert_eq!(e.prefix(), "person");
    let message = e.to_string();
    assert!(
        message.contains("cannot also be registered with 'person'"),
        "{message}"
    );
    assert!(message.contains("only one prefix"), "{message}");
}

#[test]
fn the_same_type_twice_with_the_same_prefix_fails_with_its_own_message() {
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<User>("usr") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredTwice);
    let message = e.to_string();
    assert!(message.contains("registered a second time"), "{message}");
    assert!(
        !message.contains("cannot also be registered with"),
        "{message}"
    );
}

#[test]
fn the_type_check_comes_before_the_prefix_check() {
    // Both problems at once: User is registered, and "ord" belongs to Order.
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    builder.register::<Order>("ord").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<User>("ord") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredWithDifferentPrefix);
}

#[test]
fn validation_comes_before_the_duplicate_checks() {
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    assert!(matches!(
        builder.register::<User>("bad_"),
        Err(Error::InvalidPrefix(_))
    ));
}

#[test]
fn a_failed_registration_leaves_earlier_ones_intact() {
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    assert!(builder.register::<Order>("usr").is_err());
    assert!(builder.register::<Order>("").is_err());
    builder.register::<Order>("ord").unwrap();
    let ids = builder.build();
    assert_eq!(ids.get_prefix::<User>(), Some("usr"));
    assert_eq!(ids.get_prefix::<Order>(), Some("ord"));
    assert_eq!(ids.registrations().len(), 2);
}

#[test]
fn the_prefix_is_kept_exactly_as_written() {
    let ids = Idora::builder().register::<User>("UsR").unwrap().build();
    assert_eq!(ids.prefix_of::<User>().unwrap(), "UsR");
    assert!(ids.generate::<User>().unwrap().starts_with("UsR_"));
}

#[test]
fn calls_chain_and_return_the_same_builder() {
    let mut builder = Idora::builder();
    let chained: *const _ = builder.register::<User>("usr").unwrap().uuid_v7();
    assert!(std::ptr::eq(chained, &raw const builder));
}

#[test]
fn a_builder_can_build_more_than_once_and_the_generators_are_independent() {
    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    let first = builder.build();
    builder.register::<Order>("ord").unwrap();
    let second = builder.build();
    assert!(first.generate::<Order>().is_err());
    assert!(second.generate::<Order>().is_ok());
}

#[test]
fn errors_from_registration_propagate_with_the_question_mark() {
    fn configure() -> Result<Idora, Error> {
        Ok(Idora::builder()
            .register::<User>("usr")?
            .register::<Order>("usr")?
            .build())
    }
    assert!(matches!(configure(), Err(Error::DuplicatePrefix(_))));
}
