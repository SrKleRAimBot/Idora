//! No two identifiers are the same, sequentially or concurrently.

mod support;

use std::collections::HashSet;

use idora::{Idora, Strategy};
use support::{is_ulid, is_uuid_v7, split};

struct User;

fn with(strategy: Strategy) -> Idora {
    Idora::builder()
        .strategy(strategy)
        .register::<User>("usr")
        .unwrap()
        .build()
}

#[test]
fn one_million_in_sequence_are_all_distinct() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = with(strategy);
        let mut seen = HashSet::with_capacity(1_000_000);
        for _ in 0..1_000_000 {
            assert!(seen.insert(ids.generate::<User>().unwrap()), "{strategy:?}");
        }
    }
}

fn concurrently(per_thread: usize, make: impl Fn(usize) -> String + Sync) -> Vec<String> {
    std::thread::scope(|scope| {
        let handles: Vec<_> = (0..support::threads())
            .map(|_| scope.spawn(|| (0..per_thread).map(&make).collect::<Vec<_>>()))
            .collect();
        handles
            .into_iter()
            .flat_map(|h| h.join().unwrap())
            .collect()
    })
}

fn assert_distinct(all: &[String]) {
    let unique: HashSet<&String> = all.iter().collect();
    assert_eq!(unique.len(), all.len());
}

#[test]
fn every_core_at_once_produces_no_duplicates() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = with(strategy);
        let all = concurrently(100_000, |_| ids.generate::<User>().unwrap());
        assert_distinct(&all);
    }
}

#[test]
fn every_core_at_once_produces_only_well_formed_identifiers() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = with(strategy);
        for id in concurrently(20_000, |_| ids.generate::<User>().unwrap()) {
            let (prefix, value) = split(&id);
            assert_eq!(prefix, "usr");
            match strategy {
                Strategy::Ulid => assert!(is_ulid(value), "{id}"),
                _ => assert!(is_uuid_v7(value), "{id}"),
            }
        }
    }
}

#[test]
fn several_generators_on_many_threads_produce_no_duplicates() {
    let generators = [with(Strategy::Ulid), with(Strategy::Ulid), Idora::new()];
    let all = concurrently(60_000, |i| {
        generators[i % generators.len()]
            .generate_with_prefix("usr")
            .unwrap()
    });
    assert_distinct(&all);
}

#[test]
fn ad_hoc_prefixes_concurrently_produce_no_duplicates() {
    let ids = Idora::new();
    let all = concurrently(50_000, |_| ids.generate_with_prefix("tmp").unwrap());
    assert_distinct(&all);
}

#[test]
fn a_cloned_generator_on_another_thread_shares_the_registry() {
    let ids = with(Strategy::Ulid);
    let moved = ids.clone();
    let id = std::thread::spawn(move || moved.generate::<User>().unwrap())
        .join()
        .unwrap();
    assert!(id.starts_with("usr_"));
    assert_ne!(id, ids.generate::<User>().unwrap());
}
