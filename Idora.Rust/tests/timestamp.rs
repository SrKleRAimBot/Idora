//! Timestamps are milliseconds since the Unix epoch in UTC, whatever the host's time zone
//! (specification §3).
//!
//! `SystemTime` has no time zone, so there is nothing in-process to vary. Instead the zone tests
//! re-run this test binary as a child process with `TZ` set to the most extreme zones there are,
//! and check that the identifiers it prints carry the same instant this process sees.

mod support;

use std::process::Command;

use idora::Idora;
use support::{split, ulid_timestamp, unix_ms, uuid_timestamp};

/// Allowance for spawning the child process and for scheduling noise.
const TOLERANCE_MS: u64 = 30_000;

/// Printed by the child, parsed by the parent.
const MARKER: &str = "IDORA_TIMESTAMPS ";

/// Not a test in its own right: the child-process half of the zone tests. Prints the timestamps
/// encoded in one ULID and one UUIDv7.
#[test]
#[ignore = "run as a child process by the zone tests"]
fn child_prints_encoded_timestamps() {
    let ulid_id = Idora::new().generate_with_prefix("t").unwrap();
    let v7_id = Idora::builder()
        .uuid_v7()
        .build()
        .generate_with_prefix("t")
        .unwrap();
    println!(
        "{MARKER}{} {}",
        ulid_timestamp(split(&ulid_id).1),
        uuid_timestamp(split(&v7_id).1)
    );
}

fn timestamps_from_a_child_in(zone: &str) -> (u64, u64) {
    let output = Command::new(std::env::current_exe().unwrap())
        .args([
            "child_prints_encoded_timestamps",
            "--exact",
            "--ignored",
            "--nocapture",
        ])
        .env("TZ", zone)
        .output()
        .expect("the test binary can re-run itself");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(output.status.success(), "child failed in {zone}: {stdout}");
    let line = stdout
        .lines()
        .find_map(|line| line.strip_prefix(MARKER))
        .unwrap_or_else(|| panic!("no timestamps from the child in {zone}: {stdout}"));
    let mut parts = line.split(' ').map(|n| n.parse::<u64>().unwrap());
    (parts.next().unwrap(), parts.next().unwrap())
}

#[test]
fn identifiers_encode_utc_whatever_the_hosts_time_zone() {
    // UTC+14 and UTC-12: 26 hours apart. Reading local time anywhere would show up as hours.
    for zone in [
        "Pacific/Kiritimati",
        "Etc/GMT+12",
        "UTC",
        "Australia/Sydney",
    ] {
        let before = unix_ms();
        let (ulid_ms, v7_ms) = timestamps_from_a_child_in(zone);
        let after = unix_ms();
        for (format, stamped) in [("ulid", ulid_ms), ("uuidv7", v7_ms)] {
            assert!(
                stamped + TOLERANCE_MS >= before && stamped <= after + TOLERANCE_MS,
                "{format} in {zone}: {stamped} is not within {before}..={after}"
            );
        }
    }
}

#[test]
fn ulid_timestamps_agree_with_system_time() {
    let ids = Idora::new();
    let before = unix_ms();
    let stamped = ulid_timestamp(split(&ids.generate_with_prefix("t").unwrap()).1);
    let after = unix_ms();
    assert!(before <= stamped && stamped <= after);
}

#[test]
fn uuid_v7_timestamps_agree_with_system_time() {
    let ids = Idora::builder().uuid_v7().build();
    let before = unix_ms();
    let stamped = uuid_timestamp(split(&ids.generate_with_prefix("t").unwrap()).1);
    let after = unix_ms();
    assert!(before <= stamped && stamped <= after);
}

#[test]
fn identifiers_made_in_different_zones_order_by_instant() {
    let first = timestamps_from_a_child_in("Pacific/Kiritimati");
    std::thread::sleep(std::time::Duration::from_millis(5));
    let second = timestamps_from_a_child_in("Etc/GMT+12");
    assert!(second.0 > first.0, "{second:?} should be after {first:?}");
    assert!(second.1 > first.1, "{second:?} should be after {first:?}");
}
