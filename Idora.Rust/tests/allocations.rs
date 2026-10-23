//! Proves that generating an identifier costs exactly one heap allocation: the returned `String`,
//! sized exactly. A counting global allocator is installed for this test binary only.

#![allow(unsafe_code)]

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;

use idora::{Entity, Idora};

struct Counting;

thread_local! {
    // `const` and without a destructor, so touching it from inside the allocator never allocates.
    static ALLOCATIONS: Cell<usize> = const { Cell::new(0) };
}

// SAFETY: every method forwards to `System` unchanged; the counter is a thread-local `Cell`.
unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let _ = ALLOCATIONS.try_with(|n| n.set(n.get() + 1));
        // SAFETY: forwarded with the caller's guarantees.
        unsafe { System.alloc(layout) }
    }

    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        let _ = ALLOCATIONS.try_with(|n| n.set(n.get() + 1));
        // SAFETY: forwarded with the caller's guarantees.
        unsafe { System.alloc_zeroed(layout) }
    }

    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        let _ = ALLOCATIONS.try_with(|n| n.set(n.get() + 1));
        // SAFETY: forwarded with the caller's guarantees.
        unsafe { System.realloc(ptr, layout, new_size) }
    }

    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        // SAFETY: forwarded with the caller's guarantees.
        unsafe { System.dealloc(ptr, layout) }
    }
}

#[global_allocator]
static GLOBAL: Counting = Counting;

/// Allocations made on this thread while running `f`.
fn allocations_during<R>(f: impl FnOnce() -> R) -> (usize, R) {
    let before = ALLOCATIONS.with(Cell::get);
    let result = f();
    let after = ALLOCATIONS.with(Cell::get);
    (after - before, result)
}

struct User;
impl Entity for User {
    const PREFIX: &'static str = "usr";
}

fn generators() -> [(&'static str, Idora); 2] {
    [
        (
            "ulid",
            Idora::builder().register_entity::<User>().unwrap().build(),
        ),
        (
            "uuidv7",
            Idora::builder()
                .uuid_v7()
                .register_entity::<User>()
                .unwrap()
                .build(),
        ),
    ]
}

#[test]
fn generate_for_a_registered_type_allocates_only_the_returned_string() {
    for (name, ids) in generators() {
        let _warm = ids.generate::<User>().unwrap();
        let (count, id) = allocations_during(|| ids.generate::<User>().unwrap());
        assert_eq!(count, 1, "{name}: {id}");
        assert_eq!(
            id.capacity(),
            id.len(),
            "{name}: the string is sized exactly"
        );
    }
}

#[test]
fn generate_with_an_ad_hoc_prefix_allocates_only_the_returned_string() {
    for (name, ids) in generators() {
        let _warm = ids.generate_with_prefix("tmp").unwrap();
        let (count, id) = allocations_during(|| ids.generate_with_prefix("tmp").unwrap());
        assert_eq!(count, 1, "{name}: {id}");
        assert_eq!(id.capacity(), id.len(), "{name}");
    }
}

#[test]
fn the_first_identifier_on_a_fresh_thread_also_allocates_only_its_string() {
    // Thread-local state is `const`-initialised and the first entropy fill reads straight into it.
    for (name, ids) in generators() {
        let count = std::thread::spawn(move || allocations_during(|| ids.generate::<User>()).0)
            .join()
            .unwrap();
        assert_eq!(count, 1, "{name}");
    }
}

#[test]
fn many_entropy_refills_still_cost_one_allocation_per_identifier() {
    // 5 000 identifiers drain the 1 KiB pool about 50 times.
    const N: usize = 5_000;
    for (name, ids) in generators() {
        let mut kept = Vec::with_capacity(2 * N);
        let (count, ()) = allocations_during(|| {
            for _ in 0..N {
                kept.push(ids.generate::<User>().unwrap());
                kept.push(ids.generate_with_prefix("tmp").unwrap());
            }
        });
        assert_eq!(count, 2 * N, "{name}");
    }
}

#[test]
fn validation_and_lookups_do_not_allocate_on_success() {
    let [(_, ids), _] = generators();
    let (count, ()) = allocations_during(|| {
        assert!(idora::validate_prefix("abcdefghijklmnop").is_ok());
        assert_eq!(ids.get_prefix::<User>(), Some("usr"));
        assert_eq!(ids.prefix_of::<User>().unwrap(), "usr");
        assert_eq!(ids.registrations().len(), 1);
        let _shared = ids.clone();
    });
    assert_eq!(count, 0);
}
