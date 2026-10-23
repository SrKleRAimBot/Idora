//! Declarative registration (specification §4.2): a type declares its own prefix.

#![forbid(unsafe_code)]

use core::any::{TypeId, type_name};

use crate::builder::Builder;
use crate::error::{Error, TypeInfo};

/// A type that declares the prefix its identifiers carry.
///
/// This is the alternative to [`Builder::register`]: the prefix sits next to the type it
/// describes instead of in a wall of registrations at start-up. Implementing the trait does
/// nothing by itself; you still name the types when building the generator, with
/// [`Builder::register_entity`] or [`Builder::register_entities`]. The two styles mix freely, and
/// a prefix still cannot be claimed twice.
///
/// The prefix is checked when the type is registered, and an invalid one produces an
/// [`InvalidPrefixError`](crate::InvalidPrefixError) that names the type. To check it at compile
/// time instead, implement the trait with the [`entity!`](crate::entity) macro.
///
/// Rust has no inheritance, so a declaration is never inherited (§4.2) by construction: each type
/// implements `Entity` itself, and a wrapper or newtype around an entity has no prefix until it
/// declares its own.
///
/// ```
/// use idora::{Entity, Idora};
///
/// struct User;
/// impl Entity for User {
///     const PREFIX: &'static str = "usr";
/// }
///
/// let ids = Idora::builder().register_entity::<User>()?.build();
/// assert!(ids.generate::<User>()?.starts_with("usr_"));
/// # Ok::<(), idora::Error>(())
/// ```
pub trait Entity: 'static {
    /// The prefix: 1 to 16 ASCII letters or digits. Lowercase reads best, but the prefix is
    /// emitted exactly as written.
    const PREFIX: &'static str;
}

/// Implements [`Entity`] for one or more types and checks each prefix **at compile time**.
///
/// ```
/// struct User;
/// struct Order;
///
/// idora::entity! {
///     User => "usr",
///     Order => "ord",
/// }
///
/// let ids = idora::Idora::builder()
///     .register_entities::<(User, Order)>()?
///     .build();
/// assert!(ids.generate::<Order>()?.starts_with("ord_"));
/// # Ok::<(), idora::Error>(())
/// ```
///
/// An invalid prefix is a compile error rather than a start-up failure:
///
/// ```compile_fail
/// struct User;
/// idora::entity!(User => "user_account");
/// ```
///
/// The types must be concrete (a generic type has no single prefix to check). For a generic type,
/// implement [`Entity`] by hand.
#[macro_export]
macro_rules! entity {
    ($($ty:ty => $prefix:expr),+ $(,)?) => {
        $(
            impl $crate::Entity for $ty {
                const PREFIX: &'static str = $prefix;
            }

            const _: () = ::core::assert!(
                $crate::is_valid_prefix(<$ty as $crate::Entity>::PREFIX),
                ::core::concat!(
                    "invalid Idora prefix for `",
                    ::core::stringify!($ty),
                    "`: a prefix must be 1 to 16 ASCII letters or digits, with no `_`",
                ),
            );
        )+
    };
}

/// A fixed list of [`Entity`] types, registered together by [`Builder::register_entities`].
///
/// Implemented for tuples of up to 16 entity types, such as `(User, Order, Invoice)`, and for `()`.
/// The trait is sealed; for more than 16 types, call `register_entities` more than once.
pub trait EntitySet: sealed::Sealed {
    #[doc(hidden)]
    fn __register_into(builder: &mut Builder) -> Result<(), Error>;
}

mod sealed {
    pub trait Sealed {}
}

/// One type's declaration, captured so a set can be sorted before it is registered.
pub(crate) struct Declaration {
    pub(crate) entity: TypeInfo,
    pub(crate) prefix: &'static str,
}

impl Declaration {
    pub(crate) fn of<T: Entity>() -> Self {
        Self {
            entity: TypeInfo {
                id: TypeId::of::<T>(),
                name: type_name::<T>(),
            },
            prefix: T::PREFIX,
        }
    }
}

impl sealed::Sealed for () {}

impl EntitySet for () {
    fn __register_into(_: &mut Builder) -> Result<(), Error> {
        Ok(())
    }
}

macro_rules! impl_entity_set {
    ($($t:ident),+) => {
        impl<$($t: Entity),+> sealed::Sealed for ($($t,)+) {}

        impl<$($t: Entity),+> EntitySet for ($($t,)+) {
            fn __register_into(builder: &mut Builder) -> Result<(), Error> {
                builder.register_declarations(&mut [$(Declaration::of::<$t>()),+])
            }
        }
    };
}

impl_entity_set!(A);
impl_entity_set!(A, B);
impl_entity_set!(A, B, C);
impl_entity_set!(A, B, C, D);
impl_entity_set!(A, B, C, D, E);
impl_entity_set!(A, B, C, D, E, F);
impl_entity_set!(A, B, C, D, E, F, G);
impl_entity_set!(A, B, C, D, E, F, G, H);
impl_entity_set!(A, B, C, D, E, F, G, H, I);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K, L);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K, L, M);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K, L, M, N);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K, L, M, N, O);
impl_entity_set!(A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P);
