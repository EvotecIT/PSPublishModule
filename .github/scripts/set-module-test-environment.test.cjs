'use strict';
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');

for (const [name, term, lang] of [
  ['runner defaults', '', ''],
  ['both overrides', 'xterm-256color', 'C.UTF-8'],
  ['terminal only', 'xterm-256color', ''],
  ['locale only', '', 'C.UTF-8']
]) {
  test(`test environment preserves unspecified values: ${name}`, () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'module-test-env-'));
    const file = path.join(directory, 'job-env');
    fs.writeFileSync(file, 'EXISTING=keep\n');
    try {
      const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File',
        path.join(__dirname, 'Set-ModuleTestEnvironment.ps1')], {
        encoding: 'utf8', timeout: 30000,
        env: {...process.env, GITHUB_ENV: file, MODULE_MATRIX_TERM: term, MODULE_MATRIX_LANG: lang,
          TERM: 'dumb', LANG: 'en_US.UTF-8'}
      });
      assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
      const updates = Object.fromEntries(fs.readFileSync(file, 'utf8').trim().split(/\r?\n/)
        .map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
      assert.deepEqual(updates, {EXISTING: 'keep', ...(term ? {TERM: term} : {}), ...(lang ? {LANG: lang} : {})});
      const nextStep = {TERM: 'dumb', LANG: 'en_US.UTF-8', ...updates};
      assert.equal(nextStep.TERM, term || 'dumb');
      assert.equal(nextStep.LANG, lang || 'en_US.UTF-8');
    } finally { fs.rmSync(directory, {recursive: true, force: true}); }
  });
}
