const fs = require('node:fs');
const {DETECTOR, validateCandidate} = require('./snapshot.cjs');

function snapshotRequest(candidate, environment, event, correlator) {
  if (environment.POWERFORGE_SUBMIT !== 'true' || !['push', 'workflow_dispatch'].includes(environment.GITHUB_EVENT_NAME) ||
      !event.repository?.default_branch || environment.GITHUB_REF !== `refs/heads/${event.repository.default_branch}`) {
    throw new Error('Dependency submission requires an enabled trusted default-branch run.');
  }
  if (!/^[\w.-]+\/[\w.-]+$/.test(environment.GITHUB_REPOSITORY || '') ||
      !/^[a-f0-9]{40,64}$/i.test(environment.GITHUB_SHA || '') || !/^\d+$/.test(environment.GITHUB_RUN_ID || '') || !correlator) {
    throw new Error('Missing or invalid GitHub snapshot context.');
  }
  validateCandidate(candidate);
  return {version: 0, sha: environment.GITHUB_SHA, ref: environment.GITHUB_REF,
    job: {id: environment.GITHUB_RUN_ID, correlator}, detector: DETECTOR,
    scanned: candidate.scanned, manifests: candidate.manifests};
}

async function submit(candidate, environment, event, correlator, request = fetch) {
  const body = snapshotRequest(candidate, environment, event, correlator);
  const token = environment.POWERFORGE_SNAPSHOT_TOKEN;
  if (!token) throw new Error('Snapshot upload token is missing.');
  const api = new URL(environment.GITHUB_API_URL || 'https://api.github.com');
  if (api.protocol !== 'https:') throw new Error('Snapshot upload requires HTTPS.');
  const response = await request(`${api.href.replace(/\/$/, '')}/repos/${environment.GITHUB_REPOSITORY}/dependency-graph/snapshots`, {
    method: 'POST', signal: AbortSignal.timeout(60000), headers: {'Accept': 'application/vnd.github+json', Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json', 'X-GitHub-Api-Version': '2026-03-10'}, body: JSON.stringify(body)
  });
  if (response.status !== 201) throw new Error(`Snapshot upload failed: HTTP ${response.status}`);
  const result = await response.json();
  if (result?.result !== 'SUCCESS') throw new Error(`Snapshot not applied: ${result?.result || 'missing result'}: ${result?.message || 'No result message'}`);
}

if (require.main === module) {
  const candidate = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  submit(candidate, process.env, event, process.env.POWERFORGE_CORRELATOR)
    .then(() => console.log('NuGet dependency snapshot uploaded.'))
    .catch(error => {console.error(error.message); process.exitCode = 1;});
}
module.exports = {snapshotRequest, submit};
