//! Identifier generation benchmarks, on a small hand-rolled harness (no `criterion`: Idora has no
//! dependencies, dev-dependencies included).
//!
//! Each case is warmed up, then timed over several samples of a calibrated number of iterations;
//! the table reports the median nanoseconds per identifier. Run with:
//!
//! ```text
//! cargo bench
//! ```

// Timing arithmetic converts freely between integers and floats; the magnitudes involved are far
// inside every type's exact range.
#![allow(
    clippy::cast_precision_loss,
    clippy::cast_possible_truncation,
    clippy::cast_sign_loss
)]

use std::hint::black_box;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use idora::{Entity, Idora};

struct Customer;
impl Entity for Customer {
    const PREFIX: &'static str = "cus";
}

const WARM_UP: Duration = Duration::from_millis(300);
const SAMPLE: Duration = Duration::from_millis(100);
const SAMPLES: usize = 21;

/// Median ns/op of `f` over [`SAMPLES`] samples.
fn measure<T>(mut f: impl FnMut() -> T) -> f64 {
    // Warm up, and estimate how many iterations fill one sample.
    let start = Instant::now();
    let mut warm_iterations = 0u64;
    while start.elapsed() < WARM_UP {
        for _ in 0..1_000 {
            black_box(f());
        }
        warm_iterations += 1_000;
    }
    let per_op = start.elapsed().as_secs_f64() / warm_iterations as f64;
    let iterations = ((SAMPLE.as_secs_f64() / per_op) as u64).max(1_000);

    let mut samples: Vec<f64> = (0..SAMPLES)
        .map(|_| {
            let start = Instant::now();
            for _ in 0..iterations {
                black_box(f());
            }
            start.elapsed().as_nanos() as f64 / iterations as f64
        })
        .collect();
    samples.sort_by(f64::total_cmp);
    samples[SAMPLES / 2]
}

/// The naive UUIDv4: 16 bytes straight from the operating system on every call, version and
/// variant bits set, formatted with `format!` in the hyphenated form. Roughly what
/// `Uuid::new_v4().to_string()` costs, without depending on the `uuid` crate.
fn naive_uuid_v4() -> String {
    let mut bytes = [0u8; 16];
    idora::__private::os_random(&mut bytes);
    bytes[6] = 0x40 | (bytes[6] & 0x0F);
    bytes[8] = 0x80 | (bytes[8] & 0x3F);
    let v = u128::from_be_bytes(bytes);
    format!(
        "{:08x}-{:04x}-{:04x}-{:04x}-{:012x}",
        v >> 96,
        (v >> 80) & 0xFFFF,
        (v >> 64) & 0xFFFF,
        (v >> 48) & 0xFFFF,
        v & 0xFFFF_FFFF_FFFF
    )
}

/// The naive prefixed UUIDv7: read the clock, draw 10 bytes from the operating system, and
/// `format!` the result behind a prefix.
fn naive_prefixed_uuid_v7() -> String {
    let ms = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_millis() as u64;
    let mut random = [0u8; 10];
    idora::__private::os_random(&mut random);
    let mut bytes = [0u8; 16];
    bytes[..6].copy_from_slice(&ms.to_be_bytes()[2..]);
    bytes[6..].copy_from_slice(&random);
    bytes[6] = 0x70 | (bytes[6] & 0x0F);
    bytes[8] = 0x80 | (bytes[8] & 0x3F);
    format!("cus_{:032x}", u128::from_be_bytes(bytes))
}

/// Aggregate ns per identifier with every core generating ULIDs at once.
fn measure_multi_threaded(ids: &Idora, threads: usize) -> f64 {
    const PER_THREAD: u64 = 200_000;
    let run = || {
        let start = Instant::now();
        std::thread::scope(|scope| {
            for _ in 0..threads {
                scope.spawn(|| {
                    for _ in 0..PER_THREAD {
                        black_box(ids.generate::<Customer>().unwrap());
                    }
                });
            }
        });
        start.elapsed().as_nanos() as f64 / (PER_THREAD * threads as u64) as f64
    };
    run();
    let mut samples: Vec<f64> = (0..11).map(|_| run()).collect();
    samples.sort_by(f64::total_cmp);
    samples[samples.len() / 2]
}

fn main() {
    // `cargo test --benches` runs this binary with `--bench` absent; keep that fast.
    if !std::env::args().any(|arg| arg == "--bench") {
        println!("run with `cargo bench` to measure");
        return;
    }

    let ulid = Idora::builder()
        .register_entity::<Customer>()
        .unwrap()
        .build();
    let uuid_v7 = Idora::builder()
        .uuid_v7()
        .register_entity::<Customer>()
        .unwrap()
        .build();

    let cases: Vec<(&str, f64)> = vec![
        (
            "naive UUIDv4: 16 OS-random bytes + `format!` *(baseline)*",
            measure(naive_uuid_v4),
        ),
        (
            "naive prefixed UUIDv7: clock + 10 OS-random bytes + `format!`",
            measure(naive_prefixed_uuid_v7),
        ),
        (
            "**`generate::<T>()` — ULID**",
            measure(|| ulid.generate::<Customer>().unwrap()),
        ),
        (
            "**`generate_with_prefix(\"cus\")` — ULID**",
            measure(|| ulid.generate_with_prefix(black_box("cus")).unwrap()),
        ),
        (
            "**`generate::<T>()` — UUIDv7**",
            measure(|| uuid_v7.generate::<Customer>().unwrap()),
        ),
        (
            "**`generate_with_prefix(\"cus\")` — UUIDv7**",
            measure(|| uuid_v7.generate_with_prefix(black_box("cus")).unwrap()),
        ),
    ];

    let baseline = cases[0].1;
    println!("| Case | Median | Ratio |");
    println!("| --- | ---: | ---: |");
    for (name, ns) in &cases {
        println!("| {name} | {ns:.1} ns | {:.2} |", ns / baseline);
    }

    let cores = std::thread::available_parallelism().map_or(4, std::num::NonZero::get);
    println!();
    println!("| Multi-threaded `generate::<T>()` — ULID | Aggregate | Throughput |");
    println!("| --- | ---: | ---: |");
    for threads in [1, 4, cores] {
        let aggregate = measure_multi_threaded(&ulid, threads);
        println!(
            "| {threads} thread{} | {aggregate:.1} ns/id | {:.0} M ids/s |",
            if threads == 1 { "" } else { "s" },
            1_000.0 / aggregate
        );
    }
}
