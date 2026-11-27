//! `Serialize` and `Deserialize` for [`Id`] and [`TypedId`], behind the `serde` feature.
//!
//! Both are strings on the wire, exactly the identifier's text. Deserializing runs the strict
//! parse (specification §6.1), so a document with a malformed identifier fails to deserialize
//! instead of producing a value that fails later.

#![forbid(unsafe_code)]

use core::fmt;
use core::marker::PhantomData;

use serde::de::{self, Deserialize, Deserializer, Unexpected, Visitor};
use serde::ser::{Serialize, Serializer};

use crate::entity::Entity;
use crate::error::InvalidIdError;
use crate::id::Id;
use crate::typed::TypedId;

/// Serializes the identifier as a string.
impl Serialize for Id {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(self.as_str())
    }
}

/// Deserializes an identifier from a string, rejecting anything [`Id::parse`] rejects.
///
/// ```
/// use serde::Deserialize;
/// use serde::de::IntoDeserializer;
/// use serde::de::value::{Error, StrDeserializer};
///
/// let text: StrDeserializer<'_, Error> = "usr_01k226e0g0008j4ct4ank7f24s".into_deserializer();
/// assert_eq!(idora::Id::deserialize(text)?.prefix(), "usr");
///
/// let text: StrDeserializer<'_, Error> = "usr_not-an-id".into_deserializer();
/// assert!(idora::Id::deserialize(text).is_err());
/// # Ok::<(), Error>(())
/// ```
impl<'de> Deserialize<'de> for Id {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        deserializer.deserialize_str(IdVisitor {
            expected: None,
            parsed: PhantomData,
        })
    }
}

/// Serializes the identifier as a string.
impl<E: Entity> Serialize for TypedId<E> {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(self.as_str())
    }
}

/// Deserializes an identifier from a string, rejecting anything [`TypedId::parse`] rejects,
/// including a well-formed identifier with a prefix other than `E::PREFIX`.
///
/// ```
/// use serde::Deserialize;
/// use serde::de::IntoDeserializer;
/// use serde::de::value::{Error, StrDeserializer};
/// use idora::TypedId;
///
/// struct User;
/// idora::entity!(User => "usr");
///
/// let text: StrDeserializer<'_, Error> = "usr_01k226e0g0008j4ct4ank7f24s".into_deserializer();
/// assert!(TypedId::<User>::deserialize(text).is_ok());
///
/// let text: StrDeserializer<'_, Error> = "ord_01k226e0g0008j4ct4ank7f24s".into_deserializer();
/// assert!(TypedId::<User>::deserialize(text).is_err());
/// ```
impl<'de, E: Entity> Deserialize<'de> for TypedId<E> {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let id = deserializer.deserialize_str(IdVisitor {
            expected: Some(E::PREFIX),
            parsed: PhantomData,
        })?;
        TypedId::from_id(id).map_err(de::Error::custom)
    }
}

/// Parses whatever string the format hands over, borrowed, transient or owned.
struct IdVisitor {
    expected: Option<&'static str>,
    parsed: PhantomData<Id>,
}

impl IdVisitor {
    fn parse(&self, text: &str) -> Result<Id, InvalidIdError> {
        match self.expected {
            None => Id::parse(text),
            Some(expected) => Id::parse_with_prefix(text, expected),
        }
    }
}

impl Visitor<'_> for IdVisitor {
    type Value = Id;

    fn expecting(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self.expected {
            None => f.write_str("an Idora identifier such as \"usr_01k2f9v8ta7m3x0qbrn4dzeh5c\""),
            Some(prefix) => write!(f, "an Idora identifier with the prefix \"{prefix}\""),
        }
    }

    fn visit_str<Err: de::Error>(self, text: &str) -> Result<Id, Err> {
        self.parse(text).map_err(Err::custom)
    }

    /// For formats that carry strings as raw bytes. Anything that is not UTF-8 cannot be an
    /// identifier.
    fn visit_bytes<Err: de::Error>(self, bytes: &[u8]) -> Result<Id, Err> {
        match core::str::from_utf8(bytes) {
            Ok(text) => self.visit_str(text),
            Err(_) => Err(Err::invalid_value(Unexpected::Bytes(bytes), &self)),
        }
    }
}
