const fs = require('node:fs');
const path = require('node:path');
const {buildCandidate, relativeProject, validateCandidate} = require('./snapshot.cjs');

function scan(root, indexFile, outputFile) {
  const index = JSON.parse(fs.readFileSync(indexFile, 'utf8').replace(/^\uFEFF/, ''));
  if (index.version !== 1 || !Array.isArray(index.assets) || !index.assets.length) throw new Error('Invalid native graph index.');
  const graphs = index.assets.map(item => {
    const file = path.resolve(root, item.file);
    if (relativeProject(root, file) !== item.file || path.basename(file) !== 'project.assets.json') {
      throw new Error(`Invalid native asset path: ${item.file}`);
    }
    const graph = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
    if (relativeProject(root, graph.project?.restore?.projectPath || '') !== item.project) {
      throw new Error(`Native asset project differs from its index: ${item.file}`);
    }
    if (item.scope !== undefined && !['runtime', 'development'].includes(item.scope)) throw new Error('Invalid native project scope.');
    if (item.developmentPackages !== undefined && !Array.isArray(item.developmentPackages)) throw new Error('Invalid native package scope metadata.');
    graph.scope = item.scope;
    graph.developmentPackages = item.developmentPackages;
    return graph;
  });
  const candidate = buildCandidate(graphs, root, index.assets.map(item => item.project));
  validateCandidate(candidate);
  fs.writeFileSync(outputFile, JSON.stringify(candidate), {flag: 'wx'});
  console.log(`Prepared ${Object.keys(candidate.manifests).length} NuGet manifests from native restored targets.`);
  return candidate;
}

if (require.main === module) {
  try { scan(...process.argv.slice(2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
module.exports = {scan};
