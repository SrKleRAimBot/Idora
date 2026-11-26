//! Reading and comparing identifiers (specification §6): the parsed [`Id`].

#![forbid(unsafe_code)]

use core::cmp::Ordering;
use core::fmt;
use core::hash::{Hash, Hasher};
use core::str::FromStr;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use crate::error::{InvalidIdError, InvalidIdReason, InvalidPrefixError, InvalidPrefixReason};
use crate::prefix::{self, MAX_PREFIX_LEN};
use crate::strategy::Strategy;
use crate::value::{self, MAX_TIMESTAMP_MS, UUID_LEN};

/// The longest identifier there can be: a 16-character prefix, the separator and a UUIDv7.
pub(crate) const MAX_ID_LEN: usize = MAX_PREFIX_LEN + 1 + UUID_LEN;

/// A parsed identifier such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`: its prefix, its value, the
/// value's format and the moment it was minted.
///
/// An `Id` can only hold text a conforming generator could have produced, so once you have one
/// there is nothing left to check. It parses identifiers from any Idora package, in either format,
/// whatever strategy your own generator uses.
///
/// ```
/// use std::time::{Duration, UNIX_EPOCH};
/// use idora::{Id, Strategy};
///
/// let id: Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
/// assert_eq!(id.prefix(), "usr");
/// assert_eq!(id.value(), "01k226e0g0008j4ct4ank7f24s");
/// assert_eq!(id.format(), Strategy::Ulid);
/// assert_eq!(id.timestamp_ms(), 1_754_568_000_000);
/// assert_eq!(id.created_at(), UNIX_EPOCH + Duration::from_millis(1_754_568_000_000));
/// assert_eq!(id.to_string(), "usr_01k226e0g0008j4ct4ank7f24s");
/// # Ok::<(), idora::InvalidIdError>(())
/// ```
///
/// # Representation
///
/// An identifier is at most 49 ASCII bytes, so an `Id` stores them inline, next to the decoded
/// timestamp, in 64 bytes: one cache line, no heap allocation, and `Copy`. Parsing, copying and
/// comparing never allocate, and ordering compares the timestamp as an integer before it looks at
/// any text.
///
/// # Equality and ordering
///
/// Two `Id`s are equal when their text is equal, and hash accordingly. The order is chronological
/// rather than textual (§6.3): by timestamp, then by value, then by prefix. That is what makes
/// identifiers of different entities and different formats interleave in the order they were made:
///
/// ```
/// use idora::Id;
///
/// let user: Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;  // 2025-08-07T12:00:00.000Z
/// let order: Id = "ord_01k226e0g1zzqdvk5vnacrgxv6".parse()?; // one millisecond later
///
/// assert!(user < order);                // although "ord" < "usr" as text
/// assert!(order.is_after(&user));
///
/// let mut ids = vec![order, user];
/// ids.sort();
/// assert_eq!(ids, [user, order]);
/// # Ok::<(), idora::InvalidIdError>(())
/// ```
///
/// Because that order differs from the order of the text, `Id` deliberately does not implement
/// `Borrow<str>`: a `BTreeMap<Id, _>` could not be searched by `&str` consistently. Compare it with
/// a string directly (`id == "usr_…"`) or use [`as_str`](Self::as_str).
#[derive(Clone, Copy)]
pub struct Id {
    timestamp_ms: u64,
    len: u8,
    prefix_len: u8,
    bytes: [u8; MAX_ID_LEN],
}

/// What [`check`] found wrong, without allocating. Turned into an [`InvalidIdError`] only when the
/// caller wants one.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Fault {
    NoSeparator,
    Prefix(InvalidPrefixReason),
    Value,
    PrefixMismatch,
}

/// The whole of §6.1, rules 2 to 5, in order. Never allocates.
#[inline]
fn check(text: &str, expected: Option<&str>) -> Result<Id, Fault> {
    let bytes = text.as_bytes();
    let Some(separator) = bytes.iter().position(|&b| b == b'_') else {
        return Err(Fault::NoSeparator);
    };
    let prefix = &text[..separator];
    if let Some(reason) = prefix::check(prefix) {
        return Err(Fault::Prefix(reason));
    }
    let Some((_, timestamp_ms)) = value::read_value(&bytes[separator + 1..]) else {
        return Err(Fault::Value);
    };
    if expected.is_some_and(|expected| expected != prefix) {
        return Err(Fault::PrefixMismatch);
    }
    // A valid prefix is at most 16 bytes and a valid value at most 32, so this always fits.
    Ok(Id::from_parts(bytes, separator, timestamp_ms))
}

