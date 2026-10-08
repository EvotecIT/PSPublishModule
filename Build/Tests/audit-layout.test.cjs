const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../PowerForge.Web/Assets/Audit/layout.js'), 'utf8');
function element(left, right, hidden = false) {
  return { textContent: 'Create(Stream, options)', getBoundingClientRect: () => ({ left, right, width: right - left, height: 80 }), closest: () => hidden };
}
function inspect(elements) {
  return JSON.parse(vm.runInNewContext(source + '(["main", ".member-card"])', {
    window: { innerWidth: 390 }, document: { querySelectorAll: () => elements }, getComputedStyle: () => ({ visibility: 'visible' }), Set, JSON
  }));
}
test('clipped member cards fail and overlapping selectors report each element once', () => {
  const findings = inspect([element(16, 654)]);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].right, 654);
});
test('visible responsive cards pass and hidden navigation stays outside the layout scope', () => {
  assert.equal(inspect([element(16, 374), element(-400, -10, true)]).length, 0);
});
