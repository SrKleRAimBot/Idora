import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

import { DuplicateEntityPrefixError, Idora, InvalidEntityPrefixError } from '../src/index.js';
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

  it('has an entropy block of the reference size', () => {
    // Act & Assert
    assert.equal(ENTROPY_BLOCK_SIZE, 1024);
  });
});
