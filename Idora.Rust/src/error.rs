//! The four ways Idora can fail (specification §7).

#![forbid(unsafe_code)]

use core::any::TypeId;
use core::fmt;

use crate::prefix::MAX_PREFIX_LEN;

/// Any error Idora reports.
///
/// Each operation returns the most specific error it can: [`Idora::generate`](crate::Idora::generate)
/// can only fail with a [`PrefixNotRegisteredError`],
/// [`Idora::generate_with_prefix`](crate::Idora::generate_with_prefix) only with an
/// [`InvalidPrefixError`], and [`Id::parse`](crate::Id::parse) only with an [`InvalidIdError`].
/// Registration and the registry-aware [`Idora::parse`](crate::Idora::parse) can fail in two ways
/// and return this enum. Every specific error converts into it with `?`, so a function that does
/// several of these can return `Result<_, idora::Error>`.
///
/// ```
/// use idora::{Error, Idora};
///
/// struct Account;
/// struct Accrual;
///
/// let mut builder = Idora::builder();
/// builder.register::<Account>("acc")?;
///
/// match builder.register::<Accrual>("ACC") {
///     Err(Error::DuplicatePrefix(e)) => {
///         assert_eq!(e.prefix(), "ACC");
///         assert!(e.entity_type_name().ends_with("Accrual"));
///         assert!(e.conflicting_type_name().ends_with("Account"));
///     }
///     other => panic!("expected a duplicate, got {other:?}"),
/// }
/// # Ok::<(), idora::Error>(())
/// ```
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub enum Error {
    /// A prefix broke the rules: empty, too long, or not made of ASCII letters and digits.
    InvalidPrefix(InvalidPrefixError),
    /// A registration would make a prefix ambiguous, or registers a type a second time.
    DuplicatePrefix(DuplicatePrefixError),
    /// An identifier or prefix was asked for a type that was never registered.
    PrefixNotRegistered(PrefixNotRegisteredError),
    /// Text that was parsed as an identifier is not one, or carries the wrong prefix.
    InvalidId(InvalidIdError),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidPrefix(e) => e.fmt(f),
            Self::DuplicatePrefix(e) => e.fmt(f),
            Self::PrefixNotRegistered(e) => e.fmt(f),
            Self::InvalidId(e) => e.fmt(f),
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        // The variant's own message is already this error's message, so the chain continues with
        // whatever the variant itself wraps: the prefix error behind an invalid identifier, and
        // otherwise nothing.
        match self {
            Self::InvalidId(e) => std::error::Error::source(e),
            Self::InvalidPrefix(_) | Self::DuplicatePrefix(_) | Self::PrefixNotRegistered(_) => {
                None
            }
        }
    }
}

impl From<InvalidPrefixError> for Error {
    fn from(error: InvalidPrefixError) -> Self {
        Self::InvalidPrefix(error)
    }
}

impl From<DuplicatePrefixError> for Error {
    fn from(error: DuplicatePrefixError) -> Self {
        Self::DuplicatePrefix(error)
    }
}

impl From<PrefixNotRegisteredError> for Error {
    fn from(error: PrefixNotRegisteredError) -> Self {
        Self::PrefixNotRegistered(error)
    }
}

impl From<InvalidIdError> for Error {
    fn from(error: InvalidIdError) -> Self {
        Self::InvalidId(error)
    }
}

/// Which rule an invalid prefix broke. See [`validate_prefix`](crate::validate_prefix).
///
/// Lengths and indexes count characters (Unicode scalar values), not bytes.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum InvalidPrefixReason {
    /// The prefix is the empty string.
    Empty,
    /// The prefix is longer than [`MAX_PREFIX_LEN`] characters.
    TooLong {
        /// How many characters the prefix has.
        len: usize,
    },
    /// The prefix contains the `_` separator, which divides the prefix from the value.
    Separator {
        /// The zero-based character index of the first separator.
        index: usize,
    },
    /// The prefix contains a character that is not an ASCII letter or digit.
    Character {
        /// The offending character.
        ch: char,
        /// Its zero-based character index.
        index: usize,
    },
}

impl InvalidPrefixReason {
    /// The zero-based character index of the offending character, for
    /// [`Separator`](Self::Separator) and [`Character`](Self::Character).
    #[must_use]
    pub const fn index(self) -> Option<usize> {
        match self {
            Self::Separator { index } | Self::Character { index, .. } => Some(index),
            Self::Empty | Self::TooLong { .. } => None,
        }
    }
}

