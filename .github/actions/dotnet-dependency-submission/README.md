# Workload-aware .NET dependency submission

Restore explicit project sets on their native hosts, scan their resolved NuGet
graphs in read-only jobs, and upload snapshots from a separate trusted job.
PowerForge owns the restore, transport and snapshot conversion. Microsoft
Component Detection supplies the package graph.

Use `mode: restore` (the default) after checkout on each native host:

```yaml
- uses: EvotecIT/PSPublishModule/.github/actions/dotnet-dependency-submission@actions-v1
  id: graphs
  with:
    projects-json: '["Product.sln", "Web/Converter.csproj"]'
    workloads: wasm-tools
- uses: actions/upload-artifact@v7
  with:
    name: nuget-graphs-linux
    path: ${{ steps.graphs.outputs.graphs-path }}
    include-hidden-files: true
    if-no-files-found: error
    retention-days: 1
```

In a dependent Linux x64 job with `contents: read`, download that
profile's graph artifact from the same workflow run and scan it:

```yaml
- uses: actions/download-artifact@v8
  with:
    name: nuget-graphs-linux
    path: ${{ runner.temp }}/native-graphs
- uses: EvotecIT/PSPublishModule/.github/actions/dotnet-dependency-submission@actions-v1
  id: snapshot
  with:
    mode: scan
    graph-artifact: ${{ runner.temp }}/native-graphs
- uses: actions/upload-artifact@v7
  with:
    name: nuget-snapshot-linux
    path: ${{ steps.snapshot.outputs.snapshot-path }}
    if-no-files-found: error
    retention-days: 1
```

Use this scan path on pull requests too. It downloads Component Detection 8.0.6
from its fixed release URL and verifies the SHA-256 digest
before writing an executable. The scanner child receives runtime and temporary
paths without job credentials. Scanner binaries, logs and extraction files are
removed after the scan. Snapshot conversion preserves immediate per-manifest
edges, direct references and development scopes; it does not infer edges from
global top-level referrers. Scanned project coverage must match the restore index.

Only the upload job needs `contents: write`. Restrict that job to default-branch
pushes and manual runs, and make it depend on every profile's successful scan:

```yaml
- uses: actions/download-artifact@v8
  with:
    name: nuget-snapshot-linux
    path: ${{ runner.temp }}/snapshot
- uses: EvotecIT/PSPublishModule/.github/actions/dotnet-dependency-submission@actions-v1
  with:
    mode: submit
    snapshot-file: ${{ runner.temp }}/snapshot/snapshot.json
    correlator: product-linux
    submit: 'true'
```

The upload mode validates the JSON and binds its SHA, ref, detector and job to the
current run. It requires HTTP 201 and an API `SUCCESS` result before reporting
an applied snapshot; `ACCEPTED`, `INVALID` or missing results fail the job.
It independently rejects pull requests, `pull_request_target`, tags
and non-default branches. It invokes no scanner. Use distinct stable correlators
for native host profiles. Download only named artifacts from the same workflow
run; never promote pull-request artifacts into a privileged workflow.

The SDK follows `global.json`. Workloads install before restore using existing
SDK manifests by default. Set `workload-version` to an exact workload-set version
when a project requires newer targeting packs within the same SDK feature band;
it is passed to `dotnet workload install --version`. Installation or restore
failure stops export. No signing, native application launch or package
publication is involved.

Graph artifacts contain asset files and an index of the source root and relative
paths. Import validates both original project identities before rebasing only
copied restore-path metadata. Package targets and edges remain unchanged. Output
directories must be new and outside their source. These are integrity checks for
trusted artifacts, not artifact authentication. Coverage is limited to selected
projects and restored runtime variants.

Before retiring an automatic submission route, verify all native restores,
read-only scans and actual default-branch snapshots. Keep dependency graph and
Dependabot enabled. Scanner updates change the fixed version and platform digests
in `scan.cjs` together and require renewed scan/snapshot proof. Native restore
jobs can use Windows, macOS or Linux; scanner execution is supported on Linux x64.
