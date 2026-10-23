/**
 * The Web Crypto API, which every runtime Idora.JS targets — Node.js 20+, Deno, Bun, browsers and
 * edge workers — exposes as a global. Declared locally so that the library's own build needs no
 * `DOM` or `@types/node` typings.
 */
declare const crypto: {
  getRandomValues<T extends Uint8Array>(array: T): T;
};

/**
 * Size of the entropy block. 1 KiB holds the randomness for ~102 ULIDs or 102 UUIDv7s, which keeps
 * the refill cost negligible without holding a meaningful amount of memory.
 */
export const ENTROPY_BLOCK_SIZE = 1024;

/**
 * Supplies cryptographically strong random bytes with the per-call cost of an array read.
 *
 * Calling `crypto.getRandomValues` once per identifier would dominate the cost of generating it.
 * Instead a 1 KiB block is filled at a time and handed out in slices, refilling only when it runs
 * out. The bytes still come from the same CSPRNG; only the cost of fetching them is amortised.
 *
 * JavaScript runs one thread per realm, so a single module-level block needs no locking. Each
 * worker or isolate loads its own copy of the module and therefore gets a block of its own.
 */
export class EntropyPool {
  readonly #block = new Uint8Array(ENTROPY_BLOCK_SIZE);

  /** Index of the first byte that has not yet been handed out. Starts exhausted, so the first use fills it. */
  #offset = ENTROPY_BLOCK_SIZE;

  /**
   * Reserves `count` fresh bytes and returns the offset at which they start in {@link block}.
   * The bytes are valid until the next call.
   */
  reserve(count: number): number {
    let offset = this.#offset;
    if (offset + count > ENTROPY_BLOCK_SIZE) {
      crypto.getRandomValues(this.#block);
      offset = 0;
    }

    this.#offset = offset + count;
    return offset;
  }

  /** The block that {@link reserve} offsets point into. */
  get block(): Uint8Array {
    return this.#block;
  }
}

/** The realm-wide pool shared by every generator. */
export const sharedEntropy = new EntropyPool();
