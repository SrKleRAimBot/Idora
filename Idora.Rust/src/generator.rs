//! The generator (specification §5), its read-only registry (§5.3) and registry-aware parsing
//! (§6.1).

#![forbid(unsafe_code)]

use core::any::TypeId;
use core::fmt;
use core::hash::{BuildHasherDefault, Hasher};
use std::collections::HashMap;
use std::sync::Arc;

use crate::builder::Builder;
use crate::error::{Error, InvalidPrefixError, PrefixNotRegisteredError};
use crate::id::{Id, MAX_ID_LEN};
use crate::prefix;
use crate::strategy::Strategy;
use crate::value::{self, ULID_LEN, UUID_LEN};

/// Generates prefixed, time-ordered identifiers such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
///
/// Build one at start-up with [`Idora::builder`], then share it: it is immutable, `Send + Sync`,
/// and cloning it is a reference-count increment. Every identifier is `<prefix>_<value>`, where
/// the value is a [ULID](Strategy::Ulid) (the default) or a [UUIDv7](Strategy::UuidV7); both
/// begin with a millisecond timestamp, so identifiers sort into creation order as plain strings.
///
/// Generating an identifier costs exactly one allocation, the returned `String`, and takes no
/// lock; [`generate_id`](Self::generate_id) returns an [`Id`] instead and costs none. See the
/// crate documentation for the threading and ordering guarantees.
///
/// ```
/// use idora::{Entity, Idora};
///
/// struct User;
/// impl Entity for User {
///     const PREFIX: &'static str = "usr";
/// }
/// struct Comment;
///
/// let ids = Idora::builder().register_entity::<User>()?.build();
///
/// let id = ids.generate::<User>()?;            // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
/// assert_eq!(id.len(), 3 + 1 + 26);
///
/// let tmp = ids.generate_with_prefix("tmp")?;  // "tmp_01k2f9v8tb9p5r2xnc7fkw3m0d"
/// assert!(tmp.starts_with("tmp_"));
///
/// assert!(ids.generate::<Comment>().is_err()); // never registered
/// # Ok::<(), idora::Error>(())
/// ```
#[derive(Clone)]
pub struct Idora {
    inner: Arc<Inner>,
}

struct Inner {
    strategy: Strategy,
    registrations: Box<[Registration]>,
    by_type: TypeIdMap<usize>,
}

/// One entry in the registry: an entity type and its prefix.
///
/// Returned by [`Idora::registrations`].
#[derive(Clone, PartialEq, Eq, Hash)]
pub struct Registration {
    type_id: TypeId,
    type_name: &'static str,
    /// Stored with the separator already appended, so generating an identifier is one copy.
    prefix_with_separator: Box<str>,
}

impl Registration {
    pub(crate) fn new(type_id: TypeId, type_name: &'static str, prefix: &str) -> Self {
        let mut prefix_with_separator = String::with_capacity(prefix.len() + 1);
        prefix_with_separator.push_str(prefix);
        prefix_with_separator.push(prefix::SEPARATOR);
        Self {
            type_id,
            type_name,
            prefix_with_separator: prefix_with_separator.into_boxed_str(),
        }
    }

    /// The registered type.
    #[must_use]
    pub fn type_id(&self) -> TypeId {
        self.type_id
    }

    /// The registered type's [`std::any::type_name`], for messages and diagnostics.
    #[must_use]
    pub fn type_name(&self) -> &'static str {
        self.type_name
    }

    /// The prefix, exactly as registered and without the trailing separator.
    #[must_use]
    pub fn prefix(&self) -> &str {
        let len = self.prefix_with_separator.len() - 1;
        &self.prefix_with_separator[..len]
    }
}

impl fmt::Debug for Registration {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Registration")
            .field("type_name", &self.type_name)
            .field("prefix", &self.prefix())
            .finish_non_exhaustive()
    }
}

impl Idora {
    /// Starts configuring a generator. See [`Builder`].
    #[must_use]
    pub fn builder() -> Builder {
        Builder::new()
    }

    /// A generator with the default [`Strategy::Ulid`] and nothing registered, so only
    /// [`generate_with_prefix`](Self::generate_with_prefix) is useful. Same as
    /// [`Idora::default`].
    ///
    /// ```
    /// let ids = idora::Idora::new();
    /// assert!(ids.generate_with_prefix("tmp")?.starts_with("tmp_"));
    /// # Ok::<(), idora::InvalidPrefixError>(())
    /// ```
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    pub(crate) fn from_parts(
        strategy: Strategy,
        registrations: Vec<Registration>,
        by_type: TypeIdMap<usize>,
    ) -> Self {
        Self {
            inner: Arc::new(Inner {
                strategy,
                registrations: registrations.into_boxed_slice(),
                by_type,
            }),
        }
    }

