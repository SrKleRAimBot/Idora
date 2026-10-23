//! The rules an entity prefix must satisfy (specification §2).

#![forbid(unsafe_code)]

use crate::error::{InvalidPrefixError, InvalidPrefixReason};

/// The character that separates the prefix from the value: `usr_01k2…`.
pub const SEPARATOR: char = '_';

/// The longest prefix allowed, in characters.
///
/// Long enough for a readable abbreviation, short enough that the prefix never dominates the
/// identifier.
pub const MAX_PREFIX_LEN: usize = 16;

/// Returns `true` if `prefix` is a valid entity prefix: 1 to [`MAX_PREFIX_LEN`] ASCII letters or
/// digits.
///
/// This is a `const fn`, so a prefix can be checked at compile time. The [`entity!`](crate::entity)
/// macro does exactly that. To find out *why* a prefix is invalid, use [`validate_prefix`].
///
/// ```
/// const _: () = assert!(idora::is_valid_prefix("usr"));
///
/// assert!(idora::is_valid_prefix("Usr1"));
/// assert!(!idora::is_valid_prefix(""));
/// assert!(!idora::is_valid_prefix("user_account"));
/// assert!(!idora::is_valid_prefix("café"));
/// ```
#[must_use]
pub const fn is_valid_prefix(prefix: &str) -> bool {
    let bytes = prefix.as_bytes();
    if bytes.is_empty() || bytes.len() > MAX_PREFIX_LEN {
        return false;
    }
    let mut i = 0;
    while i < bytes.len() {
        if !bytes[i].is_ascii_alphanumeric() {
            return false;
        }
        i += 1;
    }
    true
}

/// Checks `prefix` against the rules, reporting the first one it breaks.
///
/// The rules are checked in this order:
///
/// 1. it must not be empty ([`InvalidPrefixReason::Empty`]);
/// 2. it must be at most [`MAX_PREFIX_LEN`] characters ([`InvalidPrefixReason::TooLong`]);
/// 3. every character must be an ASCII letter or digit. The first one that is not is reported
///    with its zero-based index, as [`InvalidPrefixReason::Separator`] if it is the `_` separator
///    and [`InvalidPrefixReason::Character`] otherwise.
///
/// Lengths and indexes count characters (Unicode scalar values), not bytes, so `"café"` is four
/// characters long and its bad character is at index 3. Non-ASCII letters and digits such as `é`
/// or `١` are always rejected.
///
/// This does not allocate unless it fails.
///
/// # Errors
///
/// Returns an [`InvalidPrefixError`] describing the first rule `prefix` breaks.
///
/// ```
/// use idora::{InvalidPrefixReason, validate_prefix};
///
/// assert!(validate_prefix("usr").is_ok());
///
/// let error = validate_prefix("user_account").unwrap_err();
/// assert_eq!(error.reason(), InvalidPrefixReason::Separator { index: 4 });
/// ```
pub fn validate_prefix(prefix: &str) -> Result<(), InvalidPrefixError> {
    match check(prefix) {
        None => Ok(()),
        Some(reason) => Err(InvalidPrefixError::new(prefix, reason)),
    }
}

/// The allocation-free core of [`validate_prefix`].
#[inline]
pub(crate) fn check(prefix: &str) -> Option<InvalidPrefixReason> {
    let bytes = prefix.as_bytes();

    // The fast path, taken by every valid prefix: short and entirely ASCII alphanumeric.
    if !bytes.is_empty()
        && bytes.len() <= MAX_PREFIX_LEN
        && bytes.iter().all(u8::is_ascii_alphanumeric)
    {
        return None;
    }

    Some(diagnose(prefix))
}

/// Works out which rule an invalid prefix breaks. Kept out of line: it only runs on failure.
#[cold]
#[inline(never)]
fn diagnose(prefix: &str) -> InvalidPrefixReason {
    if prefix.is_empty() {
        return InvalidPrefixReason::Empty;
    }

    // A prefix of more than 16 bytes may still be 16 characters or fewer if some are multi-byte;
    // those are rejected below for the character instead, as the specification orders the rules.
    let len = prefix.chars().count();
    if len > MAX_PREFIX_LEN {
        return InvalidPrefixReason::TooLong { len };
    }

    for (index, ch) in prefix.chars().enumerate() {
        if ch == SEPARATOR {
            return InvalidPrefixReason::Separator { index };
        }
        if !ch.is_ascii_alphanumeric() {
            return InvalidPrefixReason::Character { ch, index };
        }
    }

    unreachable!("diagnose is only called for prefixes that fail the fast path")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_letters_and_digits_up_to_the_limit() {
        for prefix in ["a", "Z", "0", "usr", "USR", "Usr1", "abcdefghijklmnop"] {
            assert_eq!(check(prefix), None, "{prefix}");
            assert!(is_valid_prefix(prefix), "{prefix}");
        }
    }

    #[test]
    fn reports_the_first_failing_rule_in_order() {
        assert_eq!(check(""), Some(InvalidPrefixReason::Empty));
        // Too long wins over the bad characters it also contains.
        assert_eq!(
            check("bad_prefix_that_is_long"),
            Some(InvalidPrefixReason::TooLong { len: 23 })
        );
        assert_eq!(
            check("a_b-c"),
            Some(InvalidPrefixReason::Separator { index: 1 })
        );
        assert_eq!(
            check("a-b_c"),
            Some(InvalidPrefixReason::Character { ch: '-', index: 1 })
        );
    }

    #[test]
    fn counts_characters_not_bytes() {
        // 9 characters, 18 bytes: not too long, but é is not ASCII.
        assert_eq!(
            check("ééééééééé"),
            Some(InvalidPrefixReason::Character { ch: 'é', index: 0 })
        );
        assert_eq!(
            check("ééééééééééééééééé"),
            Some(InvalidPrefixReason::TooLong { len: 17 })
        );
        assert_eq!(
            check("ab١"),
            Some(InvalidPrefixReason::Character { ch: '١', index: 2 })
        );
    }

    #[test]
    fn const_and_runtime_checks_agree() {
        for prefix in [
            "",
            "a",
            "a_",
            "café",
            "١٢",
            "abcdefghijklmnopq",
            "us r",
            "\0",
        ] {
            assert_eq!(
                is_valid_prefix(prefix),
                check(prefix).is_none(),
                "{prefix:?}"
            );
        }
    }
}