/// A prefix was not 1 to 16 ASCII letters or digits.
///
/// Returned when registering a prefix at start-up, and by
/// [`Idora::generate_with_prefix`](crate::Idora::generate_with_prefix), which validates on every
/// call. When the prefix came from an [`Entity::PREFIX`](crate::Entity::PREFIX) declaration,
/// [`declaring_type_name`](Self::declaring_type_name) names the type, because there is no line of your code to
/// point at.
///
/// ```
/// let error = idora::validate_prefix("us-r").unwrap_err();
/// assert_eq!(error.prefix(), "us-r");
/// assert_eq!(error.reason(), idora::InvalidPrefixReason::Character { ch: '-', index: 2 });
/// assert_eq!(
///     error.to_string(),
///     "The entity prefix 'us-r' contains the character '-' at index 2. \
///      Prefixes may contain only ASCII letters and digits."
/// );
/// ```
#[derive(Clone, PartialEq, Eq)]
pub struct InvalidPrefixError {
    prefix: String,
    reason: InvalidPrefixReason,
    declared_by: Option<&'static str>,
}

/// Written by hand so that `{:?}`, which is how `main` reports an error it returns, quotes the
/// prefix shortened like the message does: it may be untrusted input of any length.
impl fmt::Debug for InvalidPrefixError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("InvalidPrefixError")
            .field("prefix", &format_args!("{}", Quoted(&self.prefix)))
            .field("reason", &self.reason)
            .field("declared_by", &self.declared_by)
            .finish()
    }
}

impl InvalidPrefixError {
    #[cold]
    pub(crate) fn new(prefix: &str, reason: InvalidPrefixReason) -> Self {
        Self {
            prefix: prefix.to_owned(),
            reason,
            declared_by: None,
        }
    }

    pub(crate) fn declared_by(mut self, type_name: &'static str) -> Self {
        self.declared_by = Some(type_name);
        self
    }

    /// The prefix that was rejected, exactly as supplied.
    #[must_use]
    pub fn prefix(&self) -> &str {
        &self.prefix
    }

    /// Which rule the prefix broke.
    #[must_use]
    pub fn reason(&self) -> InvalidPrefixReason {
        self.reason
    }

    /// The [`std::any::type_name`] of the type whose [`Entity::PREFIX`](crate::Entity::PREFIX)
    /// declared this prefix, or `None` if it was passed in directly.
    #[must_use]
    pub fn declaring_type_name(&self) -> Option<&'static str> {
        self.declared_by
    }
}

impl fmt::Display for InvalidPrefixError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if let Some(type_name) = self.declared_by {
            write!(
                f,
                "The Entity::PREFIX declared by '{type_name}' is not a valid prefix. "
            )?;
        }
        let prefix = &self.prefix;
        match self.reason {
            InvalidPrefixReason::Empty => write!(
                f,
                "An entity prefix cannot be empty. Supply 1 to {MAX_PREFIX_LEN} ASCII letters or \
                 digits, for example \"usr\"."
            ),
            // Quoted shortened: a prefix parsed out of an identifier is untrusted input of any length.
            InvalidPrefixReason::TooLong { len } => write!(
                f,
                "The entity prefix {quoted} is {len} characters long; the maximum is \
                 {MAX_PREFIX_LEN}. Use a shorter abbreviation.",
                quoted = Quoted(prefix)
            ),
            InvalidPrefixReason::Separator { index } => write!(
                f,
                "The entity prefix '{prefix}' contains the separator '_' at index {index}. The \
                 separator divides the prefix from the value, so a prefix cannot contain one."
            ),
            InvalidPrefixReason::Character { ch, index } => write!(
                f,
                "The entity prefix '{prefix}' contains the character '{ch}' at index {index}. \
                 Prefixes may contain only ASCII letters and digits."
            ),
        }
    }
}

impl std::error::Error for InvalidPrefixError {}

/// Which kind of clash a [`DuplicatePrefixError`] describes (specification §4.3).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum DuplicateKind {
    /// The type was already registered with this very prefix: the mapping was declared twice.
    TypeRegisteredTwice,
    /// The type was already registered with a different prefix.
    TypeRegisteredWithDifferentPrefix,
    /// The prefix, compared ignoring ASCII case, already belongs to a different type.
    PrefixTaken,
}