    /// Generates an identifier for `T`, using the prefix `T` was registered with.
    ///
    /// Prefer this to [`generate_with_prefix`](Self::generate_with_prefix): the prefix lives in
    /// one place, is validated once at start-up, and a type you forgot to register fails loudly
    /// instead of quietly producing a wrong prefix.
    ///
    /// # Errors
    ///
    /// [`PrefixNotRegisteredError`] if `T` was never registered. Idora never invents a prefix
    /// from the type's name.
    ///
    /// # Panics
    ///
    /// Only if the operating system's random number generator fails, which on supported platforms
    /// means the process cannot do anything securely anyway.
    ///
    /// ```
    /// struct Order;
    /// let ids = idora::Idora::builder().register::<Order>("ord")?.build();
    /// let id = ids.generate::<Order>()?;
    /// assert!(id.starts_with("ord_"));
    /// # Ok::<(), idora::Error>(())
    /// ```
    #[inline]
    pub fn generate<T: ?Sized + 'static>(&self) -> Result<String, PrefixNotRegisteredError> {
        match self.find(TypeId::of::<T>()) {
            Some(registration) => Ok(self.compose(&registration.prefix_with_separator, false)),
            None => Err(PrefixNotRegisteredError::of::<T>()),
        }
    }

    /// Generates an identifier with a prefix supplied at the call site.
    ///
    /// For prefixes only known at run time: a plugin's own types, or a prefix read from
    /// configuration. The prefix is validated on every call, which costs a few nanoseconds; for
    /// types you know at compile time, register them and use [`generate`](Self::generate).
    ///
    /// # Errors
    ///
    /// [`InvalidPrefixError`] if `prefix` is empty, longer than 16 characters, or contains
    /// anything but ASCII letters and digits.
    ///
    /// # Panics
    ///
    /// As [`generate`](Self::generate).
    ///
    /// ```
    /// let ids = idora::Idora::new();
    /// assert!(ids.generate_with_prefix("tmp")?.starts_with("tmp_"));
    /// assert!(ids.generate_with_prefix("tmp_file").is_err());
    /// # Ok::<(), idora::InvalidPrefixError>(())
    /// ```
    #[inline]
    pub fn generate_with_prefix(&self, prefix: &str) -> Result<String, InvalidPrefixError> {
        match prefix::check(prefix) {
            None => Ok(self.compose(prefix, true)),
            Some(reason) => Err(InvalidPrefixError::new(prefix, reason)),
        }
    }

    /// Generates an identifier for `T` as an [`Id`] rather than a `String`: no allocation at all,
    /// and the prefix, value and timestamp are at hand without parsing.
    ///
    /// # Errors
    ///
    /// As [`generate`](Self::generate).
    ///
    /// # Panics
    ///
    /// As [`generate`](Self::generate).
    ///
    /// ```
    /// struct Order;
    /// let ids = idora::Idora::builder().register::<Order>("ord")?.build();
    /// let id = ids.generate_id::<Order>()?;
    /// assert_eq!(id.prefix(), "ord");
    /// assert!(id.timestamp_ms() > 0);
    /// # Ok::<(), idora::Error>(())
    /// ```
    #[inline]
    pub fn generate_id<T: ?Sized + 'static>(&self) -> Result<Id, PrefixNotRegisteredError> {
        match self.find(TypeId::of::<T>()) {
            Some(registration) => {
                let head = &registration.prefix_with_separator;
                let (buf, len) = self.compose_into_buffer(head, false);
                Ok(Self::to_id(&buf[..len], head.len() - 1))
            }
            None => Err(PrefixNotRegisteredError::of::<T>()),
        }
    }

    /// Generates an identifier with a prefix supplied at the call site, as an [`Id`]. See
    /// [`generate_with_prefix`](Self::generate_with_prefix).
    ///
    /// # Errors
    ///
    /// As [`generate_with_prefix`](Self::generate_with_prefix).
    ///
    /// # Panics
    ///
    /// As [`generate`](Self::generate).
    #[inline]
    pub fn generate_id_with_prefix(&self, prefix: &str) -> Result<Id, InvalidPrefixError> {
        match prefix::check(prefix) {
            None => {
                let (buf, len) = self.compose_into_buffer(prefix, true);
                Ok(Self::to_id(&buf[..len], prefix.len()))
            }
            Some(reason) => Err(InvalidPrefixError::new(prefix, reason)),
        }
    }

    /// Parses an identifier that must carry the prefix registered for `T` (§6.1).
    ///
    /// The registered prefix is compared exactly, case included. This is the check to make on an
    /// identifier that arrives in a route, a message or a foreign key and is supposed to name a
    /// `T`.
    ///
    /// # Errors
    ///
    /// - [`Error::PrefixNotRegistered`] if `T` was never registered, whatever `text` is: the
    ///   identifier is not at fault.
    /// - [`Error::InvalidId`] if `text` is not an identifier, or is one with another prefix.
    ///
    /// ```
    /// use idora::{Error, Idora, InvalidIdReason};
    ///
    /// struct User;
    /// struct Order;
    /// let ids = Idora::builder().register::<User>("usr")?.register::<Order>("ord")?.build();
    ///
    /// let id = ids.parse::<User>("usr_01k226e0g0008j4ct4ank7f24s")?;
    /// assert_eq!(id.prefix(), "usr");
    ///
    /// match ids.parse::<Order>("usr_01k226e0g0008j4ct4ank7f24s") {
    ///     Err(Error::InvalidId(e)) => assert_eq!(e.expected_prefix(), Some("ord")),
    ///     other => panic!("expected a prefix mismatch, got {other:?}"),
    /// }
    /// # Ok::<(), idora::Error>(())
    /// ```
    pub fn parse<T: ?Sized + 'static>(&self, text: &str) -> Result<Id, Error> {
        let expected = self.prefix_of::<T>()?;
        Ok(Id::parse_with_prefix(text, expected)?)
    }

    /// Returns whether `text` is an identifier carrying the prefix registered for `T`. Never
    /// allocates unless `T` is unregistered.
    ///
    /// # Errors
    ///
    /// [`PrefixNotRegisteredError`] if `T` was never registered. An unregistered type is a bug in
    /// the program, not a property of the text, so it is not reported as `false`.
    ///
    /// ```
    /// struct User;
    /// let ids = idora::Idora::builder().register::<User>("usr")?.build();
    /// assert!(ids.is_id_for::<User>("usr_01k226e0g0008j4ct4ank7f24s")?);
    /// assert!(!ids.is_id_for::<User>("ord_01k226e0g0008j4ct4ank7f24s")?);
    /// # Ok::<(), idora::Error>(())
    /// ```
    pub fn is_id_for<T: ?Sized + 'static>(
        &self,
        text: &str,
    ) -> Result<bool, PrefixNotRegisteredError> {
        Ok(Id::is_valid_with_prefix(text, self.prefix_of::<T>()?))
    }

    /// The prefix registered for `T`, without the trailing separator.
    ///
    /// # Errors
    ///
    /// [`PrefixNotRegisteredError`] if `T` was never registered. Use
    /// [`get_prefix`](Self::get_prefix) for an `Option` instead.
    ///
    /// ```
    /// struct Order;
    /// let ids = idora::Idora::builder().register::<Order>("ord")?.build();
    ///
    /// // Does this identifier belong to an order?
    /// let id = ids.generate::<Order>()?;
    /// let (prefix, _value) = id.split_once('_').unwrap();
    /// assert_eq!(prefix, ids.prefix_of::<Order>()?);
    /// # Ok::<(), idora::Error>(())
    /// ```
    pub fn prefix_of<T: ?Sized + 'static>(&self) -> Result<&str, PrefixNotRegisteredError> {
        self.get_prefix::<T>()
            .ok_or_else(PrefixNotRegisteredError::of::<T>)
    }

    /// The prefix registered for `T`, or `None`.
    #[must_use]
    pub fn get_prefix<T: ?Sized + 'static>(&self) -> Option<&str> {
        self.get_prefix_by_type_id(TypeId::of::<T>())
    }

    /// The prefix registered for the type with this [`TypeId`], or `None`. For code that only
    /// has the type at run time.
    #[must_use]
    pub fn get_prefix_by_type_id(&self, type_id: TypeId) -> Option<&str> {
        self.find(type_id).map(Registration::prefix)
    }

    /// Every registration, in the order it was made.
    ///
    /// ```
    /// struct User;
    /// struct Order;
    /// let ids = idora::Idora::builder()
    ///     .register::<User>("usr")?
    ///     .register::<Order>("ord")?
    ///     .build();
    ///
    /// let prefixes: Vec<&str> = ids.registrations().iter().map(|r| r.prefix()).collect();
    /// assert_eq!(prefixes, ["usr", "ord"]);
    /// # Ok::<(), idora::Error>(())
    /// ```
    #[must_use]
    pub fn registrations(&self) -> &[Registration] {
        &self.inner.registrations
    }

    /// The value format this generator produces.
    #[must_use]
    pub fn strategy(&self) -> Strategy {
        self.inner.strategy
    }

    #[inline]
    fn find(&self, type_id: TypeId) -> Option<&Registration> {
        let index = *self.inner.by_type.get(&type_id)?;
        self.inner.registrations.get(index)
    }

    /// Writes `head` (and a separator, if `add_separator`) then a fresh value into a stack
    /// buffer, and copies the result into a `String` of exactly the right capacity: the one
    /// allocation an identifier costs.
    #[inline]
    fn compose(&self, head: &str, add_separator: bool) -> String {
        let (buf, len) = self.compose_into_buffer(head, add_separator);
        // Every byte is ASCII by construction (a validated prefix, `_`, and an ASCII alphabet),
        // so this check always passes; it is what keeps the crate free of `unsafe` here.
        core::str::from_utf8(&buf[..len])
            .expect("identifiers are ASCII")
            .to_owned()
    }

    /// Wraps freshly composed text, whose prefix is `prefix_len` bytes, as an [`Id`]. The
    /// timestamp is read back out of the value rather than threaded through, which costs a few
    /// nanoseconds and keeps one source of truth.
    #[inline]
    fn to_id(text: &[u8], prefix_len: usize) -> Id {
        let (_, timestamp_ms) = value::read_value(&text[prefix_len + 1..])
            .expect("a generated value is always well formed");
        Id::from_parts(text, prefix_len, timestamp_ms)
    }

    /// Writes `head` (and a separator, if `add_separator`) then a fresh value into a stack buffer,
    /// returning the buffer and how much of it is used.
    #[inline]
    fn compose_into_buffer(&self, head: &str, add_separator: bool) -> ([u8; MAX_ID_LEN], usize) {
        let mut buf = [0u8; MAX_ID_LEN];
        let mut len = head.len();
        buf[..len].copy_from_slice(head.as_bytes());
        if add_separator {
            buf[len] = b'_';
            len += 1;
        }
        match self.inner.strategy {
            Strategy::Ulid => {
                let dest: &mut [u8; ULID_LEN] = (&mut buf[len..len + ULID_LEN])
                    .try_into()
                    .expect("the buffer has room for any identifier");
                value::write_ulid(dest);
                len += ULID_LEN;
            }
            Strategy::UuidV7 => {
                let dest: &mut [u8; UUID_LEN] = (&mut buf[len..len + UUID_LEN])
                    .try_into()
                    .expect("the buffer has room for any identifier");
                value::write_uuid_v7(dest);
                len += UUID_LEN;
            }
        }
        (buf, len)
    }
}