impl Fault {
    #[cold]
    fn into_error(self, text: &str, expected: Option<&str>) -> InvalidIdError {
        let reason = match self {
            Self::NoSeparator => InvalidIdReason::NoSeparator,
            Self::Prefix(reason) => {
                let prefix = text.split_once('_').map_or("", |(prefix, _)| prefix);
                InvalidIdReason::Prefix(InvalidPrefixError::new(prefix, reason))
            }
            Self::Value => InvalidIdReason::Value,
            Self::PrefixMismatch => InvalidIdReason::PrefixMismatch {
                expected: expected.unwrap_or_default().to_owned(),
            },
        };
        InvalidIdError::new(text, reason)
    }
}

impl Id {
    /// Builds an `Id` from text already known to be valid, with its separator at `separator`.
    #[inline]
    pub(crate) fn from_parts(text: &[u8], separator: usize, timestamp_ms: u64) -> Self {
        let mut bytes = [0u8; MAX_ID_LEN];
        bytes[..text.len()].copy_from_slice(text);
        #[allow(clippy::cast_possible_truncation)] // at most 49 and 16
        Self {
            timestamp_ms,
            len: text.len() as u8,
            prefix_len: separator as u8,
            bytes,
        }
    }

    /// Parses an identifier, whatever its prefix.
    ///
    /// Parsing is strict (§6.1): it accepts exactly what a conforming generator emits, so there is
    /// no trimming, no case folding and no hyphenated UUIDs. It never allocates unless it fails.
    ///
    /// # Errors
    ///
    /// [`InvalidIdError`], with the first of these that applies as its
    /// [`reason`](InvalidIdError::reason):
    ///
    /// 1. [`NoSeparator`](InvalidIdReason::NoSeparator): there is no `_`;
    /// 2. [`Prefix`](InvalidIdReason::Prefix): the text before the first `_` is not a valid prefix;
    /// 3. [`Value`](InvalidIdReason::Value): the text after it is not a lowercase ULID or UUIDv7.
    ///
    /// ```
    /// use idora::{Id, InvalidIdReason};
    ///
    /// let id = Id::parse("inv_0198846702007011a233445566778899")?;
    /// assert_eq!(id.prefix(), "inv");
    ///
    /// let error = Id::parse("usr_01K226E0G0008J4CT4ANK7F24S").unwrap_err();
    /// assert_eq!(error.reason(), &InvalidIdReason::Value);  // uppercase is not tolerated
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    pub fn parse(text: &str) -> Result<Self, InvalidIdError> {
        check(text, None).map_err(|fault| fault.into_error(text, None))
    }

    /// Parses an identifier that must carry exactly the prefix `expected`, case included.
    ///
    /// For a prefix that belongs to a registered type, [`Idora::parse`](crate::Idora::parse) looks
    /// it up for you. An `expected` prefix that is not itself valid can never match.
    ///
    /// # Errors
    ///
    /// As [`parse`](Self::parse), and then [`PrefixMismatch`](InvalidIdReason::PrefixMismatch) if
    /// the identifier is well formed but its prefix is not `expected`. A malformed identifier is
    /// reported as malformed whatever its prefix.
    ///
    /// ```
    /// use idora::{Id, InvalidIdReason};
    ///
    /// assert!(Id::parse_with_prefix("usr_01k226e0g0008j4ct4ank7f24s", "usr").is_ok());
    ///
    /// let error = Id::parse_with_prefix("USR_01k226e0g0008j4ct4ank7f24s", "usr").unwrap_err();
    /// assert_eq!(error.expected_prefix(), Some("usr"));
    /// assert!(matches!(error.reason(), InvalidIdReason::PrefixMismatch { .. }));
    /// ```
    pub fn parse_with_prefix(text: &str, expected: &str) -> Result<Self, InvalidIdError> {
        check(text, Some(expected)).map_err(|fault| fault.into_error(text, Some(expected)))
    }

    /// Returns `true` if `text` is an identifier, whatever its prefix. Never allocates.
    ///
    /// ```
    /// assert!(idora::Id::is_valid("usr_01k226e0g0008j4ct4ank7f24s"));
    /// assert!(!idora::Id::is_valid(" usr_01k226e0g0008j4ct4ank7f24s"));
    /// ```
    #[must_use]
    pub fn is_valid(text: &str) -> bool {
        check(text, None).is_ok()
    }

