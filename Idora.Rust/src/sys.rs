//! The operating system's cryptographically secure random number generator.
//!
//! This is the only module in the crate that contains `unsafe` code, and the only place that talks
//! to the operating system directly. It exists so that Idora can have no dependencies at all: the
//! two calls below are what the `getrandom` crate would otherwise have made on our behalf.
//!
//! - **Apple platforms** (macOS, iOS, tvOS, watchOS, visionOS): `CCRandomGenerateBytes` from
//!   libSystem, a CSPRNG seeded from the kernel. It is what `std` itself uses on these platforms,
//!   and fills 1 KiB about seven times faster than `getentropy(2)` does on macOS.
//! - **Other Unix** (Linux with glibc 2.25+ or musl, Android API 28+, FreeBSD, OpenBSD, NetBSD,
//!   DragonFly, illumos, Solaris): `getentropy(2)`, in the C library `std` already links against.
//! - **Windows** 10 and later: `ProcessPrng` from `bcryptprimitives.dll`, the same function `std`
//!   itself uses to seed `HashMap`.
//!
//! Every other target fails to compile with a message saying so, rather than falling back to
//! something weaker.

#![allow(unsafe_code)]

/// Fills `dest` entirely with bytes from the operating system's CSPRNG.
///
/// # Panics
///
/// Panics if the operating system refuses to supply entropy. On the supported platforms that only
/// happens when the process is in a state where nothing else would work either (for example a
/// seccomp filter forbidding the call), and continuing with predictable identifiers would be worse
/// than stopping. `std` makes the same choice when it seeds `HashMap`.
pub(crate) fn fill(dest: &mut [u8]) {
    imp::fill(dest);
}

#[cfg(target_vendor = "apple")]
mod imp {
    use core::ffi::c_void;

    /// `kCCSuccess` from `<CommonCrypto/CommonCryptoError.h>`.
    const CC_SUCCESS: i32 = 0;

    unsafe extern "C" {
        // CCRNGStatus CCRandomGenerateBytes(void *bytes, size_t count);
        // Part of libSystem, which every Apple binary links.
        fn CCRandomGenerateBytes(bytes: *mut c_void, count: usize) -> i32;
    }

    pub(super) fn fill(dest: &mut [u8]) {
        if dest.is_empty() {
            return;
        }
        // SAFETY: `dest` is a valid, writable, exclusively borrowed region of exactly
        // `dest.len()` bytes. CCRandomGenerateBytes writes at most `count` bytes and retains no
        // pointer after returning.
        let status =
            unsafe { CCRandomGenerateBytes(dest.as_mut_ptr().cast::<c_void>(), dest.len()) };
        assert!(
            status == CC_SUCCESS,
            "idora: CCRandomGenerateBytes failed with status {status}; the operating system's \
             random number generator is unavailable"
        );
    }
}

#[cfg(any(
    target_os = "linux",
    target_os = "android",
    target_os = "freebsd",
    target_os = "openbsd",
    target_os = "netbsd",
    target_os = "dragonfly",
    target_os = "illumos",
    target_os = "solaris",
))]
mod imp {
    use core::ffi::{c_int, c_void};

    /// `getentropy(2)` rejects requests larger than this with `EINVAL`.
    const MAX_CHUNK: usize = 256;

    unsafe extern "C" {
        // int getentropy(void *buffer, size_t length);
        fn getentropy(buffer: *mut c_void, length: usize) -> c_int;
    }

    pub(super) fn fill(dest: &mut [u8]) {
        for chunk in dest.chunks_mut(MAX_CHUNK) {
            // SAFETY: `chunk` is a valid, writable, exclusively borrowed region of exactly
            // `chunk.len()` bytes, and `chunk.len() <= 256`, the documented maximum. getentropy
            // writes at most `length` bytes and retains no pointer after returning.
            let status = unsafe { getentropy(chunk.as_mut_ptr().cast::<c_void>(), chunk.len()) };
            assert!(
                status == 0,
                "idora: getentropy failed ({}); the operating system's random number generator \
                 is unavailable",
                std::io::Error::last_os_error()
            );
        }
    }
}

#[cfg(windows)]
mod imp {
    // `raw-dylib` means no import library is needed at link time; bcryptprimitives.lib is not part
    // of every Windows SDK. On 32-bit x86 the import is stdcall and must not be name-decorated.
    #[cfg_attr(
        target_arch = "x86",
        link(
            name = "bcryptprimitives",
            kind = "raw-dylib",
            import_name_type = "undecorated"
        )
    )]
    #[cfg_attr(
        not(target_arch = "x86"),
        link(name = "bcryptprimitives", kind = "raw-dylib")
    )]
    unsafe extern "system" {
        // BOOL ProcessPrng(PBYTE pbData, SIZE_T cbData);
        fn ProcessPrng(pb_data: *mut u8, cb_data: usize) -> i32;
    }

    pub(super) fn fill(dest: &mut [u8]) {
        // SAFETY: `dest` is a valid, writable, exclusively borrowed region of exactly `dest.len()`
        // bytes. ProcessPrng writes exactly that many bytes, retains no pointer, and is documented
        // to always return TRUE.
        let status = unsafe { ProcessPrng(dest.as_mut_ptr(), dest.len()) };
        assert!(
            status != 0,
            "idora: ProcessPrng failed; the operating system's random number generator is unavailable"
        );
    }
}

#[cfg(not(any(
    target_vendor = "apple",
    target_os = "linux",
    target_os = "android",
    target_os = "freebsd",
    target_os = "openbsd",
    target_os = "netbsd",
    target_os = "dragonfly",
    target_os = "illumos",
    target_os = "solaris",
    windows,
)))]
compile_error!(
    "idora does not know how to obtain cryptographically secure randomness on this target. \
     Supported: Apple platforms, Unix platforms with getentropy(2) (Linux, Android API 28+, the \
     BSDs, illumos, Solaris) and Windows 10+. Please open an issue at \
     https://github.com/SrKleRAimBot/Idora/issues naming the target."
);

#[cfg(test)]
mod tests {
    use super::fill;

    #[test]
    fn fills_requests_larger_than_one_getentropy_call() {
        // 1000 bytes is four getentropy calls. A zeroed tail would mean a chunk was skipped.
        let mut buf = [0u8; 1000];
        fill(&mut buf);
        assert!(buf[744..].iter().any(|&b| b != 0));
        assert!(buf[..256].iter().any(|&b| b != 0));
    }

    #[test]
    fn an_empty_request_is_fine() {
        fill(&mut []);
    }

    #[test]
    fn successive_calls_differ() {
        let (mut a, mut b) = ([0u8; 32], [0u8; 32]);
        fill(&mut a);
        fill(&mut b);
        assert_ne!(a, b);
    }
}
