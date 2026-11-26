import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

import {
  DuplicateEntityPrefixError,
  Idora,
  IdoraId,
  type IdStrategy,
  InvalidEntityPrefixError,
  InvalidIdError,
} from '../src/index.js';
import { ENTROPY_BLOCK_SIZE, EntropyPool } from '../src/internal/entropy.js';
import { encodeUlid, UlidStream } from '../src/internal/ulid.js';
import { composeUuidV7 } from '../src/internal/uuidv7.js';
import { decodeTimestamp } from './support/ulid-decoder.js';

/**
 * Holds this package to the shared cross-language conformance vectors in `spec/test-vectors.json`.
 * Every Idora package runs the same vectors, which is what lets an identifier minted in one
 * language be sorted, split and validated in another.
 */
interface EncodingVector {
  readonly timestampMs: number;
  readonly randomHex: string;
  readonly expected: string;
}

interface MonotonicVector {
  readonly description: string;
  readonly previous: { readonly timestampMs: number; readonly randomHex: string };
  readonly nowMs: number;
  readonly expected: { readonly timestampMs: number; readonly randomHex: string | null; readonly encoded?: string };
}

interface Vectors {
  readonly ulid: readonly EncodingVector[];
  readonly uuidv7: readonly EncodingVector[];
  readonly ulidMonotonic: readonly MonotonicVector[];
  readonly prefixes: {
    readonly valid: readonly string[];
    readonly invalid: readonly { readonly prefix: string; readonly reason: string; readonly index?: number }[];
    readonly collidingPairs: readonly (readonly [string, string])[];
    readonly distinctPairs: readonly (readonly [string, string])[];
  };
  readonly ids: {
    readonly valid: readonly {
      readonly id: string;
      readonly prefix: string;
      readonly value: string;
      readonly format: IdStrategy;
      readonly timestampMs: number;
    }[];
    readonly invalid: readonly { readonly id: string; readonly reason: string; readonly expectedPrefix?: string }[];
    readonly ordered: readonly string[];
    readonly bounds: readonly {
      readonly timestampMs: number;
      readonly format: IdStrategy;
      readonly lower: string;
      readonly upper: string;
    }[];
  };
}

// Compiled to build/test/, three levels below the repository root.
const vectors = JSON.parse(readFileSync(new URL('../../../spec/test-vectors.json', import.meta.url), 'utf8')) as Vectors;

const fromHex = (hex: string): Uint8Array => Uint8Array.from(hex.match(/../g) ?? [], (pair) => parseInt(pair, 16));

class A {}
class B {}

