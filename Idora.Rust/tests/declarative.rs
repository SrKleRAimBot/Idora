//! Declarative registration through the `Entity` trait (specification §4.2, §4.3).

use std::any::{TypeId, type_name};

use idora::{DuplicateKind, Entity, Error, Idora};

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

#[derive(Clone, Copy)]
struct Order;
impl Entity for Order {
    const PREFIX: &'static str = "ord";
}

enum Invoice {}
impl Entity for Invoice {
    const PREFIX: &'static str = "inv";
}

/// Two types whose declared prefixes differ only by case, in modules chosen so that `a::Account`
/// always sorts before `b::Accrual` by type name.
mod a {
    pub struct Account;
    impl idora::Entity for Account {
        const PREFIX: &'static str = "acc";
    }
}
mod b {
    pub struct Accrual;
    impl idora::Entity for Accrual {
        const PREFIX: &'static str = "ACC";
    }
}

mod z {
    /// Sorts after everything else in this file.
    pub struct WithSeparator;
    impl idora::Entity for WithSeparator {
        const PREFIX: &'static str = "bad_prefix";
    }
}

struct Empty;
impl Entity for Empty {
    const PREFIX: &'static str = "";
}

struct Overlong;
impl Entity for Overlong {
    const PREFIX: &'static str = "abcdefghijklmnopq";
}

/// Holds an entity but declares nothing itself.
struct Wrapper<T>(T);

struct Plain;

#[test]
fn register_entity_uses_the_declared_prefix() {
    let ids = Idora::builder().register_entity::<User>().unwrap().build();
    assert!(ids.generate::<User>().unwrap().starts_with("usr_"));
    assert_eq!(ids.prefix_of::<User>().unwrap(), User::PREFIX);
}

#[test]
fn register_entities_registers_every_listed_type() {
    let ids = Idora::builder()
        .register_entities::<(User, Order, Invoice)>()
        .unwrap()
        .build();
    assert_eq!(ids.registrations().len(), 3);
    assert_eq!(ids.get_prefix::<User>(), Some("usr"));
    assert_eq!(ids.get_prefix::<Order>(), Some("ord"));
    assert_eq!(ids.get_prefix::<Invoice>(), Some("inv"));
}

#[test]
fn register_entities_registers_in_type_name_order() {
    let ids = Idora::builder()
        .register_entities::<(User, Order, Invoice)>()
        .unwrap()
        .build();
    let names: Vec<&str> = ids
        .registrations()
        .iter()
        .map(idora::Registration::type_name)
        .collect();
    let mut sorted = names.clone();
    sorted.sort_unstable();
    assert_eq!(names, sorted);
}

#[test]
fn the_empty_set_registers_nothing() {
    let ids = Idora::builder().register_entities::<()>().unwrap().build();
    assert_eq!(ids.registrations(), []);
}

