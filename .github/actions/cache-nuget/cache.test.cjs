const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { prepare, report } = require('./cache.cjs');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'nuget-cache-contract-'));
  t.after(() => fs.rmSync(root, { recursive: true }));
  return root;
}

test('a new cache starts empty while preserving siblings and linked targets', t => {
  const root = fixture(t), cache = path.join(root, 'packages'), outside = path.join(root, 'outside');
  fs.mkdirSync(path.join(cache, 'stale', '1.0.0'), { recursive: true });
  fs.writeFileSync(path.join(cache, 'stale', '1.0.0', 'payload'), 'obsolete');
  fs.mkdirSync(outside); fs.writeFileSync(path.join(outside, 'sentinel'), 'preserve');
  fs.symlinkSync(outside, path.join(cache, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  prepare(cache);
  assert.deepEqual(fs.readdirSync(cache), []);
  assert.equal(fs.readFileSync(path.join(outside, 'sentinel'), 'utf8'), 'preserve');
});

test('a linked cache root is refused without deleting its contents', t => {
  const root = fixture(t), actual = path.join(root, 'actual'), cache = path.join(root, 'packages');
  fs.mkdirSync(actual); fs.writeFileSync(path.join(actual, 'sentinel'), 'preserve');
  fs.symlinkSync(actual, cache, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => prepare(cache), /linked package cache/);
  assert.equal(fs.readFileSync(path.join(actual, 'sentinel'), 'utf8'), 'preserve');
});

test('the report measures package payloads without following links', t => {
  const root = fixture(t), cache = path.join(root, 'packages');
  fs.mkdirSync(path.join(cache, 'one', '1.0.0'), { recursive: true });
  fs.writeFileSync(path.join(cache, 'one', '1.0.0', 'payload'), Buffer.alloc(23));
  const outside = path.join(root, 'outside');
  fs.mkdirSync(outside); fs.writeFileSync(path.join(outside, 'payload'), Buffer.alloc(99));
  fs.symlinkSync(outside, path.join(cache, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  const result = report(cache);
  assert.equal(result.bytes, 23);
  assert.deepEqual(result.largestPackages, [{ package: 'one', bytes: 23 }]);
});
