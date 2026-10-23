import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { Idora } from '../src/index.js';
import { User } from './support/entities.js';
import { decodeTimestamp, split } from './support/ulid-decoder.js';

/**
 * Pins the clock identifiers are stamped from to UTC.
 *
 * Both ULID and UUIDv7 define their timestamp as milliseconds since the Unix epoch — an absolute
 * instant, with no time zone attached — so servers in Sydney and Los Angeles that agree on the
 * instant produce identifiers that interleave in true creation order. Stamping from local time
 * would shift every server's identifiers by its own offset, silently.
 *
 * That mistake is invisible on a machine already running in UTC, so these assertions only bite
 * when the host has a non-zero offset. CI runs the suite with TZ=Australia/Sydney so that they do.
 */
const TOLERANCE_MS = 30_000;
const LOCAL_OFFSET_MS = -new Date().getTimezoneOffset() * 60_000;

function assertIsUtc(actual: number): void {
  const utcNow = Date.now();
  assert.ok(Math.abs(actual - utcNow) < TOLERANCE_MS, `timestamp ${actual} is not within ${TOLERANCE_MS}ms of UTC ${utcNow}`);

  if (LOCAL_OFFSET_MS !== 0) {
    const asLocalTime = utcNow + LOCAL_OFFSET_MS;
    assert.ok(Math.abs(actual - asLocalTime) >= TOLERANCE_MS, 'the timestamp looks like local time rather than UTC');
  }
}

describe('timestamps', () => {
  it('stamps ULIDs with UTC regardless of the host time zone', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const encoded = decodeTimestamp(split(ids.generate(User)).value);

    // Assert
    assertIsUtc(encoded);
  });

  it('stamps UUIDv7s with UTC regardless of the host time zone', () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });

    // Act: the first twelve hex characters are the 48-bit millisecond timestamp.
    const encoded = parseInt(split(ids.generate(User)).value.slice(0, 12), 16);

    // Assert
    assertIsUtc(encoded);
  });

  it('reports which zone the suite ran in, so a UTC-only run is visible in the output', (context) => {
    context.diagnostic(`TZ=${process.env['TZ'] ?? '(unset)'}; local offset ${LOCAL_OFFSET_MS / 60_000} minutes`);
  });
});
