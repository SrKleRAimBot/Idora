// Compiles a strict TypeScript consumer of each built extension against the oldest version of
// every peer dependency its range admits, with skipLibCheck off.
//
// A package's declarations can import from a peer's module paths or types that only newer peers
// ship, which a consumer then sees as a compile error the moment they import the extension. The
// workspace's own tests run against one peer version, so they cannot catch that; this does.
//
// Errors inside the peers' own declarations are not this package's to fix, and some peers have
// them (drizzle-orm's fail a full check), so only errors in idora.js, the extension or the consumer
// fail the check. Run after `npm run build && npm run build --workspaces`.
//
//   node scripts/check-peer-floors.mjs

import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));
const core = readJson(join(root, 'package.json'));

/** The lowest version a range admits: the smallest version named by any of its alternatives. */
function floorOf(range) {
  const versions = range.split('||').map((alternative) => {
    const match = /(\d+)\.(\d+)\.(\d+)/.exec(alternative);
    if (match === null) throw new Error(`cannot find a version in the peer range '${range}'`);
    return match.slice(1, 4).map(Number);
  });
  versions.sort((a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]);
  return versions[0].join('.');
}

const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
let failed = false;

for (const dir of readdirSync(join(root, 'extensions'))) {
  const extensionRoot = join(root, 'extensions', dir);
  const pkg = readJson(join(extensionRoot, 'package.json'));
  const peers = Object.entries(pkg.peerDependencies ?? {})
    .filter(([name]) => name !== core.name)
    .map(([name, range]) => `${name}@${floorOf(range)}`);

  const project = mkdtempSync(join(tmpdir(), `idora-peer-floor-${dir}-`));
  writeFileSync(join(project, 'package.json'), JSON.stringify({ name: 'consumer', private: true, type: 'module' }));
  execFileSync(
    npm,
    ['install', '--no-audit', '--no-fund', '--ignore-scripts', ...peers,
      `typescript@${core.devDependencies.typescript}`, `@types/node@${core.devDependencies['@types/node']}`],
    { cwd: project, stdio: ['ignore', 'ignore', 'inherit'] },
  );

  // Copied in after the install, which would otherwise prune them as unknown to its package.json.
  for (const [name, from] of [[core.name, root], [pkg.name, extensionRoot]]) {
    const to = join(project, 'node_modules', name);
    mkdirSync(to, { recursive: true });
    cpSync(join(from, 'package.json'), join(to, 'package.json'));
    cpSync(join(from, 'dist'), join(to, 'dist'), { recursive: true });
  }

  const entryPoints = Object.keys(pkg.exports).filter((path) => path !== './package.json');
  writeFileSync(
    join(project, 'consumer.ts'),
    entryPoints.map((path, i) => `import * as entry${i} from '${pkg.name}${path.slice(1)}';\nexport { entry${i} };\n`).join(''),
  );

  let output = '';
  try {
    execFileSync(
      join(project, 'node_modules', '.bin', process.platform === 'win32' ? 'tsc.cmd' : 'tsc'),
      ['--noEmit', '--strict', '--module', 'nodenext', '--moduleResolution', 'nodenext', '--target', 'es2022',
        '--types', 'node', 'consumer.ts'],
      { cwd: project, encoding: 'utf8' },
    );
  } catch (error) {
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  }

  const errors = output.split('\n').filter((line) => /error TS\d+/.test(line));
  const ours = errors.filter((line) => line.startsWith('consumer.ts') || /node_modules\/idora\.js/.test(line));
  const label = `${pkg.name} against ${peers.join(', ')}`;
  if (ours.length > 0) {
    failed = true;
    console.error(`::error::${label}: the published declarations do not compile`);
    for (const line of ours) console.error(`  ${line}`);
  } else {
    console.log(`${label}: ok${errors.length > 0 ? ` (${errors.length} errors inside the peers' own declarations, ignored)` : ''}`);
  }
}

if (failed) process.exit(1);
