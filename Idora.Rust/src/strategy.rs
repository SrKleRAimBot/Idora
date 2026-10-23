//! Which value follows the prefix.

#![forbid(unsafe_code)]

use crate::value::{ULID_LEN, UUID_LEN};

/// Selects the time-ordered value that follows the prefix.
///
/// Both formats are unique without coordination and sort by creation time. They differ in length
/// and in how they are written down.
///
/// |  | [`Ulid`](Self::Ulid) (default) | [`UuidV7`](Self::UuidV7) |
/// | --- | --- | --- |
/// | Length | 26 | 32 |
/// | Alphabet | Crockford Base32 | hexadecimal |
/// | Random bits | 80 | 74 |
/// | Within one millisecond | strictly increasing per thread | unordered |
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum Strategy {
    /// A 26-character lowercase ULID, for example `01k2f9v8ta7m3x0qbrn4dzeh5c`. The default.
    ///
    /// The same 128 bits as a UUID in six fewer characters, and an alphabet without the letters
    /// people confuse when reading an identifier aloud (`i`, `l`, `o`, `u`). Prefer this unless
    /// something forces your hand.
    #[default]
    Ulid,
    /// A 32-character lowercase RFC 9562 version 7 UUID without hyphens, for example
    /// `0195e7b4a1c87f3d9e2b6c4a8f01d5e3`.
    ///
    /// Choose this when something outside your control expects a real UUID: a `uuid` column, a
    /// schema that validates the format, or a service that parses the value.
    UuidV7,
}

impl Strategy {
    /// The number of characters in the value this strategy produces: 26 or 32.
    ///
    /// An identifier is `prefix.len() + 1 + value_len()` characters long.
    ///
    /// ```
    /// assert_eq!(idora::Strategy::Ulid.value_len(), 26);
    /// assert_eq!(idora::Strategy::UuidV7.value_len(), 32);
    /// ```
    #[must_use]
    pub const fn value_len(self) -> usize {
        match self {
            Self::Ulid => ULID_LEN,
            Self::UuidV7 => UUID_LEN,
        }
    }
}