/// A registration would leave a prefix not identifying exactly one type.
///
/// Two mistakes produce this. Giving one prefix to two types (compared ignoring ASCII case, so
/// `usr` and `USR` collide) would defeat the point of a prefix. Registering one type twice is
/// almost always a copy-paste slip; it is reported even when both registrations agree, because one
/// type with two sources of truth is worth hearing about. It does not matter which route each
/// registration took: [`register`](crate::Builder::register), an [`Entity`](crate::Entity)
/// declaration, or one of each.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DuplicatePrefixError {
    kind: DuplicateKind,
    prefix: String,
    existing_prefix: String,
    entity: TypeInfo,
    conflicting: TypeInfo,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct TypeInfo {
    pub(crate) id: TypeId,
    pub(crate) name: &'static str,
}

impl DuplicatePrefixError {
    #[cold]
    pub(crate) fn type_registered(entity: TypeInfo, existing: &str, attempted: &str) -> Self {
        Self {
            kind: if existing == attempted {
                DuplicateKind::TypeRegisteredTwice
            } else {
                DuplicateKind::TypeRegisteredWithDifferentPrefix
            },
            prefix: attempted.to_owned(),
            existing_prefix: existing.to_owned(),
            entity,
            conflicting: entity,
        }
    }

    #[cold]
    pub(crate) fn prefix_taken(
        attempted: &str,
        entity: TypeInfo,
        existing: &str,
        conflicting: TypeInfo,
    ) -> Self {
        Self {
            kind: DuplicateKind::PrefixTaken,
            prefix: attempted.to_owned(),
            existing_prefix: existing.to_owned(),
            entity,
            conflicting,
        }
    }

    /// Which kind of clash this is.
    #[must_use]
    pub fn kind(&self) -> DuplicateKind {
        self.kind
    }

    /// The prefix that was being registered.
    #[must_use]
    pub fn prefix(&self) -> &str {
        &self.prefix
    }

    /// The prefix already held by [`conflicting_type_name`](Self::conflicting_type_name). For a
    /// [`PrefixTaken`](DuplicateKind::PrefixTaken) clash it can differ from
    /// [`prefix`](Self::prefix) in case (`usr` versus `USR`).
    #[must_use]
    pub fn existing_prefix(&self) -> &str {
        &self.existing_prefix
    }

    /// The type being registered when the clash was detected.
    #[must_use]
    pub fn entity_type_id(&self) -> TypeId {
        self.entity.id
    }

    /// The [`std::any::type_name`] of the type being registered.
    #[must_use]
    pub fn entity_type_name(&self) -> &'static str {
        self.entity.name
    }

    /// The type that already held the prefix. When one type is registered twice this is the same
    /// as [`entity_type_id`](Self::entity_type_id).
    #[must_use]
    pub fn conflicting_type_id(&self) -> TypeId {
        self.conflicting.id
    }

    /// The [`std::any::type_name`] of the type that already held the prefix.
    #[must_use]
    pub fn conflicting_type_name(&self) -> &'static str {
        self.conflicting.name
    }
}

impl fmt::Display for DuplicatePrefixError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let entity = self.entity.name;
        let prefix = &self.prefix;
        let existing = &self.existing_prefix;
        match self.kind {
            DuplicateKind::TypeRegisteredTwice => write!(
                f,
                "The entity type '{entity}' is already registered with the prefix '{existing}', and \
                 cannot be registered a second time. Each entity type may be registered exactly \
                 once. Look for a repeated register call, a type implementing Entity that is also \
                 registered by hand, or the same type listed in two register_entities calls."
            ),
            DuplicateKind::TypeRegisteredWithDifferentPrefix => write!(
                f,
                "The entity type '{entity}' is already registered with the prefix '{existing}' and \
                 cannot also be registered with '{prefix}'. Each entity type may have only one \
                 prefix. Remove whichever registration is wrong: a register call, the type's \
                 Entity::PREFIX, or a register_entities call that lists the type."
            ),
            DuplicateKind::PrefixTaken => write!(
                f,
                "The entity prefix '{prefix}' is already registered for '{conflicting}' and cannot \
                 also be used for '{entity}'. Prefixes must identify exactly one entity type, and \
                 are compared ignoring ASCII case. Choose a different prefix for '{entity}'.",
                conflicting = self.conflicting.name,
            ),
        }
    }
}

impl std::error::Error for DuplicatePrefixError {}

