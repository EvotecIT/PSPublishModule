const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const repository = path.resolve(__dirname, '../..');
const workflow = fs.readFileSync(path.join(repository, '.github/workflows/BuildModule.yml'), 'utf8').split(/\r?\n/);
const step = workflow.findIndex(line => line.trim() === '- name: Clean compiler gate workspace');
const command = workflow.slice(step + 1).find(line => line.trim().startsWith('run:')).trim().slice(5);
const identity = { pid: 12345, startUtc: 'synthetic-only-no-process', address: '127.0.0.1', port: 1234 };

for (const scenario of ['held', 'incomplete-run', 'missing-whole-terminal', 'missing-qualification', 'missing-fixture-stop', 'failed-fixture-stop', 'wrong-stop-identity', 'completed', 'completed-failure', 'invalid-root', 'before-workflow']) {
  test(`CI compiler cleanup ${scenario} preserves ownership and terminal-release requirements`, () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-ci-cleanup-'));
    const runner = path.join(root, 'runner');
    const gate = path.join(runner, 'powerforge-compiler-gate');
    const owned = path.join(runner, 'pfw-a1b2c3d4');
    const outside = path.join(root, 'pfw-1234abcd');
    const lane = path.join(gate, 'evidence', 'whole-workflows', 'sitemap-net10.0');
    const fixture = path.join(lane, 'sitemap-script');
    const write = (file, value) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, JSON.stringify(value)); };
    try {
      fs.mkdirSync(owned, { recursive: true });
      fs.mkdirSync(outside);
      fs.writeFileSync(path.join(outside, 'unrelated.txt'), 'preserve unrelated root');
      fs.mkdirSync(fixture, { recursive: true });
      if (scenario !== 'before-workflow') fs.writeFileSync(path.join(gate, 'whole-workflow-root.txt'), scenario === 'invalid-root' ? outside : owned);
      write(path.join(gate, 'evidence', 'whole-workflows', 'whole-workflow-gate.json'), { schemaVersion: 1, passed: scenario !== 'completed-failure', plannedLanes: 5, results: [] });
      write(path.join(lane, 'qualifier.process.json'), { ExitCode: scenario === 'completed-failure' ? 1 : 0 });
      write(path.join(lane, 'qualification.json'), { schemaVersion: 1, summary: { total: 1, passed: scenario === 'completed-failure' ? 0 : 1 } });
      write(path.join(fixture, 'fixture.Start.json'), { schemaVersion: 1, operation: 'Start', fixtureIdentity: identity });
      write(path.join(fixture, 'fixture.Stop.json'), { schemaVersion: 1, operation: 'Stop', stopped: scenario !== 'failed-fixture-stop', fixtureIdentity: scenario === 'wrong-stop-identity' ? { ...identity, pid: 54321 } : identity });
      write(path.join(fixture, 'fixture.Stop.process.json'), { ExitCode: 0 });
      if (scenario === 'held') {
        write(path.join(lane, 'cleanup-held.json'), { removed: false, runRoot: path.join(owned, 'runs', 'owned') });
        write(path.join(fixture, 'fixture-cleanup-failure.json'), { succeeded: false, error: 'Synthetic Stop failure' });
      }
      if (scenario === 'incomplete-run') write(path.join(owned, 'runs', 'owned', 'scenarios', 'fixture', 'start-attempt.json'), { schemaVersion: 1, stage: 'Starting', pid: 12345 });
      if (scenario === 'missing-whole-terminal') fs.unlinkSync(path.join(gate, 'evidence', 'whole-workflows', 'whole-workflow-gate.json'));
      if (scenario === 'missing-qualification') fs.unlinkSync(path.join(lane, 'qualification.json'));
      if (scenario === 'missing-fixture-stop') fs.unlinkSync(path.join(fixture, 'fixture.Stop.json'));
      const beforeState = scenario === 'incomplete-run' ? fs.readFileSync(path.join(owned, 'runs', 'owned', 'scenarios', 'fixture', 'start-attempt.json')) : null;
      const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', command], {
        cwd: repository, env: { ...process.env, RUNNER_TEMP: runner }, encoding: 'utf8', timeout: 30_000
      });
      assert.ifError(result.error);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      const decision = JSON.parse(fs.readFileSync(path.join(runner, 'powerforge-compiler-gate-cleanup.json'), 'utf8'));
      const released = ['completed', 'completed-failure', 'before-workflow'].includes(scenario);
      assert.equal(decision.removed, released, JSON.stringify(decision));
      assert.equal(fs.existsSync(gate), !released);
      assert.equal(fs.existsSync(owned), scenario === 'before-workflow' || !released);
      assert.equal(fs.readFileSync(path.join(outside, 'unrelated.txt'), 'utf8'), 'preserve unrelated root');
      if (!released) {
        assert.ok(decision.reasons.length > 0);
        assert.equal(decision.gateRoot, gate);
        assert.ok(fs.existsSync(path.join(fixture, 'fixture.Start.json')));
      }
      if (beforeState) assert.deepEqual(fs.readFileSync(path.join(owned, 'runs', 'owned', 'scenarios', 'fixture', 'start-attempt.json')), beforeState);
    } finally {
      // All test identities are synthetic; no process was created by these receipts.
      fs.rmSync(root, { recursive: true });
    }
  });
}