    /// Returns `true` if `text` is an identifier with exactly the prefix `expected`. Never
    /// allocates.
    ///
    /// ```
    /// use idora::Id;
    ///
    /// assert!(Id::is_valid_with_prefix("ord_01k226e0g1zzqdvk5vnacrgxv6", "ord"));
    /// assert!(!Id::is_valid_with_prefix("ord_01k226e0g1zzqdvk5vnacrgxv6", "usr"));
    /// ```
    #[must_use]
    pub fn is_valid_with_prefix(text: &str, expected: &str) -> bool {
        check(text, Some(expected)).is_ok()
    }

    /// The whole identifier, exactly as it was parsed or generated.
    #[must_use]
    #[allow(clippy::missing_panics_doc)] // the `expect` below cannot fire
    pub fn as_str(&self) -> &str {
        // Every byte is ASCII, checked when the `Id` was made, so this cannot fail; it is what
        // keeps the crate free of `unsafe` here.
        core::str::from_utf8(self.as_bytes()).expect("identifiers are ASCII")
    }

    fn as_bytes(&self) -> &[u8] {
        &self.bytes[..usize::from(self.len)]
    }

    /// The prefix: the text before the first `_`, exactly as written.
    ///
    /// ```
    /// let id: idora::Id = "USR_01hf7yat0004hmasw9nf6yy093".parse()?;
    /// assert_eq!(id.prefix(), "USR");
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    #[must_use]
    pub fn prefix(&self) -> &str {
        &self.as_str()[..usize::from(self.prefix_len)]
    }

    /// The value: the 26 or 32 characters after the `_`.
    #[must_use]
    pub fn value(&self) -> &str {
        &self.as_str()[usize::from(self.prefix_len) + 1..]
    }

    /// Which format the value is in, decided by its length.
    ///
    /// ```
    /// use idora::{Id, Strategy};
    ///
    /// assert_eq!(Id::parse("usr_01k226e0g0008j4ct4ank7f24s")?.format(), Strategy::Ulid);
    /// assert_eq!(Id::parse("inv_0198846702007011a233445566778899")?.format(), Strategy::UuidV7);
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    #[must_use]
    pub fn format(&self) -> Strategy {
        if self.value().len() == Strategy::Ulid.value_len() {
            Strategy::Ulid
        } else {
            Strategy::UuidV7
        }
    }

    /// The milliseconds since the Unix epoch that the value begins with: when the identifier was
    /// minted, by the clock of whichever machine minted it.
    #[must_use]
    pub const fn timestamp_ms(&self) -> u64 {
        self.timestamp_ms
    }

    /// [`timestamp_ms`](Self::timestamp_ms) as a [`SystemTime`]. Like the timestamp itself, it is
    /// an absolute instant with no time zone.
    #[must_use]
    pub fn created_at(&self) -> SystemTime {
        UNIX_EPOCH + Duration::from_millis(self.timestamp_ms)
    }

    /// Returns `true` if the prefix is exactly `prefix`, case included.
    ///
    /// ```
    /// let id: idora::Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
    /// assert!(id.has_prefix("usr"));
    /// assert!(!id.has_prefix("USR"));
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    #[must_use]
    pub fn has_prefix(&self, prefix: &str) -> bool {
        self.prefix() == prefix
    }

    /// Returns `true` if `self` comes strictly before `other` in the natural order.
    #[must_use]
    pub fn is_before(&self, other: &Self) -> bool {
        self < other
    }

    /// Returns `true` if `self` comes strictly after `other` in the natural order.
    #[must_use]
    pub fn is_after(&self, other: &Self) -> bool {
        self > other
    }

    /// Compares the timestamps alone: [`Ordering::Equal`] for two identifiers minted in the same
    /// millisecond, whatever their prefixes, values and formats.
    ///
    /// ```
    /// use core::cmp::Ordering;
    /// use idora::Id;
    ///
    /// let ulid: Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
    /// let uuid: Id = "inv_0198846702007011a233445566778899".parse()?;
    /// assert_eq!(ulid.cmp_timestamp(&uuid), Ordering::Equal);
    /// assert!(ulid.same_millisecond(&uuid));
    /// assert_ne!(ulid, uuid);
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    #[must_use]
    pub fn cmp_timestamp(&self, other: &Self) -> Ordering {
        self.timestamp_ms.cmp(&other.timestamp_ms)
    }