/// A type was used that has no registered prefix.
///
/// [`Idora::generate`](crate::Idora::generate) deliberately refuses to invent a prefix from the
/// type's name: that would change the day somebody renames the type, and two types with the same
/// short name in different modules would quietly collide. Register the type when building the
/// generator, or use [`Idora::generate_with_prefix`](crate::Idora::generate_with_prefix) if the
/// prefix really is only known at run time.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PrefixNotRegisteredError {
    entity: TypeInfo,
}

impl PrefixNotRegisteredError {
    #[cold]
    #[inline(never)]
    pub(crate) fn of<T: ?Sized + 'static>() -> Self {
        Self {
            entity: TypeInfo {
                id: TypeId::of::<T>(),
                name: core::any::type_name::<T>(),
            },
        }
    }

    /// The type that has no registered prefix.
    #[must_use]
    pub fn entity_type_id(&self) -> TypeId {
        self.entity.id
    }

    /// The [`std::any::type_name`] of the type that has no registered prefix.
    #[must_use]
    pub fn entity_type_name(&self) -> &'static str {
        self.entity.name
    }
}

impl fmt::Display for PrefixNotRegisteredError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let name = self.entity.name;
        write!(
            f,
            "No entity prefix is registered for '{name}'. Register one when building the \
             generator, for example Idora::builder().register::<{short}>(\"abc\"), or implement \
             Entity for it and call register_entity::<{short}>(); or call generate_with_prefix \
             with an explicit prefix.",
            short = short_type_name(name),
        )
    }
}

impl std::error::Error for PrefixNotRegisteredError {}

/// Why a piece of text is not an acceptable identifier (specification §6.1).
///
/// The checks run in the order of the variants, and the first failure is reported.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub enum InvalidIdReason {
    /// The text contains no `_`, so it has no prefix and no value.
    NoSeparator,
    /// Everything before the first `_` is not a valid prefix. The [`InvalidPrefixError`] says which
    /// rule it breaks, and is also this error's [`source`](std::error::Error::source).
    Prefix(InvalidPrefixError),
    /// Everything after the first `_` is neither a 26-character lowercase ULID nor a 32-character
    /// lowercase UUIDv7.
    Value,
    /// The identifier is well formed, but its prefix is not exactly the one the caller expected.
    PrefixMismatch {
        /// The prefix the caller asked for.
        expected: String,
    },
}

/// Text that was parsed as an identifier is not one, or is one for a different entity.
///
/// Returned by [`Id::parse`](crate::Id::parse) and [`Id::parse_with_prefix`](crate::Id::parse_with_prefix),
/// by [`str::parse`] into an [`Id`](crate::Id), and inside [`Error::InvalidId`] by the
/// registry-aware [`Idora::parse`](crate::Idora::parse). Parsing is strict: it accepts exactly the
/// text a conforming generator emits, with no trimming and no case folding.
///
/// The text usually comes from outside (a URL, a request body, a message), so the message quotes
/// it with control characters escaped and anything past 64 characters elided. [`text`](Self::text)
/// returns it untouched.
///
/// ```
/// use idora::{Id, InvalidIdReason};
///
/// let error = Id::parse_with_prefix("usr_01k226e0g0008j4ct4ank7f24s", "ord").unwrap_err();
/// assert_eq!(error.text(), "usr_01k226e0g0008j4ct4ank7f24s");
/// assert_eq!(error.expected_prefix(), Some("ord"));
/// assert!(matches!(error.reason(), InvalidIdReason::PrefixMismatch { .. }));
/// assert_eq!(
///     error.to_string(),
///     "The identifier 'usr_01k226e0g0008j4ct4ank7f24s' has the prefix 'usr', but an identifier \
///      with the prefix 'ord' was expected. Prefixes are compared exactly, case included; check \
///      that the identifier refers to the right kind of entity."
/// );
/// ```
#[derive(Clone, PartialEq, Eq)]
pub struct InvalidIdError {
    text: String,
    reason: InvalidIdReason,
}

/// Written by hand so that `{:?}`, which is how `main` reports an error it returns, quotes the
/// text shortened like the message does: it is usually untrusted input, of any length.
impl fmt::Debug for InvalidIdError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("InvalidIdError")
            .field("text", &format_args!("{}", Quoted(&self.text)))
            .field("reason", &self.reason)
            .finish()
    }
}

impl InvalidIdError {
    #[cold]
    #[inline(never)]
    pub(crate) fn new(text: &str, reason: InvalidIdReason) -> Self {
        Self {
            text: text.to_owned(),
            reason,
        }
    }

