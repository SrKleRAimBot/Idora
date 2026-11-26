//! An [`Id`] that is known, in the type system, to belong to one [`Entity`].

#![forbid(unsafe_code)]

use core::cmp::Ordering;
use core::fmt;
use core::hash::{Hash, Hasher};
use core::marker::PhantomData;
use core::ops::Deref;
use core::str::FromStr;

use crate::entity::Entity;
use crate::error::{InvalidIdError, InvalidIdReason};
use crate::id::Id;

/// An [`Id`] whose prefix is known to be `E`'s [`Entity::PREFIX`].
///
/// Where a function takes a `TypedId<User>`, the compiler stops an order's identifier being
/// passed in, and the function has nothing to check. Everything an [`Id`] offers is available
/// through [`Deref`], and a `TypedId` costs exactly what an `Id` costs: 64 bytes, `Copy`, no heap.
///
/// ```
/// use idora::{Entity, TypedId};
///
/// struct User;
/// impl Entity for User {
///     const PREFIX: &'static str = "usr";
/// }
///
/// fn load_user(id: TypedId<User>) -> String {
///     format!("SELECT * FROM users WHERE id = '{id}'")
/// }
///
/// let id: TypedId<User> = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
/// assert_eq!(id.timestamp_ms(), 1_754_568_000_000); // through Deref<Target = Id>
/// load_user(id);
///
/// assert!("ord_01k226e0g0008j4ct4ank7f24s".parse::<TypedId<User>>().is_err());
/// # Ok::<(), idora::InvalidIdError>(())
/// ```
///
/// The prefix is the one `E` *declares*, read at compile time, so no generator is involved. For a
/// type registered with [`Builder::register`](crate::Builder::register) instead, check identifiers
/// against the registry with [`Idora::parse`](crate::Idora::parse).
///
/// It compares, orders and hashes exactly as the [`Id`] inside it does, and needs nothing of `E`
/// beyond [`Entity`]: `TypedId<E>` is `Copy`, `Send` and `Sync` whatever `E` is.
pub struct TypedId<E: Entity> {
    id: Id,
    // `fn() -> E` rather than `E`: the wrapper owns no `E`, so it inherits none of its auto traits.
    entity: PhantomData<fn() -> E>,
}

impl<E: Entity> TypedId<E> {
    /// Parses an identifier that must carry exactly `E::PREFIX`.
    ///
    /// # Errors
    ///
    /// As [`Id::parse_with_prefix`] with `E::PREFIX` as the expected prefix.
    ///
    /// # Panics
    ///
    /// If `E::PREFIX` is not a valid prefix, which [`entity!`](crate::entity) rules out at compile
    /// time; only a hand-written [`Entity`] implementation can declare one.
    #[track_caller]
    pub fn parse(text: &str) -> Result<Self, InvalidIdError> {
        Id::parse_with_prefix(text, E::PREFIX).map(Self::new_unchecked)
    }

    /// Checks that an already parsed `id` carries `E::PREFIX`.
    ///
    /// # Errors
    ///
    /// [`InvalidIdError`] with [`PrefixMismatch`](crate::InvalidIdReason::PrefixMismatch) if it
    /// does not. `TryFrom<Id>` does the same.
    ///
    /// ```
    /// use idora::{Id, TypedId};
    ///
    /// struct Order;
    /// idora::entity!(Order => "ord");
    ///
    /// let id = Id::parse("ord_01k226e0g1zzqdvk5vnacrgxv6")?;
    /// let order = TypedId::<Order>::from_id(id)?;
    /// assert_eq!(order.id(), &id);
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    pub fn from_id(id: Id) -> Result<Self, InvalidIdError> {
        if id.has_prefix(E::PREFIX) {
            Ok(Self::new_unchecked(id))
        } else {
            Err(InvalidIdError::new(
                id.as_str(),
                InvalidIdReason::PrefixMismatch {
                    expected: E::PREFIX.to_owned(),
                },
            ))
        }
    }

