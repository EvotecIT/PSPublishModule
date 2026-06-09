/* eslint-disable no-console */
const fs = require('fs');
const path = require('path');

function fail(msg) {
  console.error(msg);
  process.exit(1);
}

function normalizeNewlines(s) {
  return s.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
}

function findClassOpenBraceIndex(text, className) {
  const re = new RegExp(String.raw`\bclass\s+${className}\b`);
  const m = re.exec(text);
  if (!m) return -1;
  const start = m.index;
  const brace = text.indexOf('{', start);
  return brace;
}

function findClassCloseBraceIndex(text) {
  // Best-effort: assume the last non-whitespace char is the class-closing brace.
  const trimmed = text.replace(/\s+$/g, '');
  const idx = trimmed.lastIndexOf('}');
  return idx;
}

function backtrackToAttributesAndDocs(text, idx) {
  // Move split point to the start of the method block, including leading blank lines,
  // attributes (e.g. [Fact]) and XML doc comments (///).
  let start = text.lastIndexOf('\n', idx - 1);
  start = start < 0 ? 0 : start + 1;

  while (start > 0) {
    const prevEnd = start - 1;
    const prevStart = text.lastIndexOf('\n', prevEnd - 1);
    const lineStart = prevStart < 0 ? 0 : prevStart + 1;
    const line = text.slice(lineStart, prevEnd);
    if (/^\s*$/.test(line) || /^\s*\[/.test(line) || /^\s*\/\/\//.test(line)) {
      start = lineStart;
      continue;
    }
    break;
  }
  return start;
}

function main() {
  const file = process.argv[2];
  const className = process.argv[3];
  const marker = process.argv[4];
  const outFile = process.argv[5];

  if (!file || !className || !marker || !outFile) {
    fail('Usage: node Build/split-cs-class.js <file> <className> <markerSubstring> <outFile>');
  }

  const fullPath = path.resolve(file);
  const outPath = path.resolve(outFile);
  if (!fs.existsSync(fullPath)) fail(`File not found: ${fullPath}`);

  const raw = fs.readFileSync(fullPath, 'utf8');
  const text = normalizeNewlines(raw);

  const classOpen = findClassOpenBraceIndex(text, className);
  if (classOpen < 0) fail(`Class '${className}' not found in ${file}`);

  const markerIndex = text.indexOf(marker);
  if (markerIndex < 0) fail(`Marker not found in ${file}: ${marker}`);

  const classClose = findClassCloseBraceIndex(text);
  if (classClose < 0 || classClose <= classOpen) fail(`Failed to locate class closing brace in ${file}`);
  if (markerIndex <= classOpen || markerIndex >= classClose) fail(`Marker is outside class body in ${file}`);

  const splitIndex = backtrackToAttributesAndDocs(text, markerIndex);
  if (splitIndex <= classOpen || splitIndex >= classClose) fail(`Backtracked split index is outside class body in ${file}`);

  const header = text.slice(0, classOpen + 1);
  const moved = text.slice(splitIndex, classClose);
  const suffix = text.slice(classClose); // includes closing brace and trailing whitespace

  const original = text.slice(0, splitIndex) + suffix;
  const split = header + '\n' + moved.replace(/^\n+/, '') + suffix;

  fs.writeFileSync(fullPath, original, 'utf8');
  fs.writeFileSync(outPath, split, 'utf8');

  console.log(`Split '${file}' -> '${outFile}' at marker.`);
}

main();
