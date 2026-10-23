import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { ENTROPY_BLOCK_SIZE, EntropyPool } from '../src/internal/entropy.js';
import { foldPrefix } from '../src/internal/prefix.js';
import { CROCKFORD_ALPHABET, encodeTimestamp, encodeUlid, UlidStream } from '../src/internal/ulid.js';
import { composeUuidV7, createUuidV7 } from '../src/internal/uuidv7.js';
import { decodeRandom, decodeTimestamp } from './support/ulid-decoder.js';

const ARBITRARY_TIMESTAMP = 1_700_000_000_000;

/** A pool that hands out a fixed byte pattern, so stream behaviour is deterministic. */
class FixedEntropy extends EntropyPool {
  readonly #bytes: Uint8Array;
  reserved = 0;

  constructor(fill: number) {
    super();
    this.#bytes = new Uint8Array(ENTROPY_BLOCK_SIZE).fill(fill);
  }

  override get block(): Uint8Array {
    return this.#bytes;
  }

  override reserve(count: number): number {
    this.reserved += count;
    return 0;
  }
}

describe('Crockford Base32', () => {
  it('uses the lowercase Crockford alphabet, which omits i, l, o and u', () => {
    // Act & Assert
    assert.equal(CROCKFORD_ALPHABET, '0123456789abcdefghjkmnpqrstvwxyz');
    for (const letter of 'ilou') {
      assert.ok(!CROCKFORD_ALPHABET.includes(letter));
    }
  });

  it('encodes the timestamp into ten characters that decode back to it', () => {
    // Act
    const encoded = encodeTimestamp(ARBITRARY_TIMESTAMP);

    // Assert
    assert.equal(encoded.length, 10);
    assert.equal(decodeTimestamp(encoded), ARBITRARY_TIMESTAMP);
  });

  it('encodes the largest 48-bit timestamp with a leading 7', () => {
    // Act
    const encoded = encodeTimestamp(2 ** 48 - 1);

    // Assert
    assert.equal(encoded, '7zzzzzzzzz');
  });

  it('encodes eighty random bits into sixteen characters that decode back to them', () => {
    // Arrange
    const random = Uint8Array.from([0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef, 0x01, 0x23]);

    // Act
    const ulid = encodeUlid(ARBITRARY_TIMESTAMP, random);

    // Assert
    assert.equal(decodeRandom(ulid), 0x0123456789abcdef0123n);
    assert.equal(decodeTimestamp(ulid), ARBITRARY_TIMESTAMP);
  });
});

describe('the monotonic ULID stream', () => {
  it('stamps the supplied timestamp on first use', () => {
    // Arrange
    const stream = new UlidStream(new FixedEntropy(0));

    // Act
    const ulid = stream.next(ARBITRARY_TIMESTAMP);

    // Assert
    assert.equal(decodeTimestamp(ulid), ARBITRARY_TIMESTAMP);
    assert.equal(stream.lastTimestampMs, ARBITRARY_TIMESTAMP);
  });

  it('stamps a timestamp of zero, the Unix epoch itself', () => {
    // Arrange
    const stream = new UlidStream(new FixedEntropy(0));

    // Act
    const ulid = stream.next(0);

    // Assert
    assert.equal(ulid, '0'.repeat(26));
  });

  it('increments the random field by exactly one within a millisecond, without drawing entropy', () => {
    // Arrange
    const entropy = new FixedEntropy(0x42);
    const stream = new UlidStream(entropy);
    const first = stream.next(ARBITRARY_TIMESTAMP);
    const drawn = entropy.reserved;

    // Act
    const second = stream.next(ARBITRARY_TIMESTAMP);

    // Assert
    assert.equal(decodeRandom(second) - decodeRandom(first), 1n);
    assert.equal(entropy.reserved, drawn);
  });

  it('draws fresh randomness for a new millisecond rather than counting on', () => {
    // Arrange
    const entropy = new FixedEntropy(0x42);
    const stream = new UlidStream(entropy);
    stream.next(ARBITRARY_TIMESTAMP);
    stream.next(ARBITRARY_TIMESTAMP);

    // Act
    const next = stream.next(ARBITRARY_TIMESTAMP + 1);

    // Assert: the fixed pool's pattern, not the incremented one.
    assert.equal(decodeRandom(next), BigInt('0x' + '42'.repeat(10)));
    assert.equal(entropy.reserved, 20);
  });

  it('keeps the previous millisecond when the clock steps backwards, and still increases', () => {
    // Arrange
    const stream = new UlidStream(new FixedEntropy(0));
    const before = stream.next(ARBITRARY_TIMESTAMP);

    // Act
    const after = stream.next(ARBITRARY_TIMESTAMP - 5_000);

    // Assert
    assert.equal(decodeTimestamp(after), ARBITRARY_TIMESTAMP);
    assert.ok(after > before);
  });

  it('carries across every digit of the random field', () => {
    // Arrange
    const stream = new UlidStream(new FixedEntropy(0));
    stream.restore(ARBITRARY_TIMESTAMP, Uint8Array.from([0, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]));

    // Act
    const next = stream.next(ARBITRARY_TIMESTAMP);

    // Assert
    assert.equal(decodeRandom(next), 1n << 64n);
  });

  it('borrows the next millisecond when the random field is exhausted', () => {
    // Arrange
    const entropy = new FixedEntropy(0);
    const stream = new UlidStream(entropy);
    stream.restore(ARBITRARY_TIMESTAMP, new Uint8Array(10).fill(0xff));

    // Act
    const next = stream.next(ARBITRARY_TIMESTAMP);

    // Assert
    assert.equal(decodeTimestamp(next), ARBITRARY_TIMESTAMP + 1);
    assert.equal(decodeRandom(next), 0n);
    assert.equal(entropy.reserved, 10);
  });
});

