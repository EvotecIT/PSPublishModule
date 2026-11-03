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
relative to the checkout. Declare prerequisite builds in a shared test profile:

```yaml
    with:
      project_path: Module
      test_path: Module/Tests
      pester_version: '6.1.0'
      matrix_json: '{"include":[{"os":"windows-latest","shell":"powershell"},{"os":"macos-15","shell":"pwsh"}]}'
      build_config_path: Build/test-ci.json
      build_profile: module
      result_artifact_name: pester-results
```

`Build/test-ci.json` contains project policy, not command-line setup:

```json
{
  "projectRoot": "..",
  "profiles": [{
    "name": "module",
    "configuration": "Debug",
    "buildProjects": [
      { "path": "MyModule.PowerShell/MyModule.PowerShell.csproj", "frameworks": ["net8.0"] },
      { "path": "MyModule.TestHost/MyModule.TestHost.csproj", "frameworks": ["net8.0"] }
    ]
  }]
}
```

`matrix.build_profile` can select different build profiles for Windows and Unix.
Optional `matrix.term` and `matrix.lang` preserve terminal/locale policy for the
whole job, including the selected Pester host. Omitted values keep the runner defaults.
`build_script_path` runs an optional project-specific setup script in the selected
PowerShell host. Existing callers can still use `build_script`, but new callers
should keep executable setup out of their YAML. Neither input is needed for a
script module. `dotnet_version` adds consumer SDKs, including when there is no
consumer `global.json`, while preserving the engine and consumer SDK policies.

Public repositories use standard hosted runners;
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
Invoke-ModuleTestSuite -TestPath ./Module/Tests -PesterVersion 6.1.0 `
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

## .NET and native smoke tests

The .NET workflow uses the same SDK/runner setup. PowerForge creates conventional
build, test and native-publish steps and executes them through the existing
`WorkspaceValidationService`. Each test project has its own TRX directory.
Each build/test/probe process has a bounded timeout, and the workflow uploads
TRX files and a Markdown summary on success or failure.

```yaml
  dotnet:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-dotnet-test.yml@<commit-sha>
    with:
      config_path: Build/test-ci.json
      profile: test
```

Add a profile to the same configuration:

```json
{
  "name": "test",
  "testProjects": ["MyModule.Tests/MyModule.Tests.csproj"],
  "environmentVariables": { "TERM": "xterm-256color" }
}
```

The default matrix is Windows/net472, Windows/net8.0 and Ubuntu/net8.0. Override
`matrix_json` with `os`, `framework`, optional `runtime` and optional `profile`.
Keep event filters and the decision to run macOS only on pull requests in the
consumer workflow. `result_artifact_name` is the artifact prefix, with the
matrix job index appended; retention is seven days.

Native profiles use `nativeProjects` with `path`, `executable`, optional
`arguments` and optional `runEnvironments`. The workflow matrix supplies the
framework and runtime identifier. PowerForge publishes with AOT enabled, keeps
compiler/linker warnings as errors, and runs the executable once per environment
overlay. Native executables remain temporary; the artifact contains the summary.

`beforeTests` and `afterTests` accept structured `WorkspaceValidationStep`
commands for product-specific probes. Use explicit executable/argument arrays
or a checked-in script path. Builds, retries, module-path setup, test-tool
installation and result generation belong in PowerForge. A failed build, test,
native process or probe fails the job and prevents later steps in that profile.
The local equivalent is:

```powershell
powerforge test-dotnet --config Build/test-ci.json --profile test --framework net8.0 --results-path TestResults
```

## Reference consumer audit

I used ConsoleMonster's three workflows as the reference. Its 376-line test
workflow repeats SDK setup, builds, test invocation and result upload across
platforms. Its 136-line website CI already delegates site verification, but owns
the rendering-budget setup and measurement job. Website deployment already uses
the shared workflow in 40 lines.

| Current block | Shared owner | Consumer policy that stays local |
| --- | --- | --- |
| .NET SDK setup | Existing PowerForge SDK action, reused by both test workflows | Required SDK versions/global.json |
| Module and test-host builds | PowerForge .NET test plan | Project paths, Debug/Release and supported frameworks |
| xUnit on net472/net8/net10 | PowerForge .NET workflow and test service | Test projects, OS/framework matrix and event coverage |
| Pester on PowerShell 5.1/7 | Existing module test service and reusable workflow | Manifest/test paths and exact Pester version |
| Native AOT build/run | PowerForge .NET test plan | Project, runtime, executable and locale variants |
| PS 5.1 parser check | Shared probe execution | ConsoleMonster's existing syntax assertions |
| Manifest/export import smoke | Shared probe execution | Expected ConsoleMonster exports |
| Python PTY harness contracts | Shared probe execution | Harness tests and Darwin/Linux assertions |
| Glyph locale and no-cursor-reply probes | Shared bounded probe execution | ConsoleMonster interactions and expected results |
| TRX/NUnit/JUnit, summaries, uploads | PowerForge services/workflows | Artifact prefix and retention policy where supported |
| Website change detection, engine lock, SDK/social-card setup | Existing PowerForge website workflow owners | Website paths and the locked engine revision |
| Browser install, launch, HTTP readiness and teardown | HtmlTinkerX with PowerForge.Web orchestration | Browser choice and site output |
| Render-budget measurements/retries | HtmlTinkerX/PowerForge.Web extraction boundary | ConsoleMonster selectors, interactions, CDP metrics and budget thresholds |

The test workflows support the test-related rows. The browser-budget extraction
is a separate follow-up: it must preserve CDP layer/CPU/frame/timer
measurements, retries and owned server/browser teardown. It must not replace
those checks with a weaker page-load or screenshot test. The current website
verification and deployment callers stay unchanged.

The [proposed ConsoleMonster caller](Examples/ConsoleMonster.test.yml) shows the
follow-up shape, with [declarative project profiles](Examples/ConsoleMonster.test-ci.json).
These are examples for the later consumer switch, not changes to ConsoleMonster.
That follow-up also moves the existing inline export and terminal assertions into
the named project-owned probe script, preserving their behavior and failure
checks, and pins both shared workflows to the reviewed merged SHA.