    fn new_unchecked(id: Id) -> Self {
        Self {
            id,
            entity: PhantomData,
        }
    }

    /// The untyped identifier.
    #[must_use]
    pub fn id(&self) -> &Id {
        &self.id
    }

    /// Unwraps the untyped identifier. `Id::from` does the same.
    #[must_use]
    pub fn into_id(self) -> Id {
        self.id
    }
}

impl<E: Entity> Deref for TypedId<E> {
    type Target = Id;

    fn deref(&self) -> &Id {
        &self.id
    }
}

impl<E: Entity> Clone for TypedId<E> {
    fn clone(&self) -> Self {
        *self
    }
}

impl<E: Entity> Copy for TypedId<E> {}

impl<E: Entity> PartialEq for TypedId<E> {
    fn eq(&self, other: &Self) -> bool {
        self.id == other.id
    }
}

impl<E: Entity> Eq for TypedId<E> {}

impl<E: Entity> PartialEq<Id> for TypedId<E> {
    fn eq(&self, other: &Id) -> bool {
        self.id == *other
    }
}

impl<E: Entity> PartialEq<TypedId<E>> for Id {
    fn eq(&self, other: &TypedId<E>) -> bool {
        *self == other.id
    }
}

impl<E: Entity> PartialEq<str> for TypedId<E> {
    fn eq(&self, other: &str) -> bool {
        self.id == *other
    }
}

impl<E: Entity> PartialEq<&str> for TypedId<E> {
    fn eq(&self, other: &&str) -> bool {
        self.id == *other
    }
}

impl<E: Entity> Hash for TypedId<E> {
    fn hash<H: Hasher>(&self, state: &mut H) {
        self.id.hash(state);
    }
}

impl<E: Entity> Ord for TypedId<E> {
    fn cmp(&self, other: &Self) -> Ordering {
        self.id.cmp(&other.id)
    }
}

impl<E: Entity> PartialOrd for TypedId<E> {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl<E: Entity> fmt::Display for TypedId<E> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Display::fmt(&self.id, f)
    }
}

impl<E: Entity> fmt::Debug for TypedId<E> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_tuple("TypedId").field(&self.id.as_str()).finish()
    }
}

impl<E: Entity> AsRef<str> for TypedId<E> {
    fn as_ref(&self) -> &str {
        self.id.as_str()
    }
}

impl<E: Entity> AsRef<Id> for TypedId<E> {
    fn as_ref(&self) -> &Id {
        &self.id
    }
}

impl<E: Entity> FromStr for TypedId<E> {
    type Err = InvalidIdError;

    /// Same as [`TypedId::parse`].
    fn from_str(text: &str) -> Result<Self, Self::Err> {
        Self::parse(text)
    }
}

impl<E: Entity> TryFrom<&str> for TypedId<E> {
    type Error = InvalidIdError;

    /// Same as [`TypedId::parse`].
    fn try_from(text: &str) -> Result<Self, Self::Error> {
        Self::parse(text)
    }
}

impl<E: Entity> TryFrom<String> for TypedId<E> {
    type Error = InvalidIdError;

    /// Same as [`TypedId::parse`].
    fn try_from(text: String) -> Result<Self, Self::Error> {
        Self::parse(&text)
    }
}

impl<E: Entity> TryFrom<Id> for TypedId<E> {
    type Error = InvalidIdError;

    /// Same as [`TypedId::from_id`].
    fn try_from(id: Id) -> Result<Self, Self::Error> {
        Self::from_id(id)
    }
}

impl<E: Entity> From<TypedId<E>> for Id {
    fn from(id: TypedId<E>) -> Self {
        id.id
    }
}

impl<E: Entity> From<TypedId<E>> for String {
    fn from(id: TypedId<E>) -> Self {
        id.id.into()
    }
}
