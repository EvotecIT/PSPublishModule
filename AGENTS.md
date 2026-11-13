# PSPublishModule

PowerShell module for building, testing, signing and publishing PowerShell
modules and .NET libraries, backed by PowerForge: a reusable C# engine with the
`powerforge` CLI, the PowerForge.Web static site engine (`powerforge-web`),
PowerShell compilation, backups and the PowerForge Studio desktop app.

## Layout

- `PSPublishModule/`: binary cmdlets (`Cmdlets/`) and thin host adapters
  (`Services/`). `Module/` holds the manifest, `.psm1`, Pester tests (`Tests/`),
  generated `Docs/` and `en-US/` help.
- `PowerForge/`: host-agnostic engine (models in `PowerForge/Models`).
  `PowerForge.PowerShell/` holds reusable logic that still needs PowerShell
  host concepts. `PowerForge.ModuleLoader/`, `PowerForge.Generators/` and
  `PowerForge.PowerShell.Provider*` support generated modules and providers.
- `PowerForge.Cli/`: `powerforge` tool. `PowerForge.ConsoleShared/`: shared Spectre.Console UI source.
- `PowerForge.Web/`, `PowerForge.Web.Cli/`, `PowerForge.Web.Protocols/`,
  `PowerForge.Blazor/`: site engine and `powerforge-web` CLI.
  `PowerForge.Web.Sample.App/` and `Samples/` are sample sites.
- `PowerForge.Backups*/`: database backup service and CLI.
- `PowerForgeStudio.*/`: Studio domain, orchestrator, CLI and WPF app (Windows only).
- Tests: `PowerForge.Tests/` (main xUnit suite), `PowerForge.Generators.Tests/`,
  `PowerForge.Web.Protocols.Tests/`, `PowerForge.Backups.Tests/`,
  `PowerForgeStudio.Tests/`, `PowerForgeStudio.Wpf.Tests/`,
  `PowerForge.Net472SmokeTests/`. `Tests/` holds sample projects that tests and
  workflows build.
- `Build/`: build, release and gate scripts (`release.json` is the release config).
  `powerforge.json` at the root is the module build config.
- `Benchmarks/`: benchmark suites and the PowerShell compiler corpus.
- `Docs/`: engine and module design docs. `Schemas/`: JSON schemas for
  PowerForge configs (see `JSON_SCHEMA.md`). `Website/`: site content for this repo.
- `.github/workflows/`: CI plus the reusable `powerforge-*` workflows other
  repositories call.

## Build and test

```powershell
dotnet build PSPublishModule.sln -c Release
dotnet build PSPublishModule.Core.slnf -c Release            # core projects only
dotnet test PowerForge.Tests/PowerForge.Tests.csproj -c Release --filter "FullyQualifiedName~ClassName"
dotnet test PSPublishModule.sln -c Release                    # everything (slow)
./Build/Build-Project.ps1 -ModuleOnly                         # build the module into Module/
./PSPublishModule.Tests.ps1                                   # Pester suite, needs the built module
./Build/Build-Project.ps1 -Plan                               # release plan without building
./Build/Test-ProductBuild.ps1                                 # packaged product smoke check
node ./Build/linecount.js . 800                               # file length check
```

- SDK from `global.json` (.NET 10); `net8.0` is also needed.
- Targets: `PowerForge`, `PowerForge.PowerShell` and `PSPublishModule` build
  `net472`, `net8.0` and `net10.0`. `PowerForge.Cli` and `PowerForge.Web`
  build `net8.0` and `net10.0`. Studio and Backups are `net10.0` (WPF is
  `net10.0-windows`). `PowerForge.Generators` is `netstandard2.0`.
- Run the Pester suite under both Windows PowerShell 5.1 and PowerShell 7.
- `TreatWarningsAsErrors` is on for every project.
- Release only: `./Build/Build-Project.ps1 -Publish` (or `-RunMode Publish`)
  signs and publishes the module, NuGet packages and tools.
  `Build-PowerForge.ps1`, `Build-PowerForgeWeb.ps1`, `Build-AllPackages.ps1` and
  `Publish-AllPackagesNuget.ps1` are release/packaging helpers.

## Generated files

- `Module/Docs/*.md` and `Module/en-US/PSPublishModule-help.xml` come from the
  cmdlet XML documentation in `PSPublishModule/Cmdlets`. Run
  `./Build/Build-Project.ps1 -ModuleOnly -RunMode Documentation` after changing
  cmdlet docs. Never edit the generated files by hand.
- `Module/Documentation/Templates/AGENTS.MD` is a template the module ships for
  other repositories, not this repo's agent guide. Leave it alone.

## Architecture

