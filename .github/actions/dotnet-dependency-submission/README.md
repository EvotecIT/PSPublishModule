# Workload-aware .NET dependency submission

Restore explicit project sets on their native hosts, scan their resolved NuGet
graphs in read-only jobs, and upload snapshots from a separate trusted job.
PowerForge owns the restore, transport and snapshot conversion. Restored
`project.assets.json` targets supply the package identities and dependency edges.

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

Use this scan path on pull requests too. Conversion reads the indexed native
asset files and unions package identities and immediate edges across every
restored target framework and runtime identifier. Packages reached through
project references remain in each consuming project's graph. Direct references
come from that project's target-specific package roots.

Native export records evaluated `IsTestProject` and `DevelopmentDependency`
properties and explicit package `developmentDependency` metadata from the restored
NuGet cache. Development projects keep their entire dependency graph in development
scope. Tooling-only dependency closures also remain development dependencies;
a shared dependency can have runtime scope through a regular package root.

For regular projects, runtime scope requires a runtime dependency path and
runtime, native, resource or copied content contributions from the package or its
permitted dependency closure in at least one restored target. Dependency-only
meta-packages retain those runtime requirements. `IncludeAssets` restrictions
apply to each path, including native-only and copied-content-only paths.
Placeholder `_._` files provide no runtime content. Compile/build/analyzer-only
paths retain development scope. `PrivateAssets` controls propagation
to consumers. Development classification uses the evaluated project properties
and explicit dependency metadata.
Exact `PackageDownload` inputs are retained as direct development dependencies.
Manifest identities must match the restore index, and missing dependencies fail
conversion. Scan mode projects the indexed JSON locally.

Existing version-one graph artifacts remain readable. Artifacts without the
new scope metadata retain the ordinary NuGet runtime default and require native
re-export to establish development-project/tooling scope. Scope follows the
[GitHub dependency submission contract](https://docs.github.com/en/rest/dependency-graph/dependency-submission).

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

By default, SDK setup follows `global.json` (or `global-json-file`) and a missing
policy file fails setup. A workflow that has already configured its SDK can set
`setup-dotnet: 'false'` on the restore step. That mode validates and logs the SDK
selected by `dotnet --version`, retaining its normal SDK resolution. It skips
PowerForge's policy-file setup and creates no SDK policy file. SDK selection
remains with the caller. The input accepts only `true` or `false`; scans and
uploads do not configure or require an SDK.

Workloads install before restore using existing
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

Keep automatic dependency submission, dependency graph and Dependabot enabled.
Verify native restores, read-only scans and actual default-branch snapshots;
controlled scanner success does not settle a failed generated submission job.
Scanner updates change the fixed version and platform digests
in `scan.cjs` together and require renewed scan/snapshot proof. Native restore
jobs can use Windows, macOS or Linux; scanner execution is supported on Linux x64.