#[test]
fn a_sixteen_type_set_is_supported() {
    struct T<const N: u8>;
    macro_rules! declare {
        ($($n:literal => $p:literal),*) => { $(impl Entity for T<$n> { const PREFIX: &'static str = $p; })* };
    }
    declare!(0 => "a", 1 => "b", 2 => "c", 3 => "d", 4 => "e", 5 => "f", 6 => "g", 7 => "h",
             8 => "i", 9 => "j", 10 => "k", 11 => "l", 12 => "m", 13 => "n", 14 => "o", 15 => "p");
    let ids = Idora::builder()
        .register_entities::<(
            T<0>,
            T<1>,
            T<2>,
            T<3>,
            T<4>,
            T<5>,
            T<6>,
            T<7>,
            T<8>,
            T<9>,
            T<10>,
            T<11>,
            T<12>,
            T<13>,
            T<14>,
            T<15>,
        )>()
        .unwrap()
        .build();
    assert_eq!(ids.registrations().len(), 16);
    assert_eq!(ids.get_prefix::<T<15>>(), Some("p"));
}

#[test]
fn a_declaration_is_not_inherited_by_a_wrapper() {
    let ids = Idora::builder().register_entity::<User>().unwrap().build();
    assert!(ids.generate::<Wrapper<User>>().is_err());
    let _ = Wrapper(User).0;
}

#[test]
fn types_without_a_declaration_are_untouched() {
    let ids = Idora::builder()
        .register_entities::<(User, Order)>()
        .unwrap()
        .build();
    assert_eq!(ids.get_prefix::<Plain>(), None);
}

#[test]
fn two_declarations_claiming_one_prefix_collide() {
    let Err(Error::DuplicatePrefix(e)) =
        Idora::builder().register_entities::<(a::Account, b::Accrual)>()
    else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::PrefixTaken);
    assert_eq!(e.entity_type_id(), TypeId::of::<b::Accrual>());
    assert_eq!(e.conflicting_type_id(), TypeId::of::<a::Account>());
}

#[test]
fn a_clash_is_reported_the_same_way_whatever_order_the_types_are_listed_in() {
    let forwards = Idora::builder()
        .register_entities::<(a::Account, b::Accrual)>()
        .unwrap_err();
    let backwards = Idora::builder()
        .register_entities::<(b::Accrual, a::Account)>()
        .unwrap_err();
    assert_eq!(forwards, backwards);
    assert_eq!(forwards.to_string(), backwards.to_string());
}

#[test]
fn an_invalid_declared_prefix_names_the_declaring_type() {
    for (name, result) in [
        (
            type_name::<z::WithSeparator>(),
            Idora::builder()
                .register_entity::<z::WithSeparator>()
                .map(|_| ()),
        ),
        (
            type_name::<Empty>(),
            Idora::builder().register_entity::<Empty>().map(|_| ()),
        ),
        (
            type_name::<Overlong>(),
            Idora::builder()
                .register_entities::<(Overlong,)>()
                .map(|_| ()),
        ),
    ] {
        let Err(Error::InvalidPrefix(e)) = result else {
            panic!("{name}: expected an invalid prefix")
        };
        assert_eq!(e.declaring_type_name(), Some(name));
        let message = e.to_string();
        assert!(message.contains(name), "{message}");
        assert!(message.contains("Entity::PREFIX"), "{message}");
    }
}

#[test]
fn an_invalid_declared_prefix_still_reports_the_prefix_and_reason() {
    let Err(Error::InvalidPrefix(e)) = Idora::builder().register_entity::<z::WithSeparator>()
    else {
        panic!()
    };
    assert_eq!(e.prefix(), "bad_prefix");
    assert_eq!(
        e.reason(),
        idora::InvalidPrefixReason::Separator { index: 3 }
    );
    assert!(e.to_string().contains("separator '_' at index 3"));
}

#[test]
fn a_rejected_type_leaves_earlier_registrations_from_the_same_set_intact() {
    let mut builder = Idora::builder();
    // z::WithSeparator sorts last, so User and Order are registered before it fails.
    assert!(
        builder
            .register_entities::<(z::WithSeparator, User, Order)>()
            .is_err()
    );
    let ids = builder.build();
    assert_eq!(ids.get_prefix::<User>(), Some("usr"));
    assert_eq!(ids.get_prefix::<Order>(), Some("ord"));
    assert_eq!(ids.get_prefix::<z::WithSeparator>(), None);
}

#[test]
fn declarative_then_explicit_keeps_both() {
    let ids = Idora::builder()
        .register_entity::<User>()
        .unwrap()
        .register::<Plain>("pln")
        .unwrap()
        .build();
    assert_eq!(ids.get_prefix::<User>(), Some("usr"));
    assert_eq!(ids.get_prefix::<Plain>(), Some("pln"));
}

#[test]
fn explicit_then_declarative_keeps_both() {
    let ids = Idora::builder()
        .register::<Plain>("pln")
        .unwrap()
        .register_entities::<(User, Order)>()
        .unwrap()
        .build();
    assert_eq!(ids.registrations().len(), 3);
}

#[test]
fn a_declaration_whose_prefix_is_already_taken_explicitly_collides() {
    let mut builder = Idora::builder();
    builder.register::<Plain>("USR").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register_entity::<User>() else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::PrefixTaken);
    assert_eq!(e.entity_type_id(), TypeId::of::<User>());
    assert_eq!(e.conflicting_type_id(), TypeId::of::<Plain>());
}

#[test]
fn an_explicit_prefix_already_declared_collides() {
    let mut builder = Idora::builder();
    builder.register_entity::<User>().unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<Plain>("usr") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.conflicting_type_id(), TypeId::of::<User>());
}

#[test]
fn a_declared_type_registered_by_hand_too_fails_even_with_the_same_prefix() {
    let mut builder = Idora::builder();
    builder.register_entity::<User>().unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register::<User>("usr") else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredTwice);

    let mut builder = Idora::builder();
    builder.register::<User>("usr").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register_entity::<User>() else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredTwice);
}

#[test]
fn a_declared_type_registered_by_hand_with_another_prefix_says_which_disagree() {
    let mut builder = Idora::builder();
    builder.register::<User>("person").unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register_entity::<User>() else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredWithDifferentPrefix);
    let message = e.to_string();
    assert!(message.contains("'person'"), "{message}");
    assert!(message.contains("'usr'"), "{message}");
    assert!(message.contains("only one prefix"), "{message}");
}

#[test]
fn the_same_set_registered_twice_is_a_duplicate() {
    let mut builder = Idora::builder();
    builder.register_entities::<(User, Order)>().unwrap();
    let Err(Error::DuplicatePrefix(e)) = builder.register_entities::<(User, Order)>() else {
        panic!("expected a duplicate")
    };
    assert_eq!(e.kind(), DuplicateKind::TypeRegisteredTwice);
    assert!(e.to_string().contains("register_entities"));
}

#[test]
fn a_type_listed_twice_in_one_set_is_a_duplicate() {
    assert!(matches!(
        Idora::builder().register_entities::<(User, User)>(),
        Err(Error::DuplicatePrefix(_))
    ));
}

mod with_macro {
    pub struct Customer;
    pub struct Payment;

    idora::entity! {
        Customer => "cus",
        Payment => "pay",
    }

    pub struct Single;
    idora::entity!(Single => "one");
}

#[test]
fn the_entity_macro_implements_the_trait() {
    use with_macro::{Customer, Payment, Single};
    assert_eq!(Customer::PREFIX, "cus");
    let ids = Idora::builder()
        .register_entities::<(Customer, Payment, Single)>()
        .unwrap()
        .build();
    assert!(ids.generate::<Payment>().unwrap().starts_with("pay_"));
    assert!(ids.generate::<Single>().unwrap().starts_with("one_"));
}

#[test]
fn entity_types_can_be_copy_and_uninhabited() {
    let _ = Order;
    let ids = Idora::builder()
        .register_entities::<(Order, Invoice)>()
        .unwrap()
        .build();
    assert!(ids.generate::<Invoice>().is_ok());
}