    /// The text that was rejected, exactly as supplied.
    #[must_use]
    pub fn text(&self) -> &str {
        &self.text
    }

    /// Why the text was rejected.
    #[must_use]
    pub fn reason(&self) -> &InvalidIdReason {
        &self.reason
    }

    /// The prefix the caller expected, for [`InvalidIdReason::PrefixMismatch`].
    #[must_use]
    pub fn expected_prefix(&self) -> Option<&str> {
        match &self.reason {
            InvalidIdReason::PrefixMismatch { expected } => Some(expected),
            _ => None,
        }
    }

    /// The text before the first `_`, if there is one.
    fn prefix_part(&self) -> &str {
        self.text.split_once('_').map_or("", |(prefix, _)| prefix)
    }
}

impl fmt::Display for InvalidIdError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let text = Quoted(&self.text);
        match &self.reason {
            InvalidIdReason::NoSeparator => write!(
                f,
                "The text {text} is not an Idora identifier: it contains no '_' separator. An \
                 identifier is a prefix, an underscore and a value, for example \
                 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'."
            ),
            InvalidIdReason::Prefix(_) => write!(
                f,
                "The identifier {text} does not begin with a valid prefix: everything before the \
                 first '_' must be 1 to {MAX_PREFIX_LEN} ASCII letters or digits."
            ),
            InvalidIdReason::Value => write!(
                f,
                "The identifier {text} does not end in a valid value: everything after the first \
                 '_' must be a 26-character lowercase ULID or a 32-character lowercase UUIDv7 \
                 without hyphens. Identifiers are case-sensitive and are never trimmed."
            ),
            InvalidIdReason::PrefixMismatch { expected } => write!(
                f,
                "The identifier {text} has the prefix {prefix}, but an identifier with the prefix \
                 {expected} was expected. Prefixes are compared exactly, case included; check \
                 that the identifier refers to the right kind of entity.",
                prefix = Quoted(self.prefix_part()),
                expected = Quoted(expected),
            ),
        }
    }
}

impl std::error::Error for InvalidIdError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match &self.reason {
            InvalidIdReason::Prefix(e) => Some(e),
            _ => None,
        }
    }
}

/// Writes untrusted text between single quotes, escaping control characters and quotes, and
/// eliding everything past the first [`Quoted::LIMIT`] characters.
struct Quoted<'a>(&'a str);

impl Quoted<'_> {
    /// Comfortably more than the longest identifier (49 characters).
    const LIMIT: usize = 64;
}

impl fmt::Display for Quoted<'_> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("'")?;
        for ch in self.0.chars().take(Self::LIMIT) {
            if ch == '\'' {
                f.write_str("\\'")?;
            } else {
                write!(f, "{}", ch.escape_debug())?;
            }
        }
        f.write_str("'")?;
        if self.0.chars().nth(Self::LIMIT).is_some() {
            write!(f, " (truncated, {} characters)", self.0.chars().count())?;
        }
        Ok(())
    }
}

/// `my_app::model::User` becomes `User`; `my_app::Wrapper<my_app::User>` becomes
/// `Wrapper<my_app::User>`.
fn short_type_name(name: &str) -> &str {
    let head = name.find('<').map_or(name, |generic| &name[..generic]);
    match head.rfind("::") {
        Some(sep) => &name[sep + 2..],
        None => name,
    }
}

#[cfg(test)]
mod tests {
    use super::{Quoted, short_type_name};

    #[test]
    fn quoted_escapes_and_truncates_untrusted_text() {
        assert_eq!(Quoted("usr_1").to_string(), "'usr_1'");
        assert_eq!(Quoted("a\nb'c\"").to_string(), r#"'a\nb\'c\"'"#);
        assert_eq!(Quoted("café").to_string(), "'café'");
        let long = "x".repeat(100);
        assert_eq!(
            Quoted(&long).to_string(),
            format!("'{}' (truncated, 100 characters)", "x".repeat(64))
        );
        assert_eq!(
            Quoted(&long[..64]).to_string(),
            format!("'{}'", "x".repeat(64))
        );
    }

    #[test]
    fn short_type_name_strips_only_the_outer_path() {
        assert_eq!(short_type_name("a::b::User"), "User");
        assert_eq!(short_type_name("User"), "User");
        assert_eq!(short_type_name("a::Wrap<b::User>"), "Wrap<b::User>");
    }
}