describe('UUIDv7', () => {
  it('lays the timestamp out big-endian in the first six octets', () => {
    // Act
    const uuid = composeUuidV7(ARBITRARY_TIMESTAMP, new Uint8Array(10));

    // Assert
    assert.equal(parseInt(uuid.slice(0, 12), 16), ARBITRARY_TIMESTAMP);
  });

  it('sets version 7 and the RFC 9562 variant whatever the random bytes are', () => {
    for (const fill of [0x00, 0xff, 0x5a, 0xa5]) {
      // Act
      const uuid = composeUuidV7(ARBITRARY_TIMESTAMP, new Uint8Array(10).fill(fill));

      // Assert
      assert.equal(uuid[12], '7');
      assert.ok('89ab'.includes(uuid[16]!), `variant nibble was ${uuid[16]}`);
    }
  });

  it('keeps the other 74 bits random', () => {
    // Act
    const zeros = composeUuidV7(0, new Uint8Array(10));
    const ones = composeUuidV7(0, new Uint8Array(10).fill(0xff));

    // Assert
    assert.equal(zeros, '00000000000070008000000000000000');
    assert.equal(ones, '0000000000007fffbfffffffffffffff');
  });

  it('round-trips through the canonical hyphenated form', () => {
    // Arrange
    const uuid = createUuidV7(Date.now());

    // Act
    const hyphenated = `${uuid.slice(0, 8)}-${uuid.slice(8, 12)}-${uuid.slice(12, 16)}-${uuid.slice(16, 20)}-${uuid.slice(20)}`;

    // Assert
    assert.match(hyphenated, /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });
});

describe('the entropy pool', () => {
  it('hands out consecutive, non-overlapping slices', () => {
    // Arrange
    const pool = new EntropyPool();
    const first = pool.reserve(10);

    // Act
    const second = pool.reserve(10);

    // Assert
    assert.equal(second, first + 10);
  });

  it('refills and starts over when a request would run past the end of the block', () => {
    // Arrange
    const pool = new EntropyPool();
    pool.reserve(ENTROPY_BLOCK_SIZE - 5);
    const before = Uint8Array.from(pool.block);

    // Act
    const offset = pool.reserve(10);

    // Assert
    assert.equal(offset, 0);
    assert.notDeepEqual(Uint8Array.from(pool.block), before);
  });

  it('fills the block with bytes that are not all the same', () => {
    // Arrange
    const pool = new EntropyPool();

    // Act
    pool.reserve(1);

    // Assert
    assert.ok(new Set(pool.block).size > 100);
  });
});

describe('ASCII case folding', () => {
  it('lowercases ASCII letters only', () => {
    // Act & Assert
    assert.equal(foldPrefix('UsR09'), 'usr09');
    assert.equal(foldPrefix('ABCDEFGHIJKLMNOPQRSTUVWXYZ'), 'abcdefghijklmnopqrstuvwxyz');
  });
});
