'use strict';

// Only the supported setup inputs are interpreted here. global.json rollForward
// remains the dotnet CLI's responsibility; its requested version is never repinned.
function compareVersions(a, b) {
  const [an, ap] = a.split('-', 2);
  const [bn, bp] = b.split('-', 2);
  const aa = an.split('.').map(Number);
  const bb = bn.split('.').map(Number);
  for (let i = 0; i < 3; i++) {
    if (aa[i] !== bb[i]) return aa[i] - bb[i];
  }
  if (!ap || !bp) return Number(!ap) - Number(!bp);
  return ap.localeCompare(bp, 'en', {numeric: true});
}

function versionRequest(value) {
  if (/^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$/.test(value)) {
    return {value, exact: true, channel: value.split('.').slice(0, 2).join('.')};
  }
  const match = /^(\d+)(?:\.(\d+|x))?(?:\.(x|\dxx))?$/i.exec(value);
  if (!match) throw new Error(`Unsupported .NET SDK request: ${value}`);
  const channel = `${match[1]}.${!match[2] || match[2].toLowerCase() === 'x' ? '0' : match[2]}`;
  return {value, exact: false, channel, band: match[3]?.toLowerCase() === 'x' ? null : match[3]?.[0]};
}

function matches(version, request, allowPreview = false) {
  if (request.exact) return version === request.value;
  if (!allowPreview && version.includes('-')) return false;
  if (!version.startsWith(`${request.channel}.`)) return false;
  return !request.band || version.split('.')[2].startsWith(request.band);
}

function selectVersion(versions, request, allowPreview = false) {
  return versions.filter(v => matches(v, request, allowPreview)).sort(compareVersions).at(-1);
}

function sdksFromMetadata(metadata) {
  const sdks = new Map();
  for (const release of metadata.releases || []) {
    for (const sdk of [...(release.sdks || []), ...(release.sdk ? [release.sdk] : [])]) {
      if (!sdks.has(sdk.version)) sdks.set(sdk.version, sdk);
    }
  }
  return sdks;
}

// global.json supports comments. Preserve quoted strings while removing comment
// tokens so URLs and escaped quotes in unrelated properties remain untouched.
function parseGlobalJson(text) {
  const withoutComments = text.replace(/^\uFEFF/, '').replace(
    /("(?:\\.|[^"\\])*")|\/\*[\s\S]*?\*\/|\/\/[^\r\n]*/g,
    (token, quoted) => quoted || ''
  );
  return JSON.parse(withoutComments);
}

module.exports = {compareVersions, versionRequest, matches, selectVersion, sdksFromMetadata, parseGlobalJson};
