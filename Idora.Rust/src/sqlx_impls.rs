//! `Type`, `Encode` and `Decode` for [`Id`] and [`TypedId`], behind the `sqlx` feature.
//!
//! An identifier is stored as its text, so each implementation defers to the database's own
//! implementation for `str`: whatever text column types a driver accepts for a `&str` (`TEXT`,
//! `VARCHAR`, `CHAR(n)` and so on), it accepts for an `Id`. They are generic over the database,
//! so they work with every driver, including third-party ones, and need no driver feature here.
//!
//! Decoding runs the strict parse (specification §6.1): a malformed value in the column is a
//! decode error naming the text, not an `Id` that cannot exist.

#![forbid(unsafe_code)]

use sqlx::encode::IsNull;
use sqlx::error::BoxDynError;
use sqlx::{Database, Decode, Encode, Type};

use crate::entity::Entity;
use crate::id::Id;
use crate::typed::TypedId;

/// The SQL type of an identifier is the SQL type of a string.
impl<DB: Database> Type<DB> for Id
where
    str: Type<DB>,
{
    fn type_info() -> DB::TypeInfo {
        <str as Type<DB>>::type_info()
    }

    fn compatible(ty: &DB::TypeInfo) -> bool {
        <str as Type<DB>>::compatible(ty)
    }
}

/// Binds the identifier as its text.
impl<'q, DB: Database> Encode<'q, DB> for Id
where
    for<'a> &'a str: Encode<'q, DB>,
{
    fn encode_by_ref(&self, buf: &mut DB::ArgumentBuffer) -> Result<IsNull, BoxDynError> {
        <&str as Encode<'q, DB>>::encode(self.as_str(), buf)
    }

    fn size_hint(&self) -> usize {
        self.as_str().len()
    }
}

/// Decodes text and parses it; malformed text is an error.
impl<'r, DB: Database> Decode<'r, DB> for Id
where
    &'r str: Decode<'r, DB>,
{
    fn decode(value: DB::ValueRef<'r>) -> Result<Self, BoxDynError> {
        let text = <&'r str as Decode<'r, DB>>::decode(value)?;
        Ok(Id::parse(text)?)
    }
}

/// The SQL type of an identifier is the SQL type of a string.
impl<E: Entity, DB: Database> Type<DB> for TypedId<E>
where
    str: Type<DB>,
{
    fn type_info() -> DB::TypeInfo {
        <str as Type<DB>>::type_info()
    }

    fn compatible(ty: &DB::TypeInfo) -> bool {
        <str as Type<DB>>::compatible(ty)
    }
}

/// Binds the identifier as its text.
impl<'q, E: Entity, DB: Database> Encode<'q, DB> for TypedId<E>
where
    for<'a> &'a str: Encode<'q, DB>,
{
    fn encode_by_ref(&self, buf: &mut DB::ArgumentBuffer) -> Result<IsNull, BoxDynError> {
        <&str as Encode<'q, DB>>::encode(self.as_str(), buf)
    }

    fn size_hint(&self) -> usize {
        self.as_str().len()
    }
}

/// Decodes text and parses it with `E::PREFIX`; malformed text, or another entity's identifier,
/// is an error.
impl<'r, E: Entity, DB: Database> Decode<'r, DB> for TypedId<E>
where
    &'r str: Decode<'r, DB>,
{
    fn decode(value: DB::ValueRef<'r>) -> Result<Self, BoxDynError> {
        let text = <&'r str as Decode<'r, DB>>::decode(value)?;
        Ok(TypedId::parse(text)?)
    }
}