    /// Returns `true` if both identifiers were minted in the same millisecond.
    #[must_use]
    pub fn same_millisecond(&self, other: &Self) -> bool {
        self.timestamp_ms == other.timestamp_ms
    }

    /// Returns `true` if the identifier was minted strictly before `time`, compared at millisecond
    /// precision: `time` is truncated to the millisecond first.
    ///
    /// ```
    /// use std::time::{Duration, UNIX_EPOCH};
    ///
    /// let id: idora::Id = "usr_01k226e0g0008j4ct4ank7f24s".parse()?;
    /// let minted = UNIX_EPOCH + Duration::from_millis(1_754_568_000_000);
    ///
    /// assert!(id.created_before(minted + Duration::from_millis(1)));
    /// assert!(!id.created_before(minted + Duration::from_micros(999))); // same millisecond
    /// assert!(id.created_after(minted - Duration::from_millis(1)));
    /// assert!(!id.created_after(minted));
    /// # Ok::<(), idora::InvalidIdError>(())
    /// ```
    #[must_use]
    pub fn created_before(&self, time: SystemTime) -> bool {
        i128::from(self.timestamp_ms) < floor_ms(time)
    }

    /// Returns `true` if the identifier was minted strictly after `time`, compared at millisecond
    /// precision: `time` is truncated to the millisecond first. Every identifier was created after
    /// any instant before 1970.
    #[must_use]
    pub fn created_after(&self, time: SystemTime) -> bool {
        i128::from(self.timestamp_ms) > floor_ms(time)
    }

    /// The lowest identifier with this prefix and format that any conforming generator can mint
    /// in the millisecond containing `time` (§6.4).
    ///
    /// Identifiers of one prefix and format minted during a span of time are a contiguous range
    /// of strings, so a time-window query needs no parsing at all:
    ///
    /// ```
    /// use std::time::{Duration, SystemTime};
    /// use idora::{Id, Strategy};
    ///
    /// let to = SystemTime::now();
    /// let from = to - Duration::from_secs(24 * 60 * 60);
    /// let lower = Id::lower_bound("usr", from, Strategy::Ulid)?;
    /// let upper = Id::upper_bound("usr", to, Strategy::Ulid)?;
    ///
    /// // SELECT * FROM users WHERE id >= $1 AND id <= $2
    /// let minted_now = idora::Idora::new().generate_with_prefix("usr")?;
    /// assert!(lower.as_str() <= minted_now.as_str() && minted_now.as_str() <= upper.as_str());
    /// # Ok::<(), idora::InvalidPrefixError>(())
    /// ```
    ///
    /// The bounds compare correctly only as plain strings, and only against identifiers of the
    /// same prefix and format: exactly the shape of a database column.
    ///
    /// # Errors
    ///
    /// [`InvalidPrefixError`] if `prefix` is not 1 to 16 ASCII letters or digits.
    ///
    /// # Panics
    ///
    /// If `time` is before the Unix epoch or after the largest 48-bit timestamp (early in the year
    /// 10889): an argument out of range, which no identifier can describe.
    pub fn lower_bound(
        prefix: &str,
        time: SystemTime,
        strategy: Strategy,
    ) -> Result<Self, InvalidPrefixError> {
        Self::bound(prefix, time, strategy, value::lowest_value)
    }

    /// The highest identifier with this prefix and format that any conforming generator can mint
    /// in the millisecond containing `time`. See [`lower_bound`](Self::lower_bound).
    ///
    /// ```
    /// use std::time::{Duration, UNIX_EPOCH};
    /// use idora::{Id, Strategy};
    ///
    /// let time = UNIX_EPOCH + Duration::from_millis(1_754_568_000_000);
    /// assert_eq!(
    ///     Id::upper_bound("inv", time, Strategy::UuidV7)?,
    ///     "inv_0198846702007fffbfffffffffffffff"
    /// );
    /// # Ok::<(), idora::InvalidPrefixError>(())
    /// ```
    ///
    /// # Errors
    ///
    /// As [`lower_bound`](Self::lower_bound).
    ///
    /// # Panics
    ///
    /// As [`lower_bound`](Self::lower_bound).
    pub fn upper_bound(
        prefix: &str,
        time: SystemTime,
        strategy: Strategy,
    ) -> Result<Self, InvalidPrefixError> {
        Self::bound(prefix, time, strategy, value::highest_value)
    }

