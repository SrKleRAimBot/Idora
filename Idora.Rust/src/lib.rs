//! **Idora.Rust**: entity identifiers that say what they are and sort by when they were made.
//!
//! ```text
//! usr_01k2f9v8ta7m3x0qbrn4dzeh5c
//! ord_01k2f9v8tb9p5r2xnc7fkw3m0d
//! ```
//!
//! A prefix you register once, an underscore, and a lowercase [ULID](Strategy::Ulid) (the default)
//! or [UUIDv7](Strategy::UuidV7). Both values begin with a millisecond timestamp, so identifiers
//! sort into creation order as plain strings and database inserts land at the end of the index.
//!
//! This crate is the Rust member of the [Idora](https://github.com/SrKleRAimBot/Idora) family:
//! it implements the shared [specification] and produces identifiers indistinguishable from
//! Idora.NET, Idora.JS, Idora.Java and Idora.Go. It has **no dependencies** of any kind.
//!
//! # Quick start
//!
//! ```
//! use idora::{Entity, Idora};
//!
//! struct User;
//! impl Entity for User {
//!     const PREFIX: &'static str = "usr";
//! }
//!
//! struct Order;
//!
//! // Once, at start-up. Every mistake (a bad prefix, a prefix used twice) fails here.
//! let ids = Idora::builder()
//!     .register_entity::<User>()?      // the type declares its own prefix...
//!     .register::<Order>("ord")?       // ...or you give it one
//!     .build();
//!
//! // Anywhere, from any thread.
//! let user_id = ids.generate::<User>()?;   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
//! let order_id = ids.generate::<Order>()?; // "ord_01k2f9v8tb9p5r2xnc7fkw3m0d"
//! assert!(user_id.starts_with("usr_") && order_id.starts_with("ord_"));
//! # Ok::<(), idora::Error>(())
//! ```
//!
//! # Registering prefixes
//!
//! - [`Builder::register`] maps any `'static` type to a prefix.
//! - [`Entity`] lets a type declare its own prefix as an associated constant;
//!   [`Builder::register_entity`] and [`Builder::register_entities`] collect them from the types
//!   you name. The [`entity!`] macro implements the trait and checks the prefix at compile time.
//!
//! Both routes feed one registry and police each other: a type registered twice, or a prefix
//! claimed by two types (ignoring ASCII case), is an [`Error::DuplicatePrefix`].
//!
//! A prefix is 1 to 16 ASCII letters or digits; see [`validate_prefix`].
//!
//! # Threads and ordering
//!
//! An [`Idora`] is immutable, `Send + Sync` and cheap to clone. Generating takes no lock: the
//! entropy buffer and the ULID monotonic state are thread-local. ULIDs generated on one thread are
//! strictly increasing, even within one millisecond, even across different `Idora` instances, and
//! even if the wall clock steps backwards. Across threads, and for UUIDv7, identifiers are ordered
//! by millisecond only.
//!
//! [specification]: https://github.com/SrKleRAimBot/Idora/blob/main/spec/SPECIFICATION.md

#![deny(unsafe_op_in_unsafe_fn)]
#![warn(missing_docs)]

mod builder;
mod entity;
mod error;
mod generator;
mod prefix;
mod strategy;
mod sys;
mod thread_state;
mod value;

pub use builder::Builder;
pub use entity::{Entity, EntitySet};
pub use error::{
    DuplicateKind, DuplicatePrefixError, Error, InvalidPrefixError, InvalidPrefixReason,
    PrefixNotRegisteredError,
};
pub use generator::{Idora, Registration};
pub use prefix::{MAX_PREFIX_LEN, SEPARATOR, is_valid_prefix, validate_prefix};
pub use strategy::Strategy;

/// Not public API. The pure encoding functions, exposed only so this crate's own conformance
/// suite (`tests/conformance.rs`) can feed them the shared vectors with known clocks and known
/// randomness. Anything here may change or disappear in any release.
#[doc(hidden)]
pub mod __private {
    #![forbid(unsafe_code)]

    /// Encodes a 48-bit timestamp and an 80-bit random field as a 26-character ULID.
    #[must_use]
    pub fn encode_ulid(timestamp_ms: u64, random: u128) -> [u8; 26] {
        crate::value::encode_ulid(timestamp_ms, random)
    }

    /// Lays out a UUIDv7 and writes it as 32 lowercase hex characters.
    #[must_use]
    pub fn encode_uuid_v7(timestamp_ms: u64, random: [u8; 10]) -> [u8; 32] {
        crate::value::encode_hex(crate::value::compose_uuid_v7(timestamp_ms, random))
    }

    /// One monotonic step from `(last_ms, random)` at clock reading `now_ms`.
    #[must_use]
    pub fn ulid_step(
        last_ms: u64,
        random: u128,
        now_ms: u64,
        fresh: impl FnOnce() -> u128,
    ) -> (u64, u128) {
        let next = crate::value::UlidState { last_ms, random }.step(now_ms, fresh);
        (next.last_ms, next.random)
    }

    /// Fills `dest` straight from the operating system, unbuffered. Used by the benchmark's
    /// naive-UUIDv4 baseline.
    pub fn os_random(dest: &mut [u8]) {
        crate::sys::fill(dest);
    }
}

// Compiles every Rust example in the README as a doctest, so the README cannot drift from the API.
#[cfg(doctest)]
#[doc = include_str!("../README.md")]
struct ReadmeDoctests;