describe('conformance: spec/test-vectors.json', () => {
  for (const vector of vectors.ulid) {
    it(`encodes the ULID vector ${vector.expected}`, () => {
      // Act & Assert
      assert.equal(encodeUlid(vector.timestampMs, fromHex(vector.randomHex)), vector.expected);
    });
  }

  for (const vector of vectors.uuidv7) {
    it(`composes the UUIDv7 vector ${vector.expected}`, () => {
      // Act & Assert
      assert.equal(composeUuidV7(vector.timestampMs, fromHex(vector.randomHex)), vector.expected);
    });
  }

  for (const vector of vectors.ulidMonotonic) {
    it(`steps the monotonic stream: ${vector.description}`, () => {
      // Arrange
      const stream = new UlidStream(new EntropyPool());
      stream.restore(vector.previous.timestampMs, fromHex(vector.previous.randomHex));

      // Act
      const ulid = stream.next(vector.nowMs);

      // Assert
      assert.equal(decodeTimestamp(ulid), vector.expected.timestampMs);
      if (vector.expected.randomHex !== null) {
        assert.equal(ulid, vector.expected.encoded);
      }
    });
  }

  for (const prefix of vectors.prefixes.valid) {
    it(`accepts the valid prefix ${JSON.stringify(prefix)}`, () => {
      // Act & Assert
      assert.ok(new Idora().generateWithPrefix(prefix).startsWith(`${prefix}_`));
    });
  }

  for (const { prefix, reason, index } of vectors.prefixes.invalid) {
    it(`rejects the invalid prefix ${JSON.stringify(prefix)} as ${reason}`, () => {
      // Act & Assert
      assert.throws(
        () => new Idora().generateWithPrefix(prefix),
        (error: unknown) => error instanceof InvalidEntityPrefixError && error.reason === reason && error.index === index,
      );
    });
  }

  for (const [first, second] of vectors.prefixes.collidingPairs) {
    it(`treats ${first} and ${second} as a collision`, () => {
      // Act & Assert
      assert.throws(() => new Idora({ entities: [[A, first], [B, second]] }), DuplicateEntityPrefixError);
    });
  }

  for (const [first, second] of vectors.prefixes.distinctPairs) {
    it(`treats ${first} and ${second} as distinct`, () => {
      // Act & Assert
      assert.doesNotThrow(() => new Idora({ entities: [[A, first], [B, second]] }));
    });
  }

  for (const vector of vectors.ids.valid) {
    it(`parses the identifier ${vector.id}`, () => {
      // Act
      const id = IdoraId.parse(vector.id);

      // Assert
      assert.equal(id.text, vector.id);
      assert.equal(id.prefix, vector.prefix);
      assert.equal(id.value, vector.value);
      assert.equal(id.format, vector.format);
      assert.equal(id.timestampMs, vector.timestampMs);
      assert.equal(id.createdAt.getTime(), vector.timestampMs);
      assert.ok(IdoraId.isValid(vector.id));
      assert.ok(IdoraId.isValid(vector.id, vector.prefix));
    });
  }

  for (const { id, reason, expectedPrefix } of vectors.ids.invalid) {
    it(`rejects the identifier ${JSON.stringify(id)}${expectedPrefix === undefined ? '' : ` expecting ${expectedPrefix}`} as ${reason}`, () => {
      // Act & Assert
      assert.throws(
        () => IdoraId.parse(id, expectedPrefix),
        (error: unknown) =>
          error instanceof InvalidIdError &&
          error.reason === reason &&
          error.text === id &&
          error.expectedPrefix === expectedPrefix &&
          (reason === 'prefix') === (error.cause instanceof InvalidEntityPrefixError),
      );
      assert.equal(IdoraId.tryParse(id, expectedPrefix), undefined);
      assert.equal(IdoraId.isValid(id, expectedPrefix), false);
    });
  }

  it('sorts every shuffle of the ordered identifiers back into the natural order', () => {
    // Arrange
    const expected = vectors.ids.ordered;
    const parsed = expected.map((text) => IdoraId.parse(text));
    const random = seededRandom(0x1d07a);
    const permutations = [parsed.slice().reverse(), ...Array.from({ length: 200 }, () => shuffle(parsed, random))];

    for (const permutation of permutations) {
      // Act
      const sorted = permutation.slice().sort(IdoraId.compare).map(String);

      // Assert
      assert.deepEqual(sorted, expected);
    }
  });

  it('orders each adjacent pair of the ordered identifiers strictly', () => {
    // Arrange
    const parsed = vectors.ids.ordered.map((text) => IdoraId.parse(text));

    for (let i = 1; i < parsed.length; i++) {
      // Act & Assert
      assert.ok(parsed[i - 1]!.isBefore(parsed[i]!), `${parsed[i - 1]} should sort before ${parsed[i]}`);
      assert.ok(parsed[i]!.isAfter(parsed[i - 1]!));
    }
  });

  for (const vector of vectors.ids.bounds) {
    it(`gives the ${vector.format} bounds of millisecond ${vector.timestampMs}`, () => {
      // Act
      const lower = IdoraId.lowerBound('usr', vector.timestampMs, vector.format);
      const upper = IdoraId.upperBound('usr', vector.timestampMs, vector.format);

      // Assert
      assert.equal(IdoraId.lowerBoundValue(vector.timestampMs, vector.format), vector.lower);
      assert.equal(IdoraId.upperBoundValue(vector.timestampMs, vector.format), vector.upper);
      assert.equal(lower.text, `usr_${vector.lower}`);
      assert.equal(upper.text, `usr_${vector.upper}`);
      for (const bound of [lower, upper]) {
        const reparsed = IdoraId.parse(bound.text);
        assert.equal(reparsed.format, vector.format);
        assert.equal(reparsed.timestampMs, vector.timestampMs);
      }
    });
  }

  it('has an entropy block of the reference size', () => {
    // Act & Assert
    assert.equal(ENTROPY_BLOCK_SIZE, 1024);
  });
});

/** A small deterministic generator (mulberry32), so a failing shuffle can be reproduced. */
function seededRandom(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 0x1_0000_0000;
  };
}

/** A Fisher-Yates shuffle into a new array. */
function shuffle<T>(items: readonly T[], random: () => number): T[] {
  const shuffled = items.slice();
  for (let i = shuffled.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [shuffled[i], shuffled[j]] = [shuffled[j]!, shuffled[i]!];
  }

  return shuffled;
}
