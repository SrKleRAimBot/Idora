import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { Worker } from 'node:worker_threads';

import { Idora } from '../src/index.js';
import { User } from './support/entities.js';

describe('uniqueness', () => {
  it('never repeats a ULID across a large batch', () => {
    // Arrange
    const ids = new Idora({ entities: [[User, 'usr']] });

    // Act
    const generated = new Set(Array.from({ length: 200_000 }, () => ids.generate(User)));

    // Assert
    assert.equal(generated.size, 200_000);
  });

  it('never repeats a UUIDv7 across a large batch', () => {
    // Arrange
    const ids = new Idora({ strategy: 'uuidv7', entities: [[User, 'usr']] });

    // Act
    const generated = new Set(Array.from({ length: 200_000 }, () => ids.generate(User)));

    // Assert
    assert.equal(generated.size, 200_000);
  });

  it('never repeats an identifier across worker threads generating at the same time', async () => {
    // Arrange: each worker loads its own copy of the module, and with it its own stream and
    // entropy block — the JavaScript equivalent of per-thread state.
    const workerCount = 4;
    const perWorker = 25_000;
    const moduleUrl = new URL('../src/index.js', import.meta.url).href;
    const source = `
      const { parentPort, workerData } = require('node:worker_threads');
      import(workerData.moduleUrl).then(({ Idora }) => {
        const ids = new Idora({ strategy: workerData.strategy });
        const out = new Array(workerData.count);
        for (let i = 0; i < workerData.count; i++) out[i] = ids.generateWithPrefix('usr');
        parentPort.postMessage(out);
      });
    `;

    for (const strategy of ['ulid', 'uuidv7'] as const) {
      // Act
      const results = await Promise.all(
        Array.from(
          { length: workerCount },
          () =>
            new Promise<string[]>((resolve, reject) => {
              const worker = new Worker(source, { eval: true, workerData: { moduleUrl, strategy, count: perWorker } });
              worker.once('message', resolve);
              worker.once('error', reject);
            }),
        ),
      );

      // Assert
      const all = new Set(results.flat());
      assert.equal(all.size, workerCount * perWorker, `${strategy} identifiers repeated across workers`);
    }
  });
});
