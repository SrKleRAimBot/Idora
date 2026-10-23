//! The value half of an identifier: ULID (specification §3.1) and UUIDv7 (§3.2).
//!
//! Everything here except [`write_ulid`] and [`write_uuid_v7`] is a pure function of its inputs,
//! so the encodings and the monotonic step can be checked against the shared conformance vectors
//! with known clocks and known randomness.

#![forbid(unsafe_code)]

use std::time::{SystemTime, UNIX_EPOCH};

use crate::thread_state;

/// Number of characters in an encoded ULID.
pub(crate) const ULID_LEN: usize = 26;

/// Number of characters in a hyphen-free UUID.
pub(crate) const UUID_LEN: usize = 32;

/// The lowercase Crockford Base32 alphabet: digits and letters, less `i l o u`.
const CROCKFORD: &[u8; 32] = b"0123456789abcdefghjkmnpqrstvwxyz";

const HEX: &[u8; 16] = b"0123456789abcdef";

/// The largest value the 80-bit ULID random field can hold.
pub(crate) const MAX_RANDOM: u128 = (1 << 80) - 1;

const TIMESTAMP_MASK: u64 = (1 << 48) - 1;

/// Reads the wall clock as milliseconds since the Unix epoch.
///
/// `SystemTime` is an absolute instant with no time zone attached, so this is UTC by construction;
/// nothing here ever reads local time. A clock set before 1970 reads as 0, and the monotonic step
/// then carries on from the last value it issued.
#[inline]
pub(crate) fn now_ms() -> u64 {
    match SystemTime::now().duration_since(UNIX_EPOCH) {
        // u64 milliseconds last for 584 million years; the cast cannot truncate in practice.
        #[allow(clippy::cast_possible_truncation)]
        Ok(elapsed) => elapsed.as_millis() as u64,
        Err(_) => 0,
    }
}

/// The state of one monotonic ULID stream: the last timestamp issued and its 80-bit random field.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) struct UlidState {
    pub(crate) last_ms: u64,
    pub(crate) random: u128,
}

impl UlidState {
    /// Advances the stream to clock reading `now_ms`, exactly as §3.1 describes. `fresh` is only
    /// called when a new random field is needed.
    #[inline]
    #[must_use]
    pub(crate) fn step(self, now_ms: u64, fresh: impl FnOnce() -> u128) -> Self {
        if now_ms > self.last_ms {
            Self {
                last_ms: now_ms,
                random: fresh() & MAX_RANDOM,
            }
        } else if self.random < MAX_RANDOM {
            // The same millisecond, or a clock that stepped backwards: keep the timestamp and count.
            Self {
                last_ms: self.last_ms,
                random: self.random + 1,
            }
        } else {
            // 2^80 values in one millisecond. Borrow the next millisecond rather than wrap.
            Self {
                last_ms: self.last_ms + 1,
                random: fresh() & MAX_RANDOM,
            }
        }
    }
}

/// Encodes a ULID as 26 lowercase Crockford Base32 characters.
///
/// The 128 bits are one big-endian number right-aligned in 130 bits, so the first character never
/// exceeds `7`. Both fields fall on character boundaries: characters 0–9 carry the 48-bit
/// timestamp and characters 10–25 the 80-bit random field.
#[inline]
#[must_use]
pub(crate) fn encode_ulid(timestamp_ms: u64, random: u128) -> [u8; ULID_LEN] {
    let mut out = [0u8; ULID_LEN];
    let ts = timestamp_ms & TIMESTAMP_MASK;
    for (i, slot) in out[..10].iter_mut().enumerate() {
        *slot = CROCKFORD[((ts >> (45 - 5 * i)) & 0x1F) as usize];
    }
    // Split the random field so the arithmetic stays in 64-bit registers.
    #[allow(clippy::cast_possible_truncation)]
    let (high, low) = (((random >> 64) & 0xFFFF) as u64, random as u64);
    out[10] = CROCKFORD[((high >> 11) & 0x1F) as usize];
    out[11] = CROCKFORD[((high >> 6) & 0x1F) as usize];
    out[12] = CROCKFORD[((high >> 1) & 0x1F) as usize];
    #[allow(clippy::cast_possible_truncation)] // at most 31
    let straddle = (((high & 1) << 4) | (low >> 60)) as usize;
    out[13] = CROCKFORD[straddle];
    for (i, slot) in out[14..].iter_mut().enumerate() {
        *slot = CROCKFORD[((low >> (55 - 5 * i)) & 0x1F) as usize];
    }
    out
}

