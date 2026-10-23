/**
 * Measures identifier generation against `crypto.randomUUID()`, with no benchmarking dependency.
 *
 * Each case is warmed up until the JIT has settled, then timed over several samples of a fixed
 * number of calls; the median sample is reported, which is robust to the odd GC pause. Every result
 * is folded into a sink that is printed at the end, so the optimiser cannot discard the work.
 *
 *     npm run bench
 */
import { randomUUID } from 'node:crypto';
import { arch, cpus, platform } from 'node:os';

import { Idora } from '../src/index.js';

class Customer {}

const ulid = new Idora({ entities: [[Customer, 'cus']] });
const uuidv7 = new Idora({ strategy: 'uuidv7', entities: [[Customer, 'cus']] });

const CASES: ReadonlyArray<readonly [name: string, run: () => string]> = [
  ['crypto.randomUUID() (baseline)', () => randomUUID()],
  ["'cus_' + randomUUID().replaceAll('-', '')", () => 'cus_' + randomUUID().replaceAll('-', '')],
  ['generate(Customer) — ULID', () => ulid.generate(Customer)],
  ["generateWithPrefix('cus') — ULID", () => ulid.generateWithPrefix('cus')],
  ['generate(Customer) — UUIDv7', () => uuidv7.generate(Customer)],
  ["generateWithPrefix('cus') — UUIDv7", () => uuidv7.generateWithPrefix('cus')],
];

const CALLS_PER_SAMPLE = 200_000;
const SAMPLES = 15;
const WARM_UP_CALLS = 1_000_000;

let sink = 0;

function measure(run: () => string): number {
  for (let i = 0; i < WARM_UP_CALLS; i++) {
    sink += run().length;
  }

  const samples: number[] = [];
  for (let sample = 0; sample < SAMPLES; sample++) {
    const start = process.hrtime.bigint();
    for (let i = 0; i < CALLS_PER_SAMPLE; i++) {
      sink += run().length;
    }

    samples.push(Number(process.hrtime.bigint() - start) / CALLS_PER_SAMPLE);
  }

  samples.sort((a, b) => a - b);
  return samples[Math.floor(SAMPLES / 2)]!;
}

const results = CASES.map(([name, run]) => [name, measure(run)] as const);
const baseline = results[0]![1];

console.log(`Node.js ${process.version}, ${platform()} ${arch()}, ${cpus()[0]?.model ?? 'unknown CPU'}\n`);
console.log('| Method | Mean | Ratio |');
console.log('| --- | ---: | ---: |');
for (const [name, nanoseconds] of results) {
  console.log(`| \`${name}\` | ${nanoseconds.toFixed(1)} ns | ${(nanoseconds / baseline).toFixed(2)} |`);
}

console.log(`\n(sink: ${sink})`);
