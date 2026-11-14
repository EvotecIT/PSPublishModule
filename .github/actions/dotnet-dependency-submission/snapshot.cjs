// Project every restored NuGet target into GitHub's per-manifest snapshot format.
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

function packageUrl(identity) {
  const parts = identity.split('/');
  if (parts.length !== 2 || !parts.every(Boolean)) throw new Error(`Invalid NuGet identity: ${identity}`);
  return `pkg:nuget/${encodeURIComponent(parts[0].toLowerCase())}@${encodeURIComponent(parts[1].toLowerCase())}`;
}

const primaryAssetKinds = ['runtime', 'native', 'contentfiles'];

function runtimeAssetKinds(library) {
  const kinds = new Set();
  const hasFiles = section => Object.keys(library[section] || {}).some(file => path.posix.basename(file) !== '_._');
  if (hasFiles('runtime') || hasFiles('resource')) kinds.add('runtime');
  if (hasFiles('native')) kinds.add('native');
  for (const [file, asset] of Object.entries(library.runtimeTargets || {})) {
    if (path.posix.basename(file) !== '_._') kinds.add(asset.assetType === 'native' ? 'native' : 'runtime');
  }
  for (const [file, asset] of Object.entries(library.contentFiles || {})) {
    if (path.posix.basename(file) !== '_._' && asset.copyToOutput === true) kinds.add('contentfiles');
  }
  return kinds;
}

function buildCandidate(graphs, root, expectedProjects, now = new Date()) {
  const manifests = Object.create(null);
  const observed = new Set();
  for (const graph of graphs) {
    const relative = relativeProject(root, graph.project?.restore?.projectPath || '');
    if (observed.has(relative)) throw new Error(`Duplicate manifest: ${relative}`);
    observed.add(relative);
    if (!graph.targets || !Object.keys(graph.targets).length || !graph.project?.frameworks) {
      throw new Error(`Invalid restored dependency graph for ${relative}`);
    }
    const resolved = Object.create(null);
    const developmentPackages = new Set((graph.developmentPackages || []).map(identity => identity.toLowerCase()));
    function include(identity, direct, runtime, dependencies = []) {
      const url = packageUrl(identity);
      const item = resolved[url] ||= {package_url: url, relationship: 'indirect', scope: 'development', dependencies: []};
      if (direct) item.relationship = 'direct';
      if (runtime) item.scope = 'runtime';
      item.dependencies = [...new Set([...item.dependencies, ...dependencies])].sort();
    }
    for (const [targetName, target] of Object.entries(graph.targets)) {
      const tfm = targetName.split('/')[0];
      const framework = Object.entries(graph.project.frameworks).find(([name, value]) =>
        [name, value.targetAlias, value.framework].includes(tfm))?.[1];
      if (!framework) throw new Error(`Missing restored framework metadata: ${relative} (${targetName})`);
      const direct = new Set(Object.entries(framework.dependencies || {})
        .filter(([, value]) => value.target !== 'Project').map(([name]) => name.toLowerCase()));
      const identities = new Map();
      for (const identity of Object.keys(target)) {
        const name = identity.split('/')[0].toLowerCase();
        if (identities.has(name)) throw new Error(`Ambiguous dependency in ${relative} (${targetName}): ${name}`);
        identities.set(name, identity);
      }
      const runtimeReachable = new Map();
      function visitRuntime(identity, kinds) {
        if (developmentPackages.has(identity.toLowerCase())) return;
        const allowed = runtimeReachable.get(identity) || new Set();
        const added = kinds.filter(kind => !allowed.has(kind));
        if (!added.length) return;
        for (const kind of added) allowed.add(kind);
        runtimeReachable.set(identity, allowed);
        for (const name of Object.keys(target[identity].dependencies || {})) {
          const child = identities.get(name.toLowerCase());
          if (child) visitRuntime(child, added);
        }
      }
      for (const [identity, library] of Object.entries(target)) {
        const name = identity.split('/')[0].toLowerCase();
        const dependency = Object.entries(framework.dependencies || {}).find(([id]) => id.toLowerCase() === name)?.[1];
        const included = dependency?.include?.toLowerCase().split(/[,;\s]+/) || ['all'];
        const kinds = included.includes('all') ? primaryAssetKinds : included.filter(kind => primaryAssetKinds.includes(kind));
        if (direct.has(name) || library.type === 'project') visitRuntime(identity, library.type === 'project' ? primaryAssetKinds : kinds);
      }
      function hasRuntimeUse(identity, kinds, visited = new Set()) {
        if (visited.has(identity) || developmentPackages.has(identity.toLowerCase())) return false;
        visited.add(identity);
        if ([...runtimeAssetKinds(target[identity])].some(kind => kinds.has(kind))) return true;
        return Object.keys(target[identity].dependencies || {}).some(name => {
          const child = identities.get(name.toLowerCase());
          return child && hasRuntimeUse(child, kinds, visited);
        });
      }
      for (const [identity, library] of Object.entries(target)) {
        if (library.type === 'project') continue;
        if (library.type !== 'package') throw new Error(`Unsupported restored library type: ${identity}`);
        const dependencies = [];
        for (const name of Object.keys(library.dependencies || {})) {
          const child = identities.get(name.toLowerCase());
          if (!child) throw new Error(`Missing restored dependency: ${identity} -> ${name} (${targetName})`);
          if (target[child].type === 'package') dependencies.push(packageUrl(child));
        }
        include(identity, direct.has(identity.split('/')[0].toLowerCase()),
          graph.scope !== 'development' && runtimeReachable.has(identity) && hasRuntimeUse(identity, runtimeReachable.get(identity)), dependencies);
      }
    }
    // PackageDownload references are exact development inputs outside target library graphs.
    for (const framework of Object.values(graph.project.frameworks)) {
      for (const download of framework.downloadDependencies || []) {
        const version = /^\[([^,\]]+)(?:,\s*([^\]]+))?\]$/.exec(download.version || '');
        if (!version || (version[2] && version[1].trim() !== version[2].trim())) {
          throw new Error(`PackageDownload requires an exact restored version: ${download.name}`);
        }
        include(`${download.name}/${version[1].trim()}`, true, false);
      }
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