impl Default for Idora {
    fn default() -> Self {
        Self::from_parts(Strategy::default(), Vec::new(), TypeIdMap::default())
    }
}

impl fmt::Debug for Idora {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Idora")
            .field("strategy", &self.inner.strategy)
            .field("registrations", &self.inner.registrations)
            .finish()
    }
}

/// A `HashMap` keyed by [`TypeId`].
///
/// A `TypeId` is already a high-quality hash produced by the compiler, so running it through
/// SipHash again only costs time. This hasher passes the bits straight through.
pub(crate) type TypeIdMap<V> = HashMap<TypeId, V, BuildHasherDefault<TypeIdHasher>>;

#[derive(Default)]
pub(crate) struct TypeIdHasher(u64);

impl Hasher for TypeIdHasher {
    #[inline]
    fn write_u64(&mut self, value: u64) {
        self.0 ^= value;
    }

    /// `TypeId` hashes itself with `write_u64`, but its `Hash` implementation is not a stable
    /// promise, so any other shape of input is folded in with a multiplicative mix instead.
    #[inline]
    fn write(&mut self, bytes: &[u8]) {
        for chunk in bytes.chunks(8) {
            let mut word = [0u8; 8];
            word[..chunk.len()].copy_from_slice(chunk);
            self.0 = (self.0.rotate_left(5) ^ u64::from_le_bytes(word))
                .wrapping_mul(0x9E37_79B9_7F4A_7C15);
        }
    }

    #[inline]
    fn finish(&self) -> u64 {
        self.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assert_send_sync<T: Send + Sync>() {}

    #[test]
    fn idora_is_send_and_sync() {
        assert_send_sync::<Idora>();
        assert_send_sync::<Builder>();
        assert_send_sync::<crate::Error>();
    }

    #[test]
    fn the_longest_identifier_fits_the_buffer() {
        let ids = Idora::builder().uuid_v7().build();
        let id = ids.generate_with_prefix("abcdefghijklmnop").unwrap();
        assert_eq!(id.len(), MAX_ID_LEN);
        assert_eq!(id.capacity(), MAX_ID_LEN);
    }

    #[test]
    fn the_hasher_spreads_byte_input() {
        let hash = |bytes: &[u8]| {
            let mut h = TypeIdHasher::default();
            h.write(bytes);
            h.finish()
        };
        assert_ne!(hash(&[1]), hash(&[2]));
        assert_ne!(
            hash(&[1, 0, 0, 0, 0, 0, 0, 0, 1]),
            hash(&[0, 0, 0, 0, 0, 0, 0, 1, 1])
        );
    }
}
