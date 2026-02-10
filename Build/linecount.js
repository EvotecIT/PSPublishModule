/* eslint-disable no-console */
const fs = require('fs');
const path = require('path');

function parseArgs(argv) {
  // Backwards compatible with: node linecount.js <root> <max>
  // New flags:
  //   --root <path>
  //   --max <n>
  //   --ext <.cs,.ps1> (comma/semicolon separated; accepts with or without leading dot)
  //   --exclude <bin,obj,.git,.nuget> (directory names; comma/semicolon separated)
  const out = {
    root: null,
    max: null,
    exts: null,
    exclude: null,
  };

  const args = argv.slice();
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--root' || a === '--path') {
      out.root = args[++i];
      continue;
    }
    if (a === '--max') {
      out.max = args[++i];
      continue;
    }
    if (a === '--ext' || a === '--exts') {
      out.exts = args[++i];
      continue;
    }
    if (a === '--exclude') {
      out.exclude = args[++i];
      continue;
    }
  }

  // Positional fallback
  if (!out.root && args[0] && !args[0].startsWith('--')) out.root = args[0];
  if (!out.max && args[1] && !args[1].startsWith('--')) out.max = args[1];

  return out;
}

function splitList(text) {
  if (!text) return [];
  return String(text)
    .split(/[;,]/g)
    .map((s) => s.trim())
    .filter(Boolean);
}

function normalizeExt(ext) {
  const e = String(ext).trim();
  if (!e) return null;
  return e.startsWith('.') ? e.toLowerCase() : ('.' + e.toLowerCase());
}

function walk(dir, fn, excludeDirs) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const ent of entries) {
    const full = path.join(dir, ent.name);
    if (ent.isDirectory()) {
      if (excludeDirs.has(ent.name)) continue;
      walk(full, fn, excludeDirs);
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

const parsed = parseArgs(process.argv.slice(2));
const root = parsed.root ? path.resolve(parsed.root) : process.cwd();
const max = Number(parsed.max || 800);
const exts = new Set((splitList(parsed.exts).map(normalizeExt).filter(Boolean)));
if (exts.size === 0) exts.add('.cs');

const excludeDirs = new Set(splitList(parsed.exclude));
if (excludeDirs.size === 0) {
  excludeDirs.add('bin');
  excludeDirs.add('obj');
  excludeDirs.add('.git');
  excludeDirs.add('.nuget');
}

const rows = [];
walk(root, (p) => {
  if (!exts.has(path.extname(p))) return;
  const rel = path.relative(root, p).replaceAll('\\', '/');
  const n = countLines(p);
  if (n > max) rows.push([n, rel]);
}, excludeDirs);

rows.sort((a, b) => b[0] - a[0] || a[1].localeCompare(b[1]));
for (const [n, rel] of rows) {
  console.log(String(n).padStart(6, ' ') + '  ' + rel);
}

// Make this usable in CI: non-zero when any files exceed the limit.
process.exitCode = rows.length > 0 ? 1 : 0;