/// Lays out a version 7 UUID from a timestamp and the ten bytes destined for octets 6–15. The
/// version and variant bits are written over the top of them.
#[inline]
#[must_use]
pub(crate) fn compose_uuid_v7(timestamp_ms: u64, random: [u8; 10]) -> [u8; 16] {
    let mut bytes = [0u8; 16];
    bytes[..6].copy_from_slice(&(timestamp_ms & TIMESTAMP_MASK).to_be_bytes()[2..]);
    bytes[6..].copy_from_slice(&random);
    bytes[6] = 0x70 | (bytes[6] & 0x0F);
    bytes[8] = 0x80 | (bytes[8] & 0x3F);
    bytes
}

/// Writes 16 bytes as 32 lowercase hexadecimal characters.
#[inline]
#[must_use]
pub(crate) fn encode_hex(bytes: [u8; 16]) -> [u8; UUID_LEN] {
    let mut out = [0u8; UUID_LEN];
    for (pair, byte) in out.chunks_exact_mut(2).zip(bytes) {
        pair[0] = HEX[usize::from(byte >> 4)];
        pair[1] = HEX[usize::from(byte & 0x0F)];
    }
    out
}

/// Writes the next ULID from this thread's monotonic stream.
#[inline]
pub(crate) fn write_ulid(dest: &mut [u8; ULID_LEN]) {
    let now = now_ms();
    let state = thread_state::next_ulid(now);
    *dest = encode_ulid(state.last_ms, state.random);
}

