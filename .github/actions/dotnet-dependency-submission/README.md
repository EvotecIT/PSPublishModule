# Workload-aware .NET dependency submission

Restore explicit project sets on their supported hosts, then submit the resolved
NuGet graphs with GitHub's component-detection action. Apple workloads restore on
macOS; submission runs on Linux or Windows x64 because the submission action does
not provide a macOS executable.

Use `mode: restore` (the default) after checkout:

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

Use a separate Linux job after the restore jobs. Download the named artifact from
that same workflow run, then prepare and submit it:

```yaml
- uses: actions/download-artifact@v8
  with:
    name: nuget-graphs-linux
    path: ${{ runner.temp }}/native-graphs
- uses: EvotecIT/PSPublishModule/.github/actions/dotnet-dependency-submission@actions-v1
  with:
    mode: submit
    graph-artifact: ${{ runner.temp }}/native-graphs
    correlator: product-linux
    submit: 'true'
```

The submission job also needs a checkout for GitHub's snapshot metadata. Grant it
`contents: write` only on trusted default-branch pushes or manual runs. The action
independently restricts snapshot submission to those events and that branch.
Fork pull requests and `pull_request_target` never submit through this action.
Use `submit: 'false'` with `contents: read` on pull requests to validate artifact
import. This prepares graphs without invoking the submission action or scanner.
Never promote artifacts from pull-request runs into a privileged workflow.

The SDK follows `global.json` through the shared setup action. Workloads install
before every selected input restores. A failed installation or restore stops the
job. At least one asset graph must exist before export. No build, signing, native
application launch or package publication is involved.

Artifacts contain `project.assets.json` files and a repository-relative index.
Import changes only the copied `project.restore` project path, unique name and
output path, so the scanner attributes dependencies to their original relative
project names. Package identities, framework targets and dependency edges remain
unchanged. Input and output directories must be separate, and output must be new.

Use distinct stable correlators for native host profiles, and include maintained
projects outside the normal solution explicitly. The action does not claim
coverage for unselected projects or runtime variants. Verify each native restore
and actual default-branch snapshot before retiring an existing automatic route.
Keep dependency graph and Dependabot enabled throughout that transition.
