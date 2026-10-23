//! Prefix validation (specification §2) through every public route.

use idora::{Error, Idora, InvalidPrefixReason, MAX_PREFIX_LEN, SEPARATOR, validate_prefix};

struct User;

const INVALID: &[&str] = &[
    "",
    "abcdefghijklmnopq",
    "user_account",
    "_",
    "usr_",
    "us-r",
    "us r",
    " usr",
    "usr!",
    "usr.",
    "café",
    "١٢",
    "ＵＳＲ",
    "usr\n",
    "\0",
];

const VALID: &[&str] = &[
    "a",
    "Z",
    "9",
    "usr",
    "USR",
    "Usr1",
    "abcdefghijklmnop",
    "0123456789",
];

#[test]
fn the_constants_describe_the_rules() {
    assert_eq!(MAX_PREFIX_LEN, 16);
    assert_eq!(SEPARATOR, '_');
}

#[test]
fn register_rejects_every_invalid_prefix_and_reports_it() {
    for &prefix in INVALID {
        match Idora::builder().register::<User>(prefix) {
            Err(Error::InvalidPrefix(e)) => {
                assert_eq!(e.prefix(), prefix);
                assert_eq!(e.declaring_type_name(), None);
            }
            other => panic!("{prefix:?}: {other:?}"),
        }
    }
}

#[test]
fn register_accepts_every_valid_prefix() {
    for &prefix in VALID {
        assert!(
            Idora::builder().register::<User>(prefix).is_ok(),
            "{prefix}"
        );
    }
}

#[test]
fn generate_with_prefix_rejects_every_invalid_prefix() {
    let ids = Idora::new();
    for &prefix in INVALID {
        let error = ids.generate_with_prefix(prefix).unwrap_err();
        assert_eq!(error.prefix(), prefix);
    }
}

#[test]
fn generate_with_prefix_uses_every_valid_prefix() {
    let ids = Idora::new();
    for &prefix in VALID {
        let id = ids.generate_with_prefix(prefix).unwrap();
        assert_eq!(id.split_once('_').unwrap().0, prefix);
    }
}

#[test]
fn the_separator_gets_its_own_explanation() {
    let error = validate_prefix("user_account").unwrap_err();
    assert_eq!(error.reason(), InvalidPrefixReason::Separator { index: 4 });
    assert_eq!(error.reason().index(), Some(4));
    let message = error.to_string();
    assert!(message.contains("separator '_' at index 4"), "{message}");
    assert!(
        message.contains("divides the prefix from the value"),
        "{message}"
    );
}

#[test]
fn an_overlong_prefix_reports_both_lengths() {
    let error = validate_prefix("abcdefghijklmnopqrstu").unwrap_err();
    assert_eq!(error.reason(), InvalidPrefixReason::TooLong { len: 21 });
    assert_eq!(error.reason().index(), None);
    let message = error.to_string();
    assert!(message.contains("21 characters"), "{message}");
    assert!(message.contains("maximum is 16"), "{message}");
}

#[test]
fn an_empty_prefix_says_what_to_supply() {
    let message = validate_prefix("").unwrap_err().to_string();
    assert!(message.contains("cannot be empty"), "{message}");
    assert!(message.contains("\"usr\""), "{message}");
}

#[test]
fn a_bad_character_is_named_with_its_index() {
    let error = validate_prefix("usr!").unwrap_err();
    assert_eq!(
        error.reason(),
        InvalidPrefixReason::Character { ch: '!', index: 3 }
    );
    assert!(error.to_string().contains("'!' at index 3"));
}

#[test]
fn non_ascii_letters_and_digits_are_rejected_and_indexed_by_character() {
    assert_eq!(
        validate_prefix("café").unwrap_err().reason(),
        InvalidPrefixReason::Character { ch: 'é', index: 3 }
    );
    assert_eq!(
        validate_prefix("١٢").unwrap_err().reason(),
        InvalidPrefixReason::Character { ch: '١', index: 0 }
    );
    // Fullwidth Latin letters are letters to Unicode, but not to Idora.
    assert_eq!(
        validate_prefix("ＵＳＲ").unwrap_err().reason(),
        InvalidPrefixReason::Character {
            ch: 'Ｕ', index: 0
        }
    );
}

#[test]
fn length_is_counted_in_characters() {
    // 16 characters but 17 bytes: within the length limit, so the é is what is reported.
    let prefix = "abcdefghijklmnoé";
    assert_eq!(prefix.chars().count(), 16);
    assert_eq!(
        validate_prefix(prefix).unwrap_err().reason(),
        InvalidPrefixReason::Character {
            ch: 'é', index: 15
        }
    );
}

#[test]
fn errors_convert_into_the_umbrella_error() {
    fn adhoc(ids: &Idora) -> Result<String, Error> {
        Ok(ids.generate_with_prefix("bad_")?)
    }
    let error = adhoc(&Idora::new()).unwrap_err();
    assert!(matches!(error, Error::InvalidPrefix(_)));
    // The umbrella error prints the same message.
    assert_eq!(
        error.to_string(),
        validate_prefix("bad_").unwrap_err().to_string()
    );
    let _: &dyn std::error::Error = &error;
}
