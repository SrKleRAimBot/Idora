//! Identifiers sort by creation time as plain strings (specification §3.1).

mod support;

use std::time::Duration;

use idora::{Idora, Strategy};
use support::{split, ulid_timestamp};

struct User;
struct Order;

fn with(strategy: Strategy) -> Idora {
    Idora::builder()
        .strategy(strategy)
        .register::<User>("usr")
        .unwrap()
        .register::<Order>("ord")
        .unwrap()
        .build()
}

#[test]
fn ulids_are_strictly_increasing_on_one_thread() {
    let ids = with(Strategy::Ulid);
    let mut previous = ids.generate::<User>().unwrap();
    for _ in 0..100_000 {
        let next = ids.generate::<User>().unwrap();
        assert!(next > previous, "{next} <= {previous}");
        previous = next;
    }
}

#[test]
fn ulids_from_different_generators_on_one_thread_share_one_stream() {
    // The monotonic state is per thread, not per generator.
    let a = with(Strategy::Ulid);
    let b = Idora::new();
    let mut previous = String::new();
    for i in 0..10_000 {
        let ids = if i % 2 == 0 { &a } else { &b };
        let id = ids.generate_with_prefix("x").unwrap();
        assert!(id > previous, "{id} <= {previous}");
        previous = id;
    }
}

#[test]
fn both_strategies_increase_across_millisecond_boundaries() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = with(strategy);
        let mut previous = ids.generate::<User>().unwrap();
        for _ in 0..5 {
            std::thread::sleep(Duration::from_millis(2));
            let next = ids.generate::<User>().unwrap();
            assert!(next > previous, "{strategy:?}: {next} <= {previous}");
            previous = next;
        }
    }
}

#[test]
fn different_entities_sort_by_prefix_then_time() {
    for strategy in [Strategy::Ulid, Strategy::UuidV7] {
        let ids = with(strategy);
        let first_user = ids.generate::<User>().unwrap();
        std::thread::sleep(Duration::from_millis(2));
        let order = ids.generate::<Order>().unwrap();
        std::thread::sleep(Duration::from_millis(2));
        let second_user = ids.generate::<User>().unwrap();

        let mut sorted = vec![second_user.clone(), order.clone(), first_user.clone()];
        sorted.sort();
        assert_eq!(sorted, [order, first_user, second_user], "{strategy:?}");
    }
}

#[test]
fn each_thread_stays_strictly_increasing_under_concurrent_use() {
    let ids = with(Strategy::Ulid);
    std::thread::scope(|scope| {
        for _ in 0..support::threads() {
            scope.spawn(|| {
                let mut previous = ids.generate::<User>().unwrap();
                for _ in 0..20_000 {
                    let next = ids.generate::<User>().unwrap();
                    assert!(next > previous, "{next} <= {previous}");
                    previous = next;
                }
            });
        }
    });
}

#[test]
fn the_encoded_ulid_timestamp_never_decreases() {
    let ids = with(Strategy::Ulid);
    let mut previous = 0;
    for _ in 0..50_000 {
        let stamped = ulid_timestamp(split(&ids.generate::<User>().unwrap()).1);
        assert!(stamped >= previous);
        previous = stamped;
    }
}