/// Writes a fresh UUIDv7 stamped with the current time.
#[inline]
pub(crate) fn write_uuid_v7(dest: &mut [u8; UUID_LEN]) {
    let now = now_ms();
    let random = thread_state::random_bytes::<10>();
    *dest = encode_hex(compose_uuid_v7(now, random));
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(bytes: &[u8]) -> &str {
        core::str::from_utf8(bytes).unwrap()
    }

    /// An independent, deliberately naive decoder: 26 characters back to 128 bits.
    fn decode_ulid(text: &str) -> u128 {
        text.bytes().fold(0u128, |acc, c| {
            let digit = CROCKFORD.iter().position(|&a| a == c).unwrap() as u128;
            (acc << 5) | digit
        })
    }

    #[test]
    fn everything_zero_is_twenty_six_zeros() {
        assert_eq!(s(&encode_ulid(0, 0)), "0".repeat(26));
    }

    #[test]
    fn every_bit_set_is_the_largest_value() {
        assert_eq!(
            s(&encode_ulid(TIMESTAMP_MASK, MAX_RANDOM)),
            "7zzzzzzzzzzzzzzzzzzzzzzzzz"
        );
    }

    #[test]
    fn a_small_timestamp_lands_in_the_first_ten_characters() {
        assert_eq!(s(&encode_ulid(1, 0)[..10]), "0000000001");
        assert_eq!(s(&encode_ulid(32, 0)[..10]), "0000000010");
        assert_eq!(s(&encode_ulid(31, 0)[..10]), "000000000z");
    }

    #[test]
    fn a_small_random_value_lands_at_the_end() {
        assert_eq!(s(&encode_ulid(0, 1)[10..]), "0000000000000001");
        assert_eq!(s(&encode_ulid(0, 33)[10..]), "0000000000000011");
    }

    #[test]
    fn the_high_random_word_follows_the_timestamp() {
        // Bit 79 alone is the top bit of character 10.
        assert_eq!(s(&encode_ulid(0, 1 << 79)[10..]), "g000000000000000");
        // Bit 64 is the top bit of character 13, which straddles the two words.
        assert_eq!(s(&encode_ulid(0, 1 << 64)[10..]), "000g000000000000");
    }

    #[test]
    fn round_trips_through_an_independent_decoder() {
        let samples = [
            (0u64, 0u128),
            (1_754_568_000_000, 0x0011_2233_4455_6677_8899),
            (TIMESTAMP_MASK, MAX_RANDOM),
            (123_456_789, 0xdead_beef_cafe_f00d_1234),
        ];
        for (ts, random) in samples {
            let decoded = decode_ulid(s(&encode_ulid(ts, random)));
            assert_eq!(decoded >> 80, u128::from(ts));
            assert_eq!(decoded & MAX_RANDOM, random);
        }
    }

    #[test]
    fn never_emits_the_letters_crockford_excludes() {
        for _ in 0..1000 {
            let random = u128::from_le_bytes({
                let mut b = [0u8; 16];
                crate::sys::fill(&mut b);
                b
            });
            let text = encode_ulid(now_ms(), random & MAX_RANDOM);
            assert!(!text.iter().any(|c| b"ilou".contains(c)), "{}", s(&text));
            assert!(text[0] <= b'7');
        }
    }

    #[test]
    fn step_reseeds_on_a_new_millisecond() {
        let state = UlidState {
            last_ms: 10,
            random: 5,
        }
        .step(11, || 42);
        assert_eq!(
            state,
            UlidState {
                last_ms: 11,
                random: 42
            }
        );
    }

    #[test]
    fn step_counts_within_a_millisecond_without_drawing() {
        let state = UlidState {
            last_ms: 10,
            random: 5,
        }
        .step(10, || unreachable!());
        assert_eq!(
            state,
            UlidState {
                last_ms: 10,
                random: 6
            }
        );
    }

    #[test]
    fn step_keeps_the_last_timestamp_when_the_clock_goes_backwards() {
        let state = UlidState {
            last_ms: 10,
            random: 5,
        }
        .step(3, || unreachable!());
        assert_eq!(
            state,
            UlidState {
                last_ms: 10,
                random: 6
            }
        );
    }

    #[test]
    fn step_carries_from_the_low_word_into_the_high_word() {
        let state = UlidState {
            last_ms: 10,
            random: u128::from(u64::MAX),
        }
        .step(10, || unreachable!());
        assert_eq!(state.random, 1 << 64);
    }

    #[test]
    fn step_borrows_the_next_millisecond_when_exhausted() {
        let state = UlidState {
            last_ms: 10,
            random: MAX_RANDOM,
        }
        .step(10, || 7);
        assert_eq!(
            state,
            UlidState {
                last_ms: 11,
                random: 7
            }
        );
    }

    #[test]
    fn step_masks_fresh_randomness_to_eighty_bits() {
        let state = UlidState::default().step(1, || u128::MAX);
        assert_eq!(state.random, MAX_RANDOM);
    }

    #[test]
    fn step_with_a_frozen_clock_is_strictly_increasing() {
        let mut state = UlidState::default().step(1_000, || MAX_RANDOM - 500);
        let mut previous = encode_ulid(state.last_ms, state.random);
        // Runs straight through an exhaustion and into the borrowed millisecond.
        for _ in 0..1_000 {
            state = state.step(1_000, || 0);
            let next = encode_ulid(state.last_ms, state.random);
            assert!(next > previous);
            previous = next;
        }
        assert_eq!(state.last_ms, 1_001);
    }

    #[test]
    fn uuid_v7_sets_version_and_variant_and_keeps_the_rest() {
        let bytes = compose_uuid_v7(0x0123_4567_89ab, [0xff; 10]);
        assert_eq!(&bytes[..6], &[0x01, 0x23, 0x45, 0x67, 0x89, 0xab]);
        assert_eq!(bytes[6], 0x7f);
        assert_eq!(bytes[7], 0xff);
        assert_eq!(bytes[8], 0xbf);
        assert!(bytes[9..].iter().all(|&b| b == 0xff));
    }

    #[test]
    fn uuid_v7_keeps_only_the_low_48_bits_of_the_timestamp() {
        let bytes = compose_uuid_v7(u64::MAX, [0; 10]);
        assert_eq!(&bytes[..6], &[0xff; 6]);
    }

    #[test]
    fn uuid_v7_text_sorts_in_time_order() {
        let earlier = encode_hex(compose_uuid_v7(1_000, [0xff; 10]));
        let later = encode_hex(compose_uuid_v7(1_001, [0x00; 10]));
        assert!(later > earlier);
    }

    #[test]
    fn hex_is_lowercase() {
        assert_eq!(
            s(&encode_hex([0xab; 16])),
            "abababababababababababababababab"
        );
    }

    #[test]
    fn now_ms_matches_the_unix_clock() {
        let before = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis();
        let now = u128::from(now_ms());
        let after = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis();
        assert!(before <= now && now <= after);
        // Sanity: after 2024-01-01 and before 2200, i.e. milliseconds, not seconds or micros.
        assert!(now > 1_704_067_200_000 && now < 7_258_118_400_000);
    }
}
