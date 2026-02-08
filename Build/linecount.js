/* eslint-disable no-console */
const fs = require('fs');
const path = require('path');

function walk(dir, fn) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const ent of entries) {
    const full = path.join(dir, ent.name);
    if (ent.isDirectory()) {
      if (ent.name === 'bin' || ent.name === 'obj' || ent.name === '.git' || ent.name === '.nuget') continue;
      walk(full, fn);
      continue;
    }
    if (ent.isFile()) fn(full);
  }
}

function countLines(filePath) {
  const txt = fs.readFileSync(filePath, 'utf8');
  // split keeps 1 line for files without trailing newline
  return txt.split(/\r\n|\r|\n/).length;
}

const root = process.argv[2] ? path.resolve(process.argv[2]) : process.cwd();
const max = Number(process.argv[3] || 800);
const exts = new Set(['.cs']);

const rows = [];
walk(root, (p) => {
  if (!exts.has(path.extname(p))) return;
  const rel = path.relative(root, p).replaceAll('\\', '/');
  const n = countLines(p);
  if (n > max) rows.push([n, rel]);
});

rows.sort((a, b) => b[0] - a[0] || a[1].localeCompare(b[1]));
for (const [n, rel] of rows) {
  console.log(String(n).padStart(6, ' ') + '  ' + rel);
}
