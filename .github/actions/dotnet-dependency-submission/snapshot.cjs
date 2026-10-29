// Convert the pinned scanner's per-manifest graph into GitHub's snapshot format.
const path = require('node:path');

const DETECTOR = Object.freeze({name: 'PowerForgeNuGet', version: '1.0.0', url: 'https://github.com/EvotecIT/PSPublishModule'});

function relativeProject(root, file) {
  const paths = /^[A-Za-z]:[\\/]|^\\\\/.test(root) ? path.win32 : path.posix;
  const relative = paths.relative(root, file).replace(/\\/g, '/');
  if (!relative || paths.isAbsolute(relative) || relative.split('/').some(p => !p || p === '..' || p === '.')) {
    throw new Error(`Manifest is outside the scan root: ${file}`);
  }
  return relative;
}

function buildCandidate(scan, root, expectedProjects, now = new Date()) {
  if (scan.resultCode !== 'Success' || !scan.dependencyGraphs || !Array.isArray(scan.componentsFound)) {
    throw new Error('Component Detection did not produce a successful graph.');
  }
  const components = new Map(scan.componentsFound.map(item => [item.component.id, item.component]));
  function packageUrl(id) {
    const component = components.get(id);
    if (!component) throw new Error(`Graph references an unknown component: ${id}`);
    if (component.type !== 'NuGet') return null;
    if (!component.name || !component.version) throw new Error(`Incomplete NuGet identity: ${id}`);
    return `pkg:nuget/${encodeURIComponent(component.name.toLowerCase())}@${encodeURIComponent(component.version)}`;
  }
  const manifests = Object.create(null);
  const observed = new Set();
  for (const [file, entry] of Object.entries(scan.dependencyGraphs)) {
    const relative = relativeProject(root, file);
    if (observed.has(relative)) throw new Error(`Duplicate manifest: ${relative}`);
    observed.add(relative);
    if (!entry.graph || !Array.isArray(entry.explicitlyReferencedComponentIds) || !Array.isArray(entry.developmentDependencies)) {
      throw new Error(`Invalid dependency graph for ${relative}`);
    }
    const direct = new Set(entry.explicitlyReferencedComponentIds);
    const development = new Set(entry.developmentDependencies);
    const resolved = Object.create(null);
    for (const [id, children] of Object.entries(entry.graph)) {
      const url = packageUrl(id);
      if (!url) continue;
      if (children !== null && !Array.isArray(children)) throw new Error(`Invalid edges for ${id}`);
      const dependencies = [...new Set((children || []).map(packageUrl).filter(Boolean))].sort();
      if (Object.hasOwn(resolved, url)) throw new Error(`Duplicate NuGet identity in manifest: ${url}`);
      resolved[url] = {package_url: url, relationship: direct.has(id) ? 'direct' : 'indirect',
        scope: development.has(id) ? 'development' : 'runtime', dependencies};
    }
    // A missing leaf must fail instead of silently dropping a dependency edge.
    for (const item of Object.values(resolved)) {
      for (const child of item.dependencies) if (!Object.hasOwn(resolved, child)) throw new Error(`Missing graph node: ${child}`);
    }
    if (Object.keys(resolved).length) manifests[relative] = {name: relative, file: {source_location: relative}, resolved};
  }
  const expected = new Set(expectedProjects);
  if (!expected.size || observed.size !== expected.size || [...expected].some(file => !observed.has(file))) {
    throw new Error('Scanned manifest coverage differs from the restored project index.');
  }
  if (!Object.keys(manifests).length) throw new Error('No resolved NuGet manifests were found.');
  return {detector: DETECTOR, scanned: now.toISOString(), manifests};
}

function validateCandidate(candidate) {
  if (!candidate || !candidate.manifests || !Object.keys(candidate.manifests).length || !Number.isFinite(Date.parse(candidate.scanned))) {
    throw new Error('Invalid dependency snapshot candidate.');
  }
  for (const [file, manifest] of Object.entries(candidate.manifests)) {
    if (/^[A-Za-z]:/.test(file) || relativeProject('/repository', `/repository/${file}`) !== file || manifest.name !== file || manifest.file?.source_location !== file) {
      throw new Error('Invalid snapshot manifest path.');
    }
    const resolved = manifest.resolved;
    if (!resolved || !Object.keys(resolved).length) throw new Error('Empty snapshot manifest.');
    for (const [url, item] of Object.entries(resolved)) {
      if (!url.startsWith('pkg:nuget/') || item.package_url !== url || !['direct', 'indirect'].includes(item.relationship) ||
          !['runtime', 'development'].includes(item.scope) || !Array.isArray(item.dependencies) || item.dependencies.some(child => !Object.hasOwn(resolved, child))) {
        throw new Error('Invalid snapshot dependency or edge.');
      }
    }
  }
}

module.exports = {DETECTOR, relativeProject, buildCandidate, validateCandidate};
