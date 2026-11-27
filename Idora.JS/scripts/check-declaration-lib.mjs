// Compiles a strict TypeScript consumer of the built idora.js against the oldest standard library
// its declarations have supported, ES2015, with skipLibCheck off.
//
// The package is compiled with lib ES2022, so its own build cannot notice a public declaration
// that names a newer library type (as `ErrorOptions` once did), which a consumer on an older `lib`
// then sees as an error on importing idora.js at all. 1.0's declarations compiled against ES2015.
// Run after `npm run build`.
//
//   node scripts/check-declaration-lib.mjs

import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const LIB = 'es2015';
const root = resolve(import.meta.dirname, '..');
const core = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));

const project = mkdtempSync(join(tmpdir(), 'idora-declaration-lib-'));
writeFileSync(join(project, 'package.json'), JSON.stringify({ name: 'consumer', private: true, type: 'module' }));
execFileSync(
  process.platform === 'win32' ? 'npm.cmd' : 'npm',
  ['install', '--no-audit', '--no-fund', '--ignore-scripts', `typescript@${core.devDependencies.typescript}`],
  { cwd: project, stdio: ['ignore', 'ignore', 'inherit'] },
);

// Copied in after the install, which would otherwise prune it as unknown to its package.json.
const to = join(project, 'node_modules', core.name);
mkdirSync(to, { recursive: true });
cpSync(join(root, 'package.json'), join(to, 'package.json'));
cpSync(join(root, 'dist'), join(to, 'dist'), { recursive: true });
writeFileSync(join(project, 'consumer.ts'), `import * as idora from '${core.name}';\nexport { idora };\n`);

try {
  execFileSync(
    join(project, 'node_modules', '.bin', process.platform === 'win32' ? 'tsc.cmd' : 'tsc'),
    ['--noEmit', '--strict', '--module', 'nodenext', '--moduleResolution', 'nodenext', '--target', 'es2022',
      '--lib', LIB, '--types', '', 'consumer.ts'],
    { cwd: project, encoding: 'utf8' },
  );
  console.log(`${core.name}'s declarations compile against lib ${LIB}: ok`);
} catch (error) {
  console.error(`::error::${core.name}'s declarations do not compile against lib ${LIB}:`);
  console.error(`${error.stdout ?? ''}${error.stderr ?? ''}`);
  process.exit(1);
}
