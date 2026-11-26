import { IdoraId } from 'idora.js';

/** 2025-08-07T12:00:00.000Z. */
export const T = 1754568000000;

/**
 * A ULID identifier minted in millisecond `timestampMs`, with a fixed random field: the lower
 * bound's timestamp characters followed by sixteen arbitrary Crockford digits.
 */
export function idAt(prefix: string, timestampMs: number): string {
  return `${prefix}_${IdoraId.lowerBoundValue(timestampMs).slice(0, 10)}6j4ct4ank7f24sab`;
}
