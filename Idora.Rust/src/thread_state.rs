//! Per-thread state: the buffered entropy pool (specification §3.3) and the monotonic ULID stream
//! (§3.1, §7).
//!
//! Both live in one `thread_local!` with a `const` initialiser and no destructor, so reaching them
//! costs a thread-pointer offset rather than a lazy-initialisation check, and there is no shared
//! mutable state, no lock and no contention however many threads generate identifiers at once.
//!
//! Because the ULID stream is per thread, every [`Idora`](crate::Idora) on a given thread shares
//! it: identifiers made on one thread are strictly increasing even when they come from different
//! generators.

#![forbid(unsafe_code)]

use core::cell::RefCell;

use crate::sys;
use crate::value::UlidState;

/// Size of the per-thread entropy block. 1 KiB holds the randomness for 102 identifiers of either format,
/// which makes the system call negligible without holding meaningful memory on threads that only
/// ever generate a handful of identifiers.
pub(crate) const POOL_SIZE: usize = 1024;

/// A block of operating-system entropy handed out a slice at a time.
pub(crate) struct Pool {
    bytes: [u8; POOL_SIZE],
    /// Index of the first byte not yet handed out. Starts at `POOL_SIZE`: empty, so the first
    /// request fills it. (A `const` initialiser cannot call the operating system.)
    offset: usize,
}

impl Pool {
    pub(crate) const fn new() -> Self {
        Self {
            bytes: [0; POOL_SIZE],
            offset: POOL_SIZE,
        }
    }

    /// Takes `N` bytes, refilling the whole block from the operating system first if fewer remain.
    /// Bytes are never handed out twice.
    #[inline]
    pub(crate) fn take<const N: usize>(&mut self) -> [u8; N] {
        const { assert!(N <= POOL_SIZE) };
        if POOL_SIZE - self.offset < N {
            self.refill();
        }
        let mut out = [0u8; N];
        out.copy_from_slice(&self.bytes[self.offset..self.offset + N]);
        self.offset += N;
        out
    }

    /// Fills `dest` of any length. Requests larger than the block bypass it entirely.
    #[cfg(test)]
    pub(crate) fn fill(&mut self, dest: &mut [u8]) {
        if dest.len() > POOL_SIZE {
            sys::fill(dest);
            return;
        }
        if POOL_SIZE - self.offset < dest.len() {
            self.refill();
        }
        dest.copy_from_slice(&self.bytes[self.offset..self.offset + dest.len()]);
        self.offset += dest.len();
    }

    /// Takes the 80 bits of a fresh ULID random field.
    #[inline]
    pub(crate) fn take_u80(&mut self) -> u128 {
        let bytes = self.take::<10>();
        let mut wide = [0u8; 16];
        wide[6..].copy_from_slice(&bytes);
        u128::from_be_bytes(wide)
    }

    #[cold]
    #[inline(never)]
    fn refill(&mut self) {
        sys::fill(&mut self.bytes);
        self.offset = 0;
    }
}

/// Aligned to 128 bytes, the cache-line size on Apple silicon (and a multiple of the usual 64),
/// so that no two threads' state ever shares a cache line. Thread-local blocks are allocated
/// close together; without this, two threads writing their own counters can still contend for
/// one line, and throughput collapses as cores are added.
#[repr(align(128))]
struct ThreadState {
    pool: Pool,
    ulid: UlidState,
}

thread_local! {
    static STATE: RefCell<ThreadState> = const {
        RefCell::new(ThreadState { pool: Pool::new(), ulid: UlidState { last_ms: 0, random: 0 } })
    };
}

/// Advances this thread's ULID stream to `now_ms` and returns the new state.
#[inline]
pub(crate) fn next_ulid(now_ms: u64) -> UlidState {
    STATE.with(|cell| {
        let mut state = cell.borrow_mut();
        let ThreadState { pool, ulid } = &mut *state;
        *ulid = ulid.step(now_ms, || pool.take_u80());
        *ulid
    })
}

/// Takes `N` bytes from this thread's entropy pool.
#[inline]
pub(crate) fn random_bytes<const N: usize>() -> [u8; N] {
    STATE.with(|cell| cell.borrow_mut().pool.take::<N>())
}

/// Fills `dest` from this thread's entropy pool.
#[cfg(test)]
pub(crate) fn fill(dest: &mut [u8]) {
    STATE.with(|cell| cell.borrow_mut().pool.fill(dest));
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;

    #[test]
    fn successive_requests_differ() {
        let (mut a, mut b) = ([0u8; 16], [0u8; 16]);
        fill(&mut a);
        fill(&mut b);
        assert_ne!(a, b);
    }

    #[test]
    fn many_refills_never_repeat_a_block() {
        // 10 bytes at a time across ~100 refills; a refill that failed to change the buffer would
        // hand out the same 10 bytes again.
        let mut pool = Pool::new();
        let seen: HashSet<[u8; 10]> = (0..10_000).map(|_| pool.take::<10>()).collect();
        assert_eq!(seen.len(), 10_000);
    }

    #[test]
    fn requests_larger_than_the_pool_fill_every_byte() {
        let mut big = vec![0u8; POOL_SIZE * 3 + 7];
        fill(&mut big);
        // Each 64-byte window being all zero has probability 2^-512.
        for window in big.chunks(64) {
            assert!(window.iter().any(|&b| b != 0));
        }
    }

    #[test]
    fn an_empty_request_does_nothing() {
        fill(&mut []);
        let mut pool = Pool::new();
        let _: [u8; 0] = pool.take::<0>();
    }

    #[test]
    fn a_request_that_straddles_the_end_triggers_a_refill_not_a_short_read() {
        let mut pool = Pool::new();
        let _ = pool.take::<1020>();
        let tail = pool.take::<10>();
        assert_eq!(pool.offset, 10);
        assert_eq!(tail, pool.bytes[..10]);
    }

    #[test]
    fn ulid_randomness_stays_within_eighty_bits_and_is_distinct() {
        let mut pool = Pool::new();
        let values: HashSet<u128> = (0..10_000).map(|_| pool.take_u80()).collect();
        assert_eq!(values.len(), 10_000);
        assert!(values.iter().all(|&v| v <= crate::value::MAX_RANDOM));
        // And the top bits are actually used.
        assert!(values.iter().any(|&v| v >> 72 != 0));
    }

    #[test]
    fn many_threads_never_receive_the_same_bytes() {
        let handles: Vec<_> = (0..8)
            .map(|_| {
                std::thread::spawn(|| (0..2_000).map(|_| random_bytes::<10>()).collect::<Vec<_>>())
            })
            .collect();
        let mut all = HashSet::new();
        for handle in handles {
            for value in handle.join().unwrap() {
                assert!(all.insert(value));
            }
        }
        assert_eq!(all.len(), 16_000);
    }

    #[test]
    fn the_ulid_stream_is_shared_by_everything_on_one_thread() {
        let a = next_ulid(5);
        let b = next_ulid(5);
        // Either both were in the same millisecond as some earlier test on this thread, or `a`
        // reseeded; in both cases `b` counts on from `a`.
        assert_eq!(b.last_ms, a.last_ms);
        assert_eq!(b.random, a.random + 1);
    }
}
