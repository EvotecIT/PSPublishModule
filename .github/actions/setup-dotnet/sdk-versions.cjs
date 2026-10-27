'use strict';

// SDK matching follows repository global.json policy. The same file governs
// the dotnet CLI after setup; no workflow SDK version overrides it.
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
  if (request.minimum && compareVersions(version, request.minimum) < 0) return false;
  return !request.band || version.split('.')[2].startsWith(request.band);
}

function globalSdkRequest(sdk) {
  if (!sdk || !versionRequest(sdk.version).exact || sdk.version.includes('-')) {
    throw new Error('global.json SDK declarations require a stable three-part version.');
  }
  const policy = sdk.rollForward || 'latestPatch';
  if (!['latestFeature', 'latestPatch', 'disable'].includes(policy)) {
    throw new Error(`Unsupported SDK rollForward policy: ${policy}`);
  }
  const request = versionRequest(sdk.version);
  if (policy !== 'disable') {
    request.exact = false;
    request.minimum = sdk.version;
    request.band = policy === 'latestPatch' ? String(Math.floor(Number(sdk.version.split('.')[2]) / 100)) : null;
  }
  request.allowPreview = sdk.allowPrerelease === true;
  return request;
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

module.exports = {compareVersions, versionRequest, matches, selectVersion, sdksFromMetadata, parseGlobalJson, globalSdkRequest};
