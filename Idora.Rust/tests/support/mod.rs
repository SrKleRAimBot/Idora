//! Helpers shared by the integration tests. Each test file uses a different subset.

#![allow(dead_code, clippy::cast_possible_truncation)]

use std::time::{SystemTime, UNIX_EPOCH};

pub const CROCKFORD: &str = "0123456789abcdefghjkmnpqrstvwxyz";

/// Decodes a 26-character lowercase ULID back into its 128 bits, independently of the crate.
pub fn decode_ulid(value: &str) -> u128 {
    assert_eq!(value.len(), 26, "a ULID is 26 characters: {value:?}");
    value.chars().fold(0u128, |acc, c| {
        let digit = CROCKFORD
            .find(c)
            .unwrap_or_else(|| panic!("{c:?} is not lowercase Crockford Base32 in {value:?}"));
        (acc << 5) | digit as u128
    })
}

/// The millisecond timestamp in the first 10 characters of a ULID.
pub fn ulid_timestamp(value: &str) -> u64 {
    (decode_ulid(value) >> 80) as u64
}

/// The millisecond timestamp in the first 12 hex characters of a UUIDv7.
pub fn uuid_timestamp(value: &str) -> u64 {
    u64::from_str_radix(&value[..12], 16).unwrap()
}

/// Splits an identifier on its first underscore.
pub fn split(id: &str) -> (&str, &str) {
    id.split_once('_')
        .unwrap_or_else(|| panic!("{id:?} has no separator"))
}

pub fn unix_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_millis() as u64
}

pub fn is_ulid(value: &str) -> bool {
    value.len() == 26 && value.chars().all(|c| CROCKFORD.contains(c)) && value.as_bytes()[0] <= b'7'
}

pub fn is_uuid_v7(value: &str) -> bool {
    value.len() == 32
        && value
            .bytes()
            .all(|c| matches!(c, b'0'..=b'9' | b'a'..=b'f'))
        && value.as_bytes()[12] == b'7'
        && matches!(value.as_bytes()[16], b'8' | b'9' | b'a' | b'b')
}

/// Decodes a hex string into bytes.
pub fn hex(text: &str) -> Vec<u8> {
    (0..text.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&text[i..i + 2], 16).unwrap())
        .collect()
}

pub fn threads() -> usize {
    std::thread::available_parallelism().map_or(4, |n| n.get().max(2))
}
