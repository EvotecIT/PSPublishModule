const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');

const repository = path.resolve(__dirname, '../..');
const workflow = fs.readFileSync(path.join(repository, '.github/workflows/BuildModule.yml'), 'utf8').split(/\r?\n/);
const step = workflow.findIndex(line => line.trim() === '- name: Clean compiler gate workspace');
const command = workflow.slice(step + 1).find(line => line.trim().startsWith('run:')).trim().slice(5);
const identity = { pid: 12345, startUtc: 'synthetic-only-no-process', address: '127.0.0.1', port: 1234 };

for (const scenario of ['held', 'incomplete-run', 'missing-whole-terminal', 'missing-qualification', 'missing-fixture-stop', 'failed-fixture-stop', 'wrong-stop-identity', 'completed', 'completed-failure', 'invalid-root', 'missing-owner-record', 'missing-owner-marker', 'wrong-owner-marker', 'wrong-runner', 'copied-owner-record', 'linked-residue', 'before-workflow']) {
  test(`CI compiler cleanup ${scenario} preserves ownership and terminal-release requirements`, () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-ci-cleanup-'));
    const runner = path.join(root, 'runner');
    const gate = path.join(runner, 'powerforge-compiler-gate');
    let owned = path.join(runner, 'pfw-a1b2c3d4');
    let owner;
    let unownedShortRoot;
    const outside = path.join(root, 'pfw-1234abcd');
    const lane = path.join(gate, 'evidence', 'whole-workflows', 'sitemap-net10.0');
    const fixture = path.join(lane, 'sitemap-script');
    const write = (file, value) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, JSON.stringify(value)); };
    try {
      fs.mkdirSync(outside);
      fs.writeFileSync(path.join(outside, 'unrelated.txt'), 'preserve unrelated root');
      fs.mkdirSync(gate, { recursive: true });
      if (scenario === 'before-workflow') {
        fs.mkdirSync(owned, { recursive: true });
      } else {
        const creation = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '. ./Build/PowerShellWholeWorkflowWorkspace.ps1; New-WholeWorkflowWorkspace -RunnerTemp $env:RUNNER_TEMP | ConvertTo-Json -Compress'], {
          cwd: repository, env: { ...process.env, RUNNER_TEMP: runner }, encoding: 'utf8', timeout: 30_000
        });
        assert.ifError(creation.error);
        assert.equal(creation.status, 0, creation.stdout + creation.stderr);
        owner = JSON.parse(creation.stdout.trim());
        owned = owner.workspacePath;
        assert.ok(owned.length <= 25);
        fs.mkdirSync(fixture, { recursive: true });
      }
      if (scenario !== 'before-workflow') {
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
        const record = path.join(gate, 'whole-workflow-workspace.json');
        const marker = path.join(owned, '.powerforge-whole-workflow-owner.json');
        if (scenario === 'invalid-root') write(record, { ...owner, workspacePath: outside });
        if (scenario === 'missing-owner-record') fs.unlinkSync(record);
        if (scenario === 'missing-owner-marker') fs.unlinkSync(marker);
        if (scenario === 'wrong-owner-marker') write(marker, { ...owner, ownershipId: crypto.randomBytes(16).toString('hex') });
        if (scenario === 'wrong-runner') write(record, { ...owner, runnerTemp: outside });
        if (scenario === 'copied-owner-record') {
          const otherId = crypto.randomBytes(16).toString('hex');
          unownedShortRoot = path.join(owner.shortBase, 'pf' + otherId.slice(0, 8));
          fs.mkdirSync(unownedShortRoot);
          fs.writeFileSync(path.join(unownedShortRoot, 'unrelated.txt'), 'preserve same-base unrelated root');
          fs.copyFileSync(marker, path.join(unownedShortRoot, '.powerforge-whole-workflow-owner.json'));
          write(record, { ...owner, ownershipId: otherId, workspacePath: unownedShortRoot });
        }
        if (scenario === 'linked-residue') fs.symlinkSync(outside, path.join(owned, 'linked-residue'), process.platform === 'win32' ? 'junction' : 'dir');
      }
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
      if (released && owner) assert.deepEqual(decision.workspaceOwnership, owner, 'The durable cleanup receipt retains the exact creation identity.');
      assert.equal(fs.readFileSync(path.join(outside, 'unrelated.txt'), 'utf8'), 'preserve unrelated root');
      if (unownedShortRoot) assert.equal(fs.readFileSync(path.join(unownedShortRoot, 'unrelated.txt'), 'utf8'), 'preserve same-base unrelated root');
      if (!released) {
        assert.ok(decision.reasons.length > 0);
        assert.equal(decision.gateRoot, gate);
        assert.ok(fs.existsSync(path.join(fixture, 'fixture.Start.json')));
      }
      if (beforeState) assert.deepEqual(fs.readFileSync(path.join(owned, 'runs', 'owned', 'scenarios', 'fixture', 'start-attempt.json')), beforeState);
    } finally {
      // All test identities are synthetic; no process was created by these receipts.
      if (owner && fs.existsSync(owned)) {
        assert.equal(owned, path.join(owner.shortBase, 'pf' + owner.ownershipId.slice(0, 8)));
        assert.equal(owner.runnerTemp, runner);
        fs.rmSync(owned, { recursive: true });
      }
      if (unownedShortRoot) fs.rmSync(unownedShortRoot, { recursive: true });
      fs.rmSync(root, { recursive: true });
    }
  });
}
