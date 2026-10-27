# Module test CI

Use the shared workflow to run a module's Pester suite on PowerShell 7 and Windows
PowerShell 5.1. It saves the exact Pester version, installs manifest dependencies,
preserves the selected host's module paths, and uploads NUnit XML, JUnit XML and
a Markdown failure summary. The summary also appears on the workflow run.

```yaml
name: Test
on: [push, pull_request, workflow_dispatch]
permissions:
  contents: read
jobs:
  pester:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-module-test.yml@<commit-sha>
    with:
      test_path: Module/Tests
      pester_version: '6.1.0'
```

Replace `<commit-sha>` with a full reviewed commit SHA. The workflow builds the
test runner from that same revision; it does not install PSPublishModule from
the Gallery. It sets up the engine's declared .NET SDKs and the consumer's
`global.json`, when present. Tests import the module under test themselves.

The default matrix is Ubuntu/PowerShell 7, Windows/PowerShell 7 and
Windows/Windows PowerShell 5.1. Set `project_path` when the module manifest is
outside the repository root or its immediate subdirectories. Test paths are
relative to the checkout. Customize the matrix and optional build commands:

```yaml
    with:
      project_path: Module
      test_path: Module/Tests
      pester_version: '6.1.0'
      matrix_json: '{"include":[{"os":"windows-latest","shell":"powershell"},{"os":"macos-15","shell":"pwsh"}]}'
      build_script: |
        dotnet build MyModule.PowerShell/MyModule.PowerShell.csproj -c Debug
        if ($LASTEXITCODE -ne 0) { throw 'Module build failed.' }
      result_artifact_name: pester-results
```

Build commands run in the selected shell. Check each native command's exit code
when supplying several commands. Public repositories use standard hosted runners;
trusted private repositories can supply their existing self-hosted labels in
`matrix.os`. Keep untrusted changes on the repository's approved isolated runners.

Each job uploads `<result_artifact_name>-<matrix-index>` containing `nunit.xml`,
`junit.xml` and `summary.md`, including when tests fail. Setup/build failures may
have no XML; the job summary identifies that stage. Artifacts are retained for
seven days. `result_artifact_prefix` exposes the prefix to dependent jobs.
The default job timeout is 30 minutes; `test_timeout_seconds` defaults to 1200.
Discovery/setup failures and empty suites fail the test job.

## Run locally

```powershell
Invoke-ModuleTestSuite -ProjectPath . -TestPath ./Module/Tests -PesterVersion 6.1.0 `
    -ResultsPath TestResults/nunit.xml -JUnitResultsPath TestResults/junit.xml `
    -SummaryPath TestResults/summary.md -SkipImport -FailIfNoTests
```

Add `-WindowsPowerShell` to require Windows PowerShell 5.1. The CLI exposes the
same engine through `powerforge test --pester-version 6.1.0`, with
`--results-path`, `--junit-results-path`, `--summary-path`, `--windows-powershell`
and `--fail-if-no-tests`. Pinned installs require stable Pester 5 or newer.
Omitting these options preserves existing Pester selection and temporary NUnit
result behavior. `-SkipDependencies` skips installation, including pinned Pester;
the requested version must then already be available to the selected host.

Repository reads and installs retry transient 404, 408, 429, 5xx and timeout
failures with capped exponential backoff. Child-process installs try at most
four times with 1, 2 and 4 second delays; managed reads use their existing retry
count and initial-delay settings. Authentication, invalid input and missing
tools fail without retry. Every retry logs its attempt and delay; an exhausted
install reports the package/version and the repository checks to make next.
Publishing operations do not gain 404 retries.
