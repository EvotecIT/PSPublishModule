const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

// A new default-branch key is populated from a cold, ephemeral package store.
// Do this before any restore, never by deleting versions from a partial graph.
function prepare(cache) {
  if (fs.existsSync(cache) && fs.lstatSync(cache).isSymbolicLink()) throw new Error('Refusing a linked package cache.');
  fs.rmSync(cache, { recursive: true, force: true });
  fs.mkdirSync(cache, { recursive: true });
}

function report(cache) {
  function bytes(folder) {
    let total = 0;
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const file = path.join(folder, entry.name);
      total += entry.isDirectory() ? bytes(file) : fs.statSync(file).size;
    }
    return total;
  }
  const packages = fs.existsSync(cache) ? fs.readdirSync(cache, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && !entry.isSymbolicLink())
    .map(entry => ({ package: entry.name, bytes: bytes(path.join(cache, entry.name)) })) : [];
  return { bytes: packages.reduce((sum, pkg) => sum + pkg.bytes, 0),
    packageIds: packages.length, largestPackages: packages.sort((a, b) => b.bytes - a.bytes).slice(0, 10) };
}

if (require.main === module) {
  if (process.env.RUNNER_ENVIRONMENT !== 'github-hosted') throw new Error('Actions package caches require an ephemeral GitHub-hosted runner.');
  const cache = path.join(os.homedir(), '.nuget', 'packages');
  if (process.argv[2] === 'prepare') prepare(cache);
  else if (process.argv[2] === 'report') {
    const text = `NuGet package cache before save: ${JSON.stringify(report(cache))}\n`;
    process.stdout.write(text);
    if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, text);
  } else throw new Error('Expected prepare or report.');
}
module.exports = { prepare, report };