- Keep cmdlets thin: parameter binding, parameter sets, `ShouldProcess`,
  prompts, stream routing and mapping results to stable cmdlet-facing types.
- Put reusable logic in `PowerForge` (host-agnostic) or `PowerForge.PowerShell`
  (needs PowerShell host, module registration or manifest editing). Don't add
  business logic to `PSPublishModule/Cmdlets` that `PowerForge.Cli`, Studio,
  tests or another C# host could reuse.
- Before adding code to a cmdlet, ask: could a test, CLI or other host call it?
  Could two cmdlets share it? Does it touch files, versions, dependencies,
  repositories, GitHub, NuGet or build plans? If yes, it belongs in a shared layer.
- Keep host-specific rendering (`Host.UI.Write*`, Spectre.Console, `WriteObject`)
  in the cmdlet. Don't create abstractions just to move those calls out.
- If a public result type must stay stable, keep the internal model in the
  shared layer and map it back in `PSPublishModule`.
- Add a cmdlet by extending a typed service in `PowerForge`, then a thin cmdlet
  that calls it. Remove the legacy PowerShell function it replaces.
- Prefer typed models and enums. Keep `Hashtable`/`OrderedDictionary` for
  legacy adapters only. Don't enable `unsafe` code.
- Prefer engine fixes over per-site theme hacks when the issue can recur.

## Websites (PowerForge.Web)

Read `Docs/PowerForge.Web.Roadmap.md`, `Pipeline.md`, `QualityGates.md` and
`WebsiteStarter.md` (all `Docs/PowerForge.Web.*`) before engine changes.

- Each site has `site.json` with explicit `Features` and `pipeline.json` with a
  `verify-ci` step (`modes:["ci"]`, baseline, `failOnNewWarnings:true`), an
  `audit` or `doctor` step (baseline, `failOnNewIssues:true`) and a budget
  such as `maxTotalFiles`.
- Commit `.powerforge/verify-baseline.json` and `.powerforge/audit-baseline.json`.
  Ignore `_site/`, `_temp/` and `_reports/`.
- CI fails on new warnings and issues; dev mode warns and summarizes.
- Scriban themes use `pf.nav_links`, `pf.nav_actions` and `pf.menu_tree`,
  not `navigation.menus[0]`.
- From a site folder: `powerforge-web pipeline --config ./pipeline.json --mode ci`.
  Sibling website repos find this engine through `POWERFORGE_ROOT`.

## Conventions

- C#: file-scoped namespaces, XML docs on public types and cmdlet parameters,
  follow `.editorconfig`.
- PowerShell scripts and module code run on Windows PowerShell 5.1 and PowerShell 7.
- Keep files under about 800 lines; split large classes into partial files.
- Commit and PR titles are short imperative sentences: `Package every file the
  module manifest loads`.

## Gotchas

- Don't hand-edit fixtures: `PowerForge.Tests/Fixtures/`,
  `PowerForge.Tests/ApiDocsSourceFixtures/`, `Module/Tests/Fixtures/`, and the
  compiler corpus under `Benchmarks/PowerShellCompilation/Corpus/`. Corpus
  `*-baseline.*.json` files are committed census results; update them only by
  rerunning the corpus runner.
- Opt-in tests: `POWERFORGE_RUN_WIX_COMPILE_TESTS=1` (WiX compile),
  `POWERFORGE_RUN_MSI_INSTALL_TESTS=1` (installs an MSI), `POWERFORGE_TEST_LDAP_TARGET`
  and `POWERFORGE_TEST_CIM_TARGET` (live directory and CIM providers),
  `POWERFORGE_RUN_PRODUCT_SMOKE` (set by `Test-ProductBuild.ps1`).
  `POWERFORGE_PWSH76_PATH` points compiler oracle tests at a pinned PowerShell 7.6.
- Live Azure Artifacts gallery tests need `PSPUBLISHMODULE_AZDO_LIVE=1` and a real feed.
- Compiler gates (`Build/Invoke-PowerShellCompilerGate.ps1`,
  `Invoke-PowerShellWholeWorkflowGate.ps1`) are slow; CI runs them.
- Publishing signs with a certificate from the local store. Local builds
  (`-ModuleOnly` without `-Publish`) don't sign.
- Studio WPF projects and `net472` targets build only on Windows.

## Repository skills

In `.agents/skills`:

- `powerforge-module-builder`: build, validate, install and publish PowerShell modules.
- `powerforge-library-builder`: version, pack, sign and publish multi-project .NET libraries.
- `powerforge-docs-builder`: generated cmdlet docs, external help and about topics.
- `powerforge-website-starter`: create or standardize PowerForge.Web sites.
- `powerforge-homeassistant-release`: releases for Home Assistant integrations and HACS plugins.