    /// The lowest value, without a prefix, that any conforming generator can produce in the
    /// millisecond containing `time`. For a column that stores the value alone, such as a native
    /// `uuid` column.
    ///
    /// ```
    /// use std::time::UNIX_EPOCH;
    /// use idora::{Id, Strategy};
    ///
    /// assert_eq!(Id::lower_bound_value(UNIX_EPOCH, Strategy::UuidV7), "00000000000070008000000000000000");
    /// ```
    ///
    /// # Panics
    ///
    /// As [`lower_bound`](Self::lower_bound).
    #[must_use]
    pub fn lower_bound_value(time: SystemTime, strategy: Strategy) -> String {
        bound_value(time, strategy, value::lowest_value)
    }

    /// The highest value, without a prefix, that any conforming generator can produce in the
    /// millisecond containing `time`.
    ///
    /// ```
    /// use std::time::UNIX_EPOCH;
    /// use idora::{Id, Strategy};
    ///
    /// assert_eq!(Id::upper_bound_value(UNIX_EPOCH, Strategy::Ulid), "0000000000zzzzzzzzzzzzzzzz");
    /// ```
    ///
    /// # Panics
    ///
    /// As [`lower_bound`](Self::lower_bound).
    #[must_use]
    pub fn upper_bound_value(time: SystemTime, strategy: Strategy) -> String {
        bound_value(time, strategy, value::highest_value)
    }

    fn bound(
        prefix: &str,
        time: SystemTime,
        strategy: Strategy,
        write: fn(u64, Strategy, &mut [u8]) -> usize,
    ) -> Result<Self, InvalidPrefixError> {
        prefix::validate_prefix(prefix)?;
        let timestamp_ms = bound_timestamp(time);
        let mut text = [0u8; MAX_ID_LEN];
        text[..prefix.len()].copy_from_slice(prefix.as_bytes());
        text[prefix.len()] = b'_';
        let len = prefix.len() + 1 + write(timestamp_ms, strategy, &mut text[prefix.len() + 1..]);
        Ok(Self::from_parts(&text[..len], prefix.len(), timestamp_ms))
    }
}

fn bound_value(
    time: SystemTime,
    strategy: Strategy,
    write: fn(u64, Strategy, &mut [u8]) -> usize,
) -> String {
    let mut text = [0u8; UUID_LEN];
    let len = write(bound_timestamp(time), strategy, &mut text);
    core::str::from_utf8(&text[..len])
        .expect("values are ASCII")
        .to_owned()
}

/// `time` as a whole number of milliseconds since the epoch, for a range bound.
#[track_caller]
fn bound_timestamp(time: SystemTime) -> u64 {
    match u64::try_from(floor_ms(time)) {
        Ok(ms) if ms <= MAX_TIMESTAMP_MS => ms,
        _ => panic!(
            "an Idora range bound needs a time between the Unix epoch and {MAX_TIMESTAMP_MS} ms \
             after it (the largest 48-bit timestamp), got {time:?}"
        ),
    }
}

/// `time` in milliseconds since the epoch, rounded towards negative infinity, so the
/// sub-millisecond part is dropped on both sides of 1970.
fn floor_ms(time: SystemTime) -> i128 {
    match time.duration_since(UNIX_EPOCH) {
        // Durations are at most u64::MAX seconds, so these all fit in an i128.
        Ok(after) => i128::try_from(after.as_millis()).expect("fits"),
        Err(before) => {
            -i128::try_from(before.duration().as_nanos().div_ceil(1_000_000)).expect("fits")
        }
    }
}

impl PartialEq for Id {
    fn eq(&self, other: &Self) -> bool {
        self.as_bytes() == other.as_bytes()
    }
}

impl Eq for Id {}

impl Hash for Id {
    fn hash<H: Hasher>(&self, state: &mut H) {
        self.as_bytes().hash(state);
    }
}

impl Ord for Id {
    /// The natural order (§6.3): by timestamp, then by value, then by prefix. Consistent with
    /// equality, because the value and the prefix together are the whole text.
    fn cmp(&self, other: &Self) -> Ordering {
        self.timestamp_ms
            .cmp(&other.timestamp_ms)
            .then_with(|| self.value().cmp(other.value()))
            .then_with(|| self.prefix().cmp(other.prefix()))
    }
}

