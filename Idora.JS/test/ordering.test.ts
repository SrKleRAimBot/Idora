import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { Idora } from '../src/index.js';
import { User } from './support/entities.js';
import { decodeRandom, decodeTimestamp, split } from './support/ulid-decoder.js';

/** Ordinal comparison, which is what a database's binary collation does. */
const ordinal = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

describe('ordering', () => {
  it('produces ULIDs that sort, as plain strings, into the order they were made', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const generated = Array.from({ length: 10_000 }, () => ids.generate(User));

    // Assert
    assert.deepEqual([...generated].sort(ordinal), generated);
  });

  it('keeps ULIDs strictly increasing within a single millisecond', () => {
    // Arrange: far more identifiers than one millisecond can hold apart, so most share one.
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const generated = Array.from({ length: 5_000 }, () => ids.generate(User));

    // Assert
    for (let i = 1; i < generated.length; i++) {
      assert.ok(generated[i - 1]! < generated[i]!, `${generated[i - 1]} should sort before ${generated[i]}`);
    }
  });

  it('increments the random field by exactly one between ULIDs in the same millisecond', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });
    let first = split(ids.generate(User)).value;
    let second = split(ids.generate(User)).value;
    while (decodeTimestamp(first) !== decodeTimestamp(second)) {
      first = second;
      second = split(ids.generate(User)).value;
    }

    // Act
    const difference = decodeRandom(second) - decodeRandom(first);

    // Assert
    assert.equal(difference, 1n);
  });

  it('shares one monotonic stream between generators in the same realm', () => {
    // Arrange
    const first = new Idora({ entities: [[User, 'usr']] });
    const second = new Idora({ entities: [[User, 'usr']] });

    // Act
    const generated = Array.from({ length: 2_000 }, (_, i) => split((i % 2 === 0 ? first : second).generate(User)).value);

    // Assert
    for (let i = 1; i < generated.length; i++) {
      assert.ok(generated[i - 1]! < generated[i]!);
    }
  });

  it('orders UUIDv7s by the millisecond they were made in', async () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });
    const earlier = ids.generate(User);
    await new Promise((resolve) => setTimeout(resolve, 5));

    // Act
    const later = ids.generate(User);

    // Assert
    assert.ok(earlier < later);
  });

  it('orders identifiers made across milliseconds by time, not by random chance', async () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });
    const batches: string[] = [];
    for (let batch = 0; batch < 5; batch++) {
      batches.push(ids.generate(User));
      await new Promise((resolve) => setTimeout(resolve, 2));
    }

    // Act
    const sorted = [...batches].sort(ordinal);

    // Assert
    assert.deepEqual(sorted, batches);
  });
});