impl PartialOrd for Id {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl PartialEq<str> for Id {
    fn eq(&self, other: &str) -> bool {
        self.as_str() == other
    }
}

impl PartialEq<&str> for Id {
    fn eq(&self, other: &&str) -> bool {
        self.as_str() == *other
    }
}

impl PartialEq<String> for Id {
    fn eq(&self, other: &String) -> bool {
        self.as_str() == other
    }
}

impl PartialEq<Id> for str {
    fn eq(&self, other: &Id) -> bool {
        self == other.as_str()
    }
}

impl PartialEq<Id> for &str {
    fn eq(&self, other: &Id) -> bool {
        *self == other.as_str()
    }
}

impl PartialEq<Id> for String {
    fn eq(&self, other: &Id) -> bool {
        self == other.as_str()
    }
}

impl fmt::Display for Id {
    /// Writes the identifier exactly as parsed. Width, fill and alignment are honoured.
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.pad(self.as_str())
    }
}

impl fmt::Debug for Id {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_tuple("Id").field(&self.as_str()).finish()
    }
}

impl AsRef<str> for Id {
    fn as_ref(&self) -> &str {
        self.as_str()
    }
}

impl FromStr for Id {
    type Err = InvalidIdError;

    /// Same as [`Id::parse`].
    fn from_str(text: &str) -> Result<Self, Self::Err> {
        Self::parse(text)
    }
}

impl TryFrom<&str> for Id {
    type Error = InvalidIdError;

    /// Same as [`Id::parse`].
    fn try_from(text: &str) -> Result<Self, Self::Error> {
        Self::parse(text)
    }
}

impl TryFrom<String> for Id {
    type Error = InvalidIdError;

    /// Same as [`Id::parse`]. Handy where a framework hands over an owned `String`.
    fn try_from(text: String) -> Result<Self, Self::Error> {
        Self::parse(&text)
    }
}

impl From<Id> for String {
    fn from(id: Id) -> Self {
        id.as_str().to_owned()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ULID_ID: &str = "usr_01k226e0g0008j4ct4ank7f24s";

    #[test]
    fn an_id_is_one_cache_line_and_needs_no_drop() {
        assert_eq!(size_of::<Id>(), 64);
        assert!(!core::mem::needs_drop::<Id>());
    }

    #[test]
    fn check_reports_faults_in_the_specified_order() {
        assert_eq!(check("", None), Err(Fault::NoSeparator));
        assert_eq!(check("nope", Some("usr")), Err(Fault::NoSeparator));
        assert_eq!(
            check("_x", None),
            Err(Fault::Prefix(InvalidPrefixReason::Empty))
        );
        assert_eq!(check("usr_bad", Some("ord")), Err(Fault::Value));
        assert_eq!(check(ULID_ID, Some("ord")), Err(Fault::PrefixMismatch));
        assert!(check(ULID_ID, Some("usr")).is_ok());
    }

    #[test]
    fn the_prefix_error_names_only_the_prefix() {
        let error = Id::parse("us-r_01k226e0g0008j4ct4ank7f24s").unwrap_err();
        match error.reason() {
            InvalidIdReason::Prefix(e) => {
                assert_eq!(e.prefix(), "us-r");
                assert_eq!(
                    e.reason(),
                    InvalidPrefixReason::Character { ch: '-', index: 2 }
                );
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn floor_ms_truncates_towards_the_past_on_both_sides_of_the_epoch() {
        let micros = |n: u64| Duration::from_micros(n);
        assert_eq!(floor_ms(UNIX_EPOCH), 0);
        assert_eq!(floor_ms(UNIX_EPOCH + micros(999)), 0);
        assert_eq!(floor_ms(UNIX_EPOCH + micros(1_000)), 1);
        assert_eq!(floor_ms(UNIX_EPOCH - micros(1)), -1);
        assert_eq!(floor_ms(UNIX_EPOCH - micros(1_000)), -1);
        assert_eq!(floor_ms(UNIX_EPOCH - micros(1_001)), -2);
    }

    #[test]
    fn display_honours_padding() {
        let id = Id::parse("x_00000000000000000000000000").unwrap();
        assert_eq!(format!("[{id:>30}]"), "[  x_00000000000000000000000000]");
    }
}
