# PowerForge.Web Pipeline & Publish Specs (Draft)

This guide documents the JSON formats used by `powerforge-web pipeline` and
`powerforge-web publish`, with examples and field meanings.

## Pipeline spec (`powerforge-web pipeline`)

Pipeline specs execute a list of steps in order. Paths are resolved relative to
the pipeline JSON file location.

Schema:
- `Schemas/powerforge.web.pipelinespec.schema.json`

Minimal pipeline:
```json
{
  "$schema": "./Schemas/powerforge.web.pipelinespec.schema.json",
  "steps": [
    {
      "task": "build",
      "config": "./site.json",
      "out": "./Artifacts/site"
    }
  ]
}
```

### Pipeline inheritance (`extends`)

Pipeline configs can inherit from one or more base files:

- `extends` (or `Extends`) accepts a string or array of strings.
- Base configs are loaded in order.
- Object values merge recursively.
- `steps` arrays are appended (`base` steps first, then child steps).

Example:
```json
{
  "$schema": "./Schemas/powerforge.web.pipelinespec.schema.json",
  "extends": "./config/presets/pipeline.web-quality.json"
}
```

With local additions:
```json
{
  "$schema": "./Schemas/powerforge.web.pipelinespec.schema.json",
  "extends": "./config/presets/pipeline.web-quality.json",
  "steps": [
    { "task": "indexnow", "modes": ["ci"], "baseUrl": "https://example.com", "keyEnv": "INDEXNOW_KEY", "sitemap": "./_site/sitemap.xml" }
  ]
}
```

Notes:
- Inheritance loops are detected and fail fast.
- Relative paths inside merged steps resolve from the root pipeline file you execute.

### CLI flags

`powerforge-web pipeline` supports a few command-line flags to speed up local iteration:

- `--fast`: applies safe performance-focused overrides (for example, scopes optimize/audit when possible and disables expensive rendered checks).
- `--dev`: implies `--fast`, sets pipeline mode to `dev`, and skips `optimize` + `audit` unless you explicitly include them via `--only`.
- `--mode <name>`: sets a pipeline mode label used for step filtering (see "Step modes" below).
- `--only <task[,task...]>`: run only the specified tasks.
- `--skip <task[,task...]>`: skip the specified tasks.
- `--watch`: rerun the pipeline when files change (watches the pipeline folder, ignores output folders).

### Security model

Pipeline specs are treated as trusted build code.

- Only run `pipeline.json` files from repositories/branches you trust.
- Steps such as `hook`, `exec`, `html-transform`, and `data-transform` execute external commands and can run arbitrary code on the build agent.
- Never store long-lived secrets directly in pipeline JSON; prefer environment-variable indirection (`tokenEnv`) and CI secret stores.
- `git-sync` emits `[PFWEB.GITSYNC.SECURITY]` when an inline `token` is detected to help prevent accidental secret commits.

### Step modes

Pipelines can gate individual steps by mode to keep local iteration fast while keeping CI exhaustive.

Behavior:
- If `--mode` is not specified, the effective mode is `default`.
- If a step does not specify any mode constraints, it runs in all modes.
- `mode`/`modes` restrict when a step runs.
- `skipModes` disables a step for selected modes.

Step fields:
- `mode`: single mode name (string).
- `modes` (or `onlyModes`): allowed modes (array of strings).
- `skipModes`: disallowed modes (array of strings).

Example: skip heavy steps in `dev`, keep them in `ci`/`default`.
```json
{
  "steps": [
    { "task": "build", "config": "./site.json", "out": "./_site" },
    { "task": "verify", "config": "./site.json" },
    { "task": "optimize", "siteRoot": "./_site", "skipModes": ["dev"] },
    { "task": "audit", "siteRoot": "./_site", "skipModes": ["dev"] }
  ]
}
```

### Supported steps

#### build
Builds markdown + theme into static HTML.
```json
{ "task": "build", "config": "./site.json", "out": "./Artifacts/site", "clean": true }
```
Notes:
- `clean: true` clears the output directory before building (avoids stale files).

#### nav-export
Exports a deterministic `site-nav.json` payload (including `surfaces` + `profiles`) from `site.json` + discovered content, without building HTML output.
```json
{ "task": "nav-export", "config": "./site.json", "out": "./static/data/site-nav.json", "overwrite": false }
```
Notes:
- If `out` is omitted, the default output is `static/<dataRoot>/site-nav.json` under the site root (absolute `dataRoot` values fall back to `static/data/site-nav.json`).
- Export contract is versioned and stable for downstream tools:
  - `schemaVersion: 2`
  - `format: "powerforge.site-nav"`
  - canonical surface keys (`main`, `docs`, `apidocs`, `products`) plus `surfaceAliases` (for example `api -> apidocs`)
- Safe overwrite behavior:
  - By default, `nav-export` will only overwrite an existing file when it is marked `"generated": true`.
  - Set `overwrite: true` to force overwrite of a user-managed file.

Force overwrite example:
```json
{ "task": "nav-export", "config": "./site.json", "out": "./static/data/site-nav.json", "overwrite": true }
```

#### verify
Validates content + routing consistency from the site config.
```json
{
  "task": "verify",
  "config": "./site.json",
  "failOnWarnings": true,
  "failOnNavLint": true,
  "failOnThemeContract": true,
  "baseline": "./.powerforge/verify-baseline.json",
  "failOnNewWarnings": true
}
```
Notes:
- Emits warnings for missing titles, duplicate routes, missing assets, and TOC coverage.
- By default fails only when errors are found.
- `failOnWarnings`, `failOnNavLint`, and `failOnThemeContract` can enforce stricter quality gates.
- `suppressWarnings` (array of strings) filters warnings before printing and before policy evaluation (use codes like `PFWEB.NAV.LINT` or `re:...`).
- Baselines:
  - `baseline`: path to a baseline file (must resolve under the site root)
  - `baselineGenerate`: write a baseline from current warnings
  - `baselineUpdate`: merge current warnings into an existing baseline
  - `failOnNewWarnings`: fail only when warnings not present in baseline are produced (recommended for CI)
- Failure previews (pipeline output):
  - `warningPreviewCount`: number of warnings included in the thrown failure summary
  - `errorPreviewCount`: number of errors included in the thrown failure summary

#### markdown-fix
Normalizes markdown hygiene issues in content folders (dry-run by default).
```json
{
  "task": "markdown-fix",
  "config": "./site.json",
  "include": "blog/**/*.md",
  "exclude": "**/archive/**",
  "apply": false,
  "failOnChanges": true,
  "reportPath": "./_reports/markdown-fix.json",
  "summaryPath": "./_reports/markdown-fix.md"
}
```
Notes:
- Fixes run against markdown files (`*.md`) under `root`/`path`/`siteRoot`, or from `config` (content root).
- `apply: false` is a dry-run (CI-friendly); `apply: true` writes changes.
- `failOnChanges: true` fails the step when dry-run detects files that need fixes.
- Reports:
  - `reportPath`: writes structured JSON output
  - `summaryPath`: writes markdown summary with totals and per-file breakdown
- Current normalizations include:
  - multiline media tag normalization outside code fences (`img`, `iframe`, `video`, `audio`, `source`, `picture`)
  - simple HTML-to-markdown replacements (`h1..h6`, `strong/b`, `em/i`, `p`, `br`)

#### doctor
Runs build/verify/audit as one health-check step.
```json
{
  "task": "doctor",
  "config": "./site.json",
  "out": "./Artifacts/site",
  "build": true,
  "verify": true,
  "audit": true,
  "failOnWarnings": true,
  "failOnNavLint": true,
  "failOnThemeContract": true,
  "summary": true,
  "sarif": true
}
```
Notes:
- Supports the same strict verify flags as `verify`.
- Supports `suppressWarnings` to filter verify warnings before policy evaluation.
- Supports `suppressIssues` to filter audit issues before counts/gates and before writing summary/SARIF.
- Supports verify baselines (prefix `verify*` to avoid confusion with audit baselines):
  - `verifyBaseline`: path to a verify baseline file
  - `verifyBaselineGenerate` / `verifyBaselineUpdate`
  - `verifyFailOnNewWarnings`: fail only when verify emits new warnings vs baseline
- Supports audit controls (`requiredRoutes`, `navRequiredLinks`, `checkHeadingOrder`, `checkSeoMeta`, `checkLinkPurpose`, etc.).

#### apidocs
Generates API reference output from XML docs (optionally enriched by assembly).
```json
{
  "task": "apidocs",
  "config": "./site.json",
  "xml": "./Artifacts/generated/MyLib.xml",
  "assembly": "./Artifacts/generated/MyLib.dll",
  "out": "./Artifacts/site/api",
  "title": "API Reference",
  "baseUrl": "/api",
  "format": "json",
  "includeNamespace": "MyLib",
  "excludeType": "MyLib.InternalHelper,MyLib.Internal*"
}
```
Notes:
- `format`: `json`, `html`, `hybrid`, or `both` (json + html)
- `title`: used for both `<title>` and the visible API overview `<h1>` (important for multi-API sites)
- `inputs` (alias: `entries`) can run multiple API docs inputs in one step. Parent settings act as defaults.
- HTML mode can include `headerHtml` + `footerHtml` fragments
- Critical CSS (optional):
  - `injectCriticalCss: true` inlines `assetRegistry.criticalCss` from `site.json` into API pages (requires `config`)
  - `criticalCssPath` (or `criticalCss`) inlines a single CSS file into API pages
- `config` (recommended) enables best-practice defaults.
  - If `config` is omitted, the pipeline will use `./site.json` when it exists at the pipeline root.
  - if `nav` is not set, it prefers `static/<dataRoot>/site-nav.json` (when present), otherwise falls back to `config`
  - if `headerHtml`/`footerHtml` are not set, the engine will try to use `themes/<defaultTheme>/partials/api-header.html` + `api-footer.html` (when present), otherwise falls back to `header.html` + `footer.html`
- `template`: `simple` (default) or `docs` (sidebar layout)
- `type`: `CSharp` (default) or `PowerShell` (uses PowerShell help XML)
- `templateRoot` lets you override built-in templates/assets by placing files like
  `index.html`, `type.html`, `docs-index.html`, `docs-type.html`, `docs.js`,
  `search.js`, or `fallback.css` in that folder
- `templateIndex`, `templateType`, `templateDocsIndex`, `templateDocsType` let you
  override a single template file without a template root
  - `docsScript` / `searchScript` let you override the embedded JS files
  - `docsHome` / `docsHomeUrl` override the "Back to Docs" link in the sidebar (default `/docs/`)
  - `sidebar` (`left` or `right`) controls the docs sidebar position (`template: docs`)
  - `bodyClass` sets the `<body>` class on API docs pages (default `pf-api-docs`)
  - social preview controls:
    - `socialImage` / `socialTwitterCard` set default OG/Twitter image and card type
    - `socialImageWidth` / `socialImageHeight` set OG image dimensions for static social images
    - `socialTwitterSite` / `socialTwitterCreator` set Twitter account handles for cards
    - `socialAutoGenerate: true` enables per-page API social card PNG generation
    - `socialCardPath` sets output URL prefix (default `/assets/social/generated/api`)
    - `socialCardWidth` / `socialCardHeight` control generated card dimensions (default `1200x630`)
  - `sourceRoot` / `sourceUrl` enable source links in the API docs (requires PDB)
  - `sourcePathPrefix` prepends a stable prefix to resolved source paths before URL token expansion (useful for mixed-repo or nested-source layouts)
- `includeUndocumented` (default `true`) adds public types/members missing from XML docs
- `nav`: path to `site.json` or `site-nav.json` to inject navigation tokens into header/footer
- `navSurface`:
  - optional explicit surface name from `site-nav.json.surfaces` (for example `apidocs`, `docs`, `main`)
  - when set, API docs navigation tokens are sourced from that surface
- `navContextPath` / `navContextCollection` / `navContextLayout` / `navContextProject`:
  - optional context used to select `Navigation.Profiles` when injecting nav tokens
  - default behavior: profile selection uses the site root (`/`) unless you set `navContextPath` explicitly
  - if you want API pages to use an `/api/` profile override, set `navContextPath: "/api/"` on the apidocs step
- `failOnWarnings`: fail the pipeline step when API docs emits warnings
  - default: `true` in CI (when `CI=true`) unless running `mode: dev` / `--fast`
- `suppressWarnings`: array of warning suppressions (same matching rules as `verify`)
  - useful codes: `[PFWEB.APIDOCS.CSS.CONTRACT]`, `[PFWEB.APIDOCS.NAV.FALLBACK]`, `[PFWEB.APIDOCS.INPUT.*]`, `[PFWEB.APIDOCS.SOURCE]`, `[PFWEB.APIDOCS.XREF]`, `[PFWEB.APIDOCS.DISPLAY]`, `[PFWEB.APIDOCS.MEMBER.SIGNATURES]`
- If `nav` is provided but your custom `headerHtml`/`footerHtml` fragments do not contain `{{NAV_LINKS}}` / `{{NAV_ACTIONS}}`, the generator emits `[PFWEB.APIDOCS.NAV]` warnings.
- Source-link diagnostics emit `[PFWEB.APIDOCS.SOURCE]` warnings for mapping issues (for example unmatched `sourceUrlMappings.pathPrefix` or likely duplicated GitHub path prefixes causing 404 source/edit links).
- Source URL templates are validated preflight:
  - require at least one path token (`{path}`, `{pathNoRoot}`, `{pathNoPrefix}`)
  - warn on unsupported tokens (supported: `{path}`, `{line}`, `{root}`, `{pathNoRoot}`, `{pathNoPrefix}`)
- Additional `apidocs` preflight checks emit warning codes before generation starts:
  - `[PFWEB.APIDOCS.SOURCE]` for source-link config issues (for example `sourceUrlMappings` configured without `sourceRoot`/`sourceUrl`, missing `sourceRoot` directory, duplicate mapping prefixes)
  - `[PFWEB.APIDOCS.NAV]` for nav config issues (for example `navSurface` configured without `nav`)
  - `[PFWEB.APIDOCS.POWERSHELL]` for missing PowerShell examples paths when `psExamplesPath` is set
- `warningPreviewCount`: how many warnings to print to console (default `2` in dev, `5` otherwise)
- `includeNamespace` / `excludeNamespace` are comma-separated namespace prefixes (pipeline only)
- `includeType` / `excludeType` accept comma-separated full type names (supports `*` suffix for prefix match)
- `quickStartTypes` (aliases: `quickstartTypes`, `quick-start-types`) accepts comma-separated simple type names for the "Quick Start" and "Main API" sections
- `displayNameMode` (alias: `display-name-mode`) controls generated type labels:
  - `short` keeps short names only
  - `namespace-suffix` (default) disambiguates duplicate short names
  - `full` uses full type names in docs and JSON search/index artifacts
- API coverage reports and gates:
  - `coverageReport`: write coverage JSON (default: `coverage.json` under apidocs output)
  - `generateCoverageReport`: enable/disable coverage report generation (default: `true`)
  - `xrefMap`: write API xref map JSON (default: `xrefmap.json` under apidocs output)
  - `generateXrefMap`: enable/disable xref map generation (default: `true`)
  - `generateMemberXrefs`: include member/parameter xref entries in the map (default: `true`)
  - `memberXrefKinds`: optional member-kind filter (`constructors,methods,properties,fields,events,extensions,parameters`)
  - `memberXrefMaxPerType`: optional cap for member xref entries per type/command (`0` = unlimited)
  - coverage thresholds (0-100): `minTypeSummaryPercent`, `minTypeRemarksPercent`, `minTypeCodeExamplesPercent`, `minMemberSummaryPercent`, `minMemberCodeExamplesPercent`, `minPowerShellSummaryPercent`, `minPowerShellRemarksPercent`, `minPowerShellCodeExamplesPercent`, `minPowerShellParameterSummaryPercent`
  - source coverage thresholds (0-100): `minTypeSourcePathPercent`, `minTypeSourceUrlPercent`, `minMemberSourcePathPercent`, `minMemberSourceUrlPercent`, `minPowerShellSourcePathPercent`, `minPowerShellSourceUrlPercent`
  - source quality max-count thresholds (>=0): `maxTypeSourceInvalidUrlCount`, `maxMemberSourceInvalidUrlCount`, `maxPowerShellSourceInvalidUrlCount`, `maxTypeSourceUnresolvedTemplateCount`, `maxMemberSourceUnresolvedTemplateCount`, `maxPowerShellSourceUnresolvedTemplateCount`, `maxTypeSourceRepoMismatchHints`, `maxMemberSourceRepoMismatchHints`, `maxPowerShellSourceRepoMismatchHints`
  - `failOnCoverage`: fail step when thresholds are below minimums (default: `true` when any threshold is configured)
  - `coveragePreviewCount`: max failed coverage metrics shown in logs
  - PowerShell-only example inputs: `psExamplesPath`, `generatePowerShellFallbackExamples`, `powerShellFallbackExampleLimit`

Multi-library batch example:
```json
{
  "task": "apidocs",
  "config": "./site.json",
  "format": "both",
  "template": "docs",
  "css": "/css/app.css,/css/api.css",
  "inputs": [
    {
      "id": "core-csharp",
      "type": "CSharp",
      "xml": "./Artifacts/core/Core.xml",
      "assembly": "./Artifacts/core/Core.dll",
      "out": "./_site/api/core",
      "title": "Core API",
      "baseUrl": "/api/core"
    },
    {
      "id": "module-powershell",
      "type": "PowerShell",
      "helpPath": "./Module/en-US/Module-help.xml",
      "out": "./_site/api/powershell",
      "title": "PowerShell Cmdlets",
      "baseUrl": "/api/powershell"
    }
  ]
}
```

##### Template overrides
You can fully control the API docs layout by providing a template root or per-file overrides.
Starter templates live in:
- `Assets/ApiDocs/Templates/default` (matches the embedded defaults)
- `Assets/ApiDocs/Templates/sidebar-right` (example that moves the sidebar)
For CSS hooks and JS expectations, see `Docs/PowerForge.Web.ApiDocs.md`.

Recommended usage:
```json
{
  "task": "apidocs",
  "type": "CSharp",
  "xml": "./Artifacts/MyLib.xml",
  "assembly": "./Artifacts/MyLib.dll",
  "out": "./Artifacts/site/api",
  "format": "both",
  "template": "docs",
  "templateRoot": "./Assets/ApiDocs/Templates/default",
  "css": "/css/api-docs.css",
  "headerHtml": "./themes/nova/partials/api-header.html",
  "footerHtml": "./themes/nova/partials/api-footer.html",
  "nav": "./site.json"
}
```

PowerShell help usage:
```json
{
  "task": "apidocs",
  "type": "PowerShell",
  "help": "./Module/en-US/MyModule-help.xml",
  "out": "./Artifacts/site/api",
  "format": "both",
  "template": "docs",
  "css": "/css/api-docs.css"
}
```
Notes:
- If `help` points to a folder, the first `*-help.xml` file is used.
- For deterministic output, point to a specific help XML file.

##### Template tokens
Common tokens (all templates):
- `{{CSS}}` – stylesheet link or inline fallback CSS
- `{{CRITICAL_CSS}}` – optional critical CSS HTML injected into `<head>` (typically `<style>...</style>`)
- `{{HEADER}}` / `{{FOOTER}}` – injected header/footer fragments (optional)

Simple templates:
- `index.html`: `{{TITLE}}`, `{{TYPE_COUNT}}`, `{{TYPE_LINKS}}`, `{{SEARCH_SCRIPT}}`
- `type.html`: `{{TYPE_TITLE}}`, `{{TYPE_FULLNAME}}`, `{{TYPE_SUMMARY}}`, `{{TYPE_REMARKS}}`, `{{MEMBERS}}`

Docs templates (`template: docs`):
  - `docs-index.html`: `{{TITLE}}`, `{{SIDEBAR}}`, `{{SIDEBAR_CLASS}}`, `{{MAIN}}`, `{{DOCS_SCRIPT}}`
  - `docs-type.html`: `{{TITLE}}`, `{{SIDEBAR}}`, `{{SIDEBAR_CLASS}}`, `{{MAIN}}`, `{{DOCS_SCRIPT}}`

Header/footer fragments can use nav tokens when `nav` is provided:
- `{{SITE_NAME}}`, `{{BRAND_NAME}}`, `{{BRAND_URL}}`, `{{BRAND_ICON}}`
- `{{NAV_LINKS}}`, `{{NAV_ACTIONS}}`
- `{{FOOTER_PRODUCT}}`, `{{FOOTER_RESOURCES}}`, `{{FOOTER_COMPANY}}`
- `{{YEAR}}`

Project.json example (metadata you can reuse across repos):
```json
{
  "ApiDocs": {
    "Type": "CSharp",
    "AssemblyPath": "artifacts/MyLib.dll",
    "XmlDocPath": "artifacts/MyLib.xml",
    "OutputPath": "api"
  }
}
```
Notes:
- `ApiDocs` in `project.json` is **metadata**; today you still add the `apidocs` pipeline step.
- See `Samples/PowerForge.Web.Sample/projects/ApiDocsDemo/project.json` for a full example.

#### xref-merge
Merges multiple xref maps into one shared map (useful when combining C# API docs, PowerShell API docs, and site pages).
```json
{
  "task": "xref-merge",
  "out": "./Artifacts/site/data/xrefmap.json",
  "mapFiles": [
    "./Artifacts/site/api/xrefmap.json",
    "./Artifacts/site/powershell/xrefmap.json",
    "./xref/site-xref-overrides.json"
  ],
  "preferLast": true,
  "maxReferences": 80000,
  "maxDuplicates": 200,
  "maxReferenceGrowthCount": 15000,
  "maxReferenceGrowthPercent": 35
}
```
Notes:
- Inputs can be files or directories.
- Directory inputs are scanned using `pattern` (default `*.json`) and `recursive` (default `true`).
- Duplicate UIDs:
  - merged by UID
  - aliases are unioned
  - `preferLast:true` lets later files override `href`/`name`
  - `failOnDuplicates:true` fails immediately when duplicates are detected
- Growth guardrails:
  - `maxReferences` warns when merged reference count exceeds the configured threshold (`0` disables)
  - `maxDuplicates` warns when duplicate UID count exceeds the configured threshold (`0` disables)
  - `maxReferenceGrowthCount` warns when merged reference growth exceeds an absolute delta versus previous output (`0` disables)
  - `maxReferenceGrowthPercent` warns when merged reference growth exceeds a percent delta versus previous output (`0` disables)
- Warning policy:
  - `failOnWarnings` defaults to `true` in CI and `false` in dev (same behavior as `apidocs`)
  - `warningPreviewCount` controls how many warnings are shown in logs

#### changelog
Generates a `data/changelog.json` file from a local `CHANGELOG.md` or GitHub releases.
```json
{
  "task": "changelog",
  "source": "auto",
  "changelog": "./CHANGELOG.md",
  "repo": "EvotecIT/IntelligenceX",
  "out": "./Artifacts/site/data/changelog.json"
}
```
Notes:
- `source`: `auto` (default), `file`, or `github`
- Use `repo` (`owner/name`) or `repoUrl` for GitHub releases.
- Use `max` to limit number of releases.
- The generator emits `body_md`; the build converts it to `body` HTML automatically.

Usage scenarios:

Local changelog only:
```json
{
  "task": "changelog",
  "source": "file",
  "changelog": "./CHANGELOG.md",
  "out": "./Artifacts/site/data/changelog.json"
}
```

GitHub releases only:
```json
{
  "task": "changelog",
  "source": "github",
  "repo": "EvotecIT/IntelligenceX",
  "token": "%GITHUB_TOKEN%",
  "max": 20,
  "out": "./Artifacts/site/data/changelog.json"
}
```

Template usage (Scriban):
```scriban
{{ for item in data.changelog.items }}
<article class="release">
  <h2>{{ item.title }}</h2>
  {{ if item.publishedAt }}<div class="release-date">{{ item.publishedAt }}</div>{{ end }}
  <div class="release-body">{{ item.body }}</div>
</article>
{{ end }}
```

#### version-hub
Generates version-switcher metadata for multi-version docs sites.
```json
{
  "task": "version-hub",
  "title": "CodeGlyphX Versions",
  "discoverRoot": "./versions",
  "discoverPattern": "v*",
  "basePath": "/docs/",
  "setLatestFromNewest": true,
  "out": "./data/version-hub.json"
}
```

Notes:
- Use explicit `versions`/`entries` arrays when you want full control over labels/channels/flags.
- Use `discoverRoot` to auto-discover folder-based versions (for example `v1.0`, `v2.0`, `v3.0-preview1`).
- Output includes:
  - `latestPath` and `ltsPath` for theme switchers/canonical helpers
  - ordered `versions` list with `latest`, `lts`, `deprecated`, and optional `aliases`
- Best practice: run this before `build` so templates can consume `data/version-hub.json`.
- To wire it into `site.json` versioning without duplicating entries, set `Versioning.HubPath` to the generated file and leave `Versioning.Versions` empty.

#### package-hub
Generates a unified package/module metadata JSON file from `.csproj` and `.psd1` inputs.
```json
{
  "task": "package-hub",
  "title": "IntelligenceX Package Hub",
  "projectFiles": [
    "../IntelligenceX/IntelligenceX.csproj",
    "../IntelligenceX.Cli/IntelligenceX.Cli.csproj"
  ],
  "moduleFiles": [
    "../Module/IntelligenceX.psd1"
  ],
  "out": "./data/package-hub.json"
}
```
Notes:
- Supports `project` / `projects` / `projectFiles` (`project-files`) for `.csproj` inputs.
- Supports `module` / `modules` / `moduleFiles` (`module-files`) for `.psd1` inputs.
- Output includes:
  - libraries: package id/version, target frameworks, and package references
  - modules: module version, PowerShell compatibility, exported commands, required modules
  - warnings: missing files or parse issues
- PowerShell `RequiredModules` supports both string entries and hashtable entries (`ModuleName` + version keys).
- Best used before `build` so templates can consume `data/package-hub.json`.

#### llms
Generates `llms.txt`, `llms.json`, and `llms-full.txt`.
```json
{
  "task": "llms",
  "siteRoot": "./Artifacts/site",
  "project": "./src/MyLib/MyLib.csproj",
  "apiIndex": "./Artifacts/site/api/index.json",
  "apiBase": "/api",
  "name": "MyLib",
  "packageId": "MyLib",
  "version": "1.2.3",
  "apiLevel": "Summary",
  "apiMaxTypes": 200,
  "apiMaxMembers": 1500
}
```
Notes:
- `apiLevel`: `None` (default), `Summary`, or `Full`
- `apiMaxTypes` / `apiMaxMembers` cap the size of API detail sections in `llms-full.txt`

#### compat-matrix
Generates compatibility matrix data for C# libraries and PowerShell modules.
```json
{
  "task": "compat-matrix",
  "title": "CodeGlyphX Compatibility",
  "csprojFiles": ["./src/CodeGlyphX/CodeGlyphX.csproj"],
  "psd1Files": ["./Module/CodeGlyphX.psd1"],
  "entries": [
    {
      "type": "nuget",
      "id": "CodeGlyphX.Extensions",
      "version": "1.2.0-preview.1",
      "targetFrameworks": ["net8.0", "net10.0"],
      "status": "preview"
    }
  ],
  "out": "./data/compat-matrix.json",
  "markdownOut": "./content/docs/compatibility.md"
}
```
Notes:
- `csproj` / `csprojFiles` discover package id, version, TFMs, and package dependencies from project files.
- `psd1` / `psd1Files` discover module version, PowerShell version/editions, and required modules.
- explicit `entries` are merged with discovered rows (explicit values win when duplicated by type+id).
- Use `includeDependencies:false` to omit dependency columns from generated outputs.

#### sitemap
Generates `sitemap.xml` and (optionally) JSON/HTML outputs, `sitemap-news.xml`, and a sitemap index.
```json
{
  "task": "sitemap",
  "siteRoot": "./Artifacts/site",
  "baseUrl": "https://example.com",
  "includeLanguageAlternates": true,
  "entriesJson": "./data/sitemap.entries.json",
  "newsOut": "./Artifacts/site/sitemap-news.xml",
  "newsPaths": ["/news/**", "/blog/**"],
  "newsMetadata": {
    "publicationName": "Example Product",
    "publicationLanguage": "en"
  },
  "imageOut": "./Artifacts/site/sitemap-images.xml",
  "imagePaths": ["/blog/**", "/news/**", "/docs/**"],
  "videoOut": "./Artifacts/site/sitemap-videos.xml",
  "videoPaths": ["/showcase/**", "/news/**"],
  "sitemapIndex": "./Artifacts/site/sitemap-index.xml",
  "extraPaths": ["/robots.txt"],
  "json": true,
  "jsonOut": "./Artifacts/site/sitemap/index.json",
  "html": true,
  "htmlTemplate": "./themes/nova/templates/sitemap.html",
  "htmlTitle": "Sitemap",
  "htmlCss": "/themes/nova/assets/app.css",
  "entries": [
    { "path": "/docs/", "changefreq": "weekly", "priority": "0.8" }
  ]
}
```
Notes:
- By default, HTML discovery excludes utility pages (`*.scripts.html`, `*.head.html`, `api-fragments/**`) and pages with robots `noindex`.
- `siteRoot` and `baseUrl` can be inferred from `config` (`site.json`) plus a prior `build` step output, so you can avoid duplicating values.
- `entries` only override metadata (priority/changefreq/lastmod) for specific paths.
- `entriesJson` can load entries from a file (`[{...}]` or `{ "entries":[...] }`) so sitemap HTML can be driven from JSON content.
- `newsOut` / `newsPaths` generate a Google News sitemap from matching routes. If omitted, defaults target `**/news/**`.
- `newsMetadata` sets publication metadata for the news sitemap (`publicationName`, `publicationLanguage`, `genres`, `access`, `keywords`).
- `imageOut` / `imagePaths` generate an image sitemap from matching routes that contain discovered image URLs.
- `videoOut` / `videoPaths` generate a video sitemap from matching routes that contain discovered video URLs.
- `sitemapIndex` emits a sitemap index file that references generated XML sitemap outputs.
- image/video URL discovery is automatic for rendered HTML (`<img src>`, `<video src>`, `<source src>`, `<iframe src>`), and can also be provided via `entries[].images` / `entries[].videos`.
- `json`/`jsonOut` emit a machine-readable sitemap payload with resolved URLs and metadata.
- Set `includeHtmlFiles: false` for a strict/manual sitemap.
- Set `includeNoIndexHtml: true` to include noindex pages anyway.
- Set `noDefaultExclude: true` to include utility pages (or pass `excludePatterns` for custom exclusions).
- `includeNoIndexPages` defaults to `false`; enable it to include explicit noindex entries from sitemap metadata payloads.
- `includeLanguageAlternates` (default `true`) emits `xhtml:link` alternates (`hreflang` and `x-default`) when `_powerforge/site-spec.json` contains an enabled `Localization` config.
- When `htmlCss` is omitted, PowerForge tries to auto-detect site/theme CSS so the HTML sitemap inherits theme styling.

#### optimize
Applies critical CSS, minifies HTML/CSS/JS, optimizes images, and can hash assets + generate cache headers.
```json
{
  "task": "optimize",
  "siteRoot": "./Artifacts/site",
  "config": "./site.json",
  "criticalCss": "./themes/codeglyphx/critical.css",
  "cssPattern": "(app|api-docs)\\.css",
  "minifyHtml": true,
  "minifyCss": true,
  "minifyJs": true,
  "optimizeImages": true,
  "imageExtensions": [".png", ".jpg", ".jpeg", ".webp"],
  "imageQuality": 82,
  "imageGenerateWebp": true,
  "imagePreferNextGen": true,
  "imageWidths": [480, 960, 1440],
  "imageEnhanceTags": true,
  "imageMaxTotalBytes": 50000000,
  "imageFailOnBudget": true,
  "hashAssets": true,
  "hashExtensions": [".css", ".js"],
  "hashExclude": ["**/nohash/**"],
  "cacheHeaders": true
}
```
Notes:
- `config` loads `AssetPolicy` from `site.json` (rewrites, hashing defaults, cache headers).
- `hashAssets` fingerprints files and rewrites references (HTML + CSS).
- `cacheHeaders` writes `_headers` with cache-control rules (Netlify/Cloudflare Pages compatible).
- `imageGenerateWebp` / `imageGenerateAvif` can create next-gen variants when they are smaller than source output.
- `imagePreferNextGen` rewrites `<img src>` to next-gen output when available.
- `imageWidths` generates responsive variants and `srcset` entries.
- `imageEnhanceTags` injects `loading=\"lazy\"`, `decoding=\"async\"`, and intrinsic `width`/`height` (when known) on rewritten image tags.
- Markdown rendering also injects default `loading=\"lazy\"` and `decoding=\"async\"` on `<img>` tags when missing, so markdown image syntax (`![](...)`) gets sane defaults even before optimize runs. Control this via `site.json -> Markdown` (`AutoImageHints`, `DefaultImageLoading`, `DefaultImageDecoding`).
- `imageMaxBytesPerFile` / `imageMaxTotalBytes` define budgets; `imageFailOnBudget` fails the step if budgets are exceeded.
- `scopeFromBuildUpdated`: when enabled, and `htmlInclude` is not set, limits HTML processing to the HTML files updated by the most recent `build` step (when `siteRoot` matches build `out`). In `powerforge-web pipeline --fast` this is enabled by default; set to `false` to force full-site optimize even in fast mode.

#### audit
Runs static (and optional rendered) checks against generated HTML.
```json
{
  "task": "audit",
  "siteRoot": "./Artifacts/site",
  "checkLinks": true,
  "checkAssets": true,
  "checkNav": true,
  "rendered": true,
  "renderedMaxPages": 10,
  "renderedInclude": "index.html,docs/**,benchmarks/**",
  "renderedExclude": "api/**,docs/api/**",
  "summary": true
}
```
Notes:
- Static checks run by default; set `rendered: true` to enable Playwright checks.
- `renderedInclude` / `renderedExclude` are comma-separated glob patterns (paths are relative to `siteRoot`).
- `summary: true` writes `audit-summary.json` under `siteRoot` unless `summaryPath` is provided.
- `maxTotalFiles` can be used as a guardrail to keep site outputs from silently ballooning (for example, too many generated assets).
  - Use `budgetExclude` (comma-separated globs) to exclude folders like `api/**` from the file-count budget without excluding them from the HTML audit scope.
- `suppressIssues` (array of strings) filters audit issues before counts/gates and before printing/writing artifacts (use codes like `PFAUDIT.BUDGET` or `re:...`).
- Use `failOnIssueCodes` (comma-separated) for targeted hard gates on specific findings without failing an entire category (for example `media-img-dimensions,heading-order,head-render-blocking`).
- Use `noDefaultIgnoreNav` to disable the built-in API docs nav ignore list.
- Use `ignoreMedia` (comma-separated globs) to relax media checks for selected paths.
- Use `noDefaultIgnoreMedia` to disable the built-in API docs media-ignore list (`api/**`, `docs/api/**`, `api-docs/**`).
- Use `mediaProfiles` (`.json`) for path-scoped media policy overrides (for example allowing standard YouTube host on selected sections or tightening eager-image limits).
- Match precedence for `mediaProfiles`: longest `match` pattern wins.
- Use `navRequired: false` (or `navOptional: true`) if some pages intentionally omit a nav element.
- Use `navIgnorePrefixes` to skip nav checks for path prefixes (comma-separated, e.g. `api/,docs/api/`).
- `checkMediaEmbeds` (alias `checkMedia`) validates media/embed hygiene for page speed and UX:
  - iframe checks: `loading="lazy"`, `title`, external `referrerpolicy`, YouTube nocookie host hint
  - image checks: loading/decoding hints, intrinsic size/aspect-ratio hints, `srcset` + `sizes` pairing
  - rendered-content check: flags escaped media HTML tags (hint `media-escaped-html-tag`) when `<img>/<iframe>/...` appears as literal text outside code blocks
- `checkSeoMeta` validates canonical/OpenGraph/Twitter metadata consistency:
  - duplicate canonical, OG, and Twitter tags
  - absolute URL checks for canonical, `og:url`, `og:image`, `twitter:url`, `twitter:image`
  - missing `og:image` detection
  - skipped for pages marked with robots `noindex`
- Use `noDefaultExclude` to include partial HTML files like `*.scripts.html`.
- `renderedBaseUrl` lets you run rendered checks against a running server (otherwise a local server is started).
- `renderedServe`, `renderedHost`, `renderedPort` control the temporary local server used for rendered checks.
- `renderedEnsureInstalled` auto-installs Playwright browsers before rendered checks (defaults to `true` in CLI/pipeline when `rendered` is enabled).
- `scopeFromBuildUpdated`: when enabled, and `include` is not set, limits the audit to the HTML files updated by the most recent `build` step (when `siteRoot` matches build `out`). In `powerforge-web pipeline --fast` this is enabled by default; set to `false` to force full-site audit even in fast mode.
CLI note:
- Use `--rendered-no-install` to skip auto-install (for CI environments with preinstalled browsers).

Recommended `mediaProfiles` file (copy/paste starter):
```json
[
  {
    "match": "api/**",
    "ignore": true
  },
  {
    "match": "docs/**",
    "requireIframeLazy": true,
    "requireIframeTitle": true,
    "requireIframeReferrerPolicy": true,
    "requireImageLoadingHint": true,
    "requireImageDecodingHint": true,
    "requireImageDimensions": true,
    "requireImageSrcSetSizes": true,
    "maxEagerImages": 1
  },
  {
    "match": "showcase/**",
    "allowYoutubeStandardHost": true,
    "requireImageLoadingHint": true,
    "requireImageDecodingHint": true,
    "requireImageDimensions": true,
    "requireImageSrcSetSizes": true,
    "maxEagerImages": 3
  }
]
```

Reference this file from your pipeline step:
```json
{
  "task": "audit",
  "siteRoot": "./_site",
  "mediaProfiles": "./config/media-profiles.json"
}
```

`doctor` supports the same option:
```json
{
  "task": "doctor",
  "config": "./site.json",
  "mediaProfiles": "./config/media-profiles.json"
}
```

Sample file in this repo:
- `Samples/PowerForge.Web.CodeGlyphX.Sample/config/media-profiles.json`

#### seo-doctor
Runs editorial + technical SEO heuristics over generated HTML.
```json
{
  "task": "seo-doctor",
  "siteRoot": "./_site",
  "checkTitleLength": true,
  "checkDescriptionLength": true,
  "checkH1": true,
  "checkImageAlt": true,
  "checkDuplicateTitles": true,
  "checkOrphanPages": true,
  "checkFocusKeyphrase": false,
  "baseline": "./.powerforge/seo-baseline.json",
  "baselineGenerate": true,
  "reportPath": "./_reports/seo-doctor.json",
  "summaryPath": "./_reports/seo-doctor.md"
}
```
Notes:
- SEO doctor checks include:
  - title/meta-description length heuristics
  - missing/multiple visible `h1`
  - images missing `alt` attribute
  - duplicate title intent across pages
  - orphan page candidates (zero inbound links from scanned pages)
  - optional focus-keyphrase checks via page meta tags
  - canonical checks (duplicate/absolute URL + optional required canonical)
  - canonical alias hygiene (`*.html` pages that duplicate `/<slug>/index.html` must include `robots noindex`)
  - hreflang checks (duplicate/invalid/absolute URL + optional required `x-default`)
  - JSON-LD checks (invalid payload shape/JSON + missing `@context` or `@type`)
- `includeNoIndexPages` defaults to `false`, so `robots noindex` pages are skipped by default.
- Requirement flags are opt-in (default `false`): `requireCanonical`, `requireHreflang`, `requireHreflangXDefault`, `requireStructuredData`.
- Baselines follow the same CI pattern as audit:
  - `baselineGenerate` / `baselineUpdate`
  - `failOnNewIssues` (alias `failOnNew`)
  - `failOnWarnings`, `maxErrors`, `maxWarnings`
- `reportPath` writes full JSON; `summaryPath` writes markdown summary.
- `scopeFromBuildUpdated` supports incremental runs in `--fast` mode when a preceding build step updated HTML files.

#### dotnet-build
Runs `dotnet build`.
```json
{
  "task": "dotnet-build",
  "project": "./src/MySite/MySite.csproj",
  "configuration": "Release",
  "framework": "net9.0",
  "noRestore": true,
  "skipIfProjectMissing": true
}
```

#### dotnet-publish
Runs `dotnet publish` and (optionally) applies Blazor fixes.
```json
{
  "task": "dotnet-publish",
  "project": "./src/MySite/MySite.csproj",
  "out": "./Artifacts/publish",
  "configuration": "Release",
  "framework": "net9.0",
  "selfContained": false,
  "noBuild": true,
  "noRestore": true,
  "baseHref": "/",
  "defineConstants": "DOCS_BUILD",
  "blazorFixes": true,
  "skipIfProjectMissing": true
}
```

Notes:
- Blazor publish fixes are enabled by default.
  - Disable via `blazorFixes: false` (alias: `blazor-fixes: false`) or `noBlazorFixes: true` (alias: `no-blazor-fixes: true`).
- If you specify both `blazorFixes` and `noBlazorFixes`, they must be logically consistent; conflicting values fail the step.
- `defineConstants` maps to `-p:DefineConstants=...` for multi-variant Blazor publishes.
- `skipIfProjectMissing` (`skipIfMissingProject`, `skip-if-project-missing`) makes the step succeed with a skip message when the project path is absent (useful for worktree-only/partial checkouts).

#### hook
Runs a named hook command with deterministic pipeline context and optional output capture.
```json
{
  "task": "hook",
  "event": "pre-build",
  "command": "dotnet",
  "args": "--version",
  "contextPath": "./_reports/hooks/pre-build.json",
  "stdoutPath": "./_reports/hooks/pre-build.stdout.log",
  "stderrPath": "./_reports/hooks/pre-build.stderr.log",
  "env": {
    "PF_HOOK_PROFILE": "docs"
  }
}
```

Notes:
- `event` (aliases: `hook`, `name`) and `command` (aliases: `cmd`, `file`) are required.
- Security: `hook` runs external processes from pipeline config. Treat hook-enabled pipeline files as trusted code only.
- Arguments work the same as `exec` (`args`/`arguments` or `argsList`/`argumentsList`).
- `contextPath` writes a JSON payload with hook metadata (`event`, `mode`, step label/id, directories, UTC timestamp).
- Built-in environment variables are injected automatically:
  - `POWERFORGE_HOOK_EVENT`
  - `POWERFORGE_HOOK_LABEL`
  - `POWERFORGE_HOOK_MODE`
  - `POWERFORGE_HOOK_WORKDIR`
  - `POWERFORGE_HOOK_BASEDIR`
  - `POWERFORGE_HOOK_CONTEXT` (when `contextPath` is set)
- `env` (`environment`) adds or overrides environment variables for the hook process.
- `stdoutPath` / `stderrPath` persist captured process streams for CI diagnostics.
- `allowFailure` (`continueOnError`) keeps the pipeline green when the hook exits non-zero.
- `hook` steps are intentionally not cacheable (external side effects/plugins).

#### html-transform
Runs an external transform command for each HTML file under `siteRoot` with include/exclude filtering.
```json
{
  "task": "html-transform",
  "siteRoot": "./_site",
  "include": ["docs/**", "index.html"],
  "exclude": ["docs/drafts/**"],
  "command": "my-transform",
  "argsList": ["--input", "{file}", "--mode", "docs"],
  "writeMode": "inplace",
  "reportPath": "./_reports/html-transform.json"
}
```

Filter-style mode (command writes transformed HTML to stdout):
```json
{
  "task": "html-transform",
  "siteRoot": "./_site",
  "command": "my-html-filter",
  "args": "--file {file}",
  "writeMode": "stdout",
  "requireOutput": true
}
```

Notes:
- `siteRoot` (`site-root`) and `command` (`cmd`, `file`) are required.
- `include` / `exclude` support array or comma-separated string globs relative to `siteRoot`.
- `extensions` defaults to `.html,.htm`.
- `writeMode`:
  - `inplace` (default): command updates files directly.
  - `stdout`: engine replaces file content with captured stdout.
- Token expansion in `args` / `argsList` / `env` values:
  - `{file}` absolute file path
  - `{relative}` relative path from `siteRoot`
  - `{siteRoot}` absolute site root
  - `{index}` zero-based file index
- Built-in per-file environment variables:
  - `POWERFORGE_TRANSFORM_FILE`
  - `POWERFORGE_TRANSFORM_RELATIVE`
  - `POWERFORGE_TRANSFORM_SITE_ROOT`
  - `POWERFORGE_TRANSFORM_INDEX`
- `stdin: true` pipes original file content to process stdin.
- `allowFailure` (`continueOnError`) continues processing and reports allowed failures.
- `reportPath` emits JSON with processed/changed/failed counts and per-file outcomes.
- `html-transform` steps are intentionally not cacheable (external side effects/plugins).

#### data-transform
Runs a single data transform command with explicit input/output paths.
```json
{
  "task": "data-transform",
  "input": "./_temp/data/source.json",
  "out": "./_temp/data/transformed.json",
  "command": "my-data-filter",
  "argsList": ["--input", "{input}", "--output", "{output}"],
  "inputMode": "file",
  "writeMode": "passthrough",
  "reportPath": "./_reports/data-transform.json"
}
```

Filter-style mode (input through stdin, transformed data from stdout):
```json
{
  "task": "data-transform",
  "input": "./_temp/data/source.json",
  "out": "./_temp/data/transformed.json",
  "command": "my-json-filter",
  "args": "--pretty",
  "inputMode": "stdin",
  "writeMode": "stdout"
}
```

Notes:
- Required:
  - input: `input` (`inputPath`, `source`, etc.)
  - output: `out` (`output`, `outputPath`, `destination`, etc.)
  - command: `command` (`cmd`, `file`)
- `inputMode` (`input-mode`, `transformMode`, `transform-mode`):
  - `stdin` (default): input file content is piped to process stdin.
  - `file`: process reads input path directly.
- `writeMode`:
  - `stdout` (default): process stdout becomes output file content.
  - `passthrough`: process is expected to write output file itself.
- Token expansion in args/env values:
  - `{input}`
  - `{output}`
  - `{baseDir}`
- Built-in environment variables:
  - `POWERFORGE_DATA_INPUT`
  - `POWERFORGE_DATA_OUTPUT`
  - `POWERFORGE_DATA_BASEDIR`
  - `POWERFORGE_DATA_MODE`
  - `POWERFORGE_DATA_WRITE_MODE`
- `allowFailure` (`continueOnError`) keeps pipeline green on non-zero exit and reports as allowed failure.
- `reportPath` writes a JSON result summary (exit code, changed flag, mode/writeMode, timestamp).
- `data-transform` steps are intentionally not cacheable (external side effects/plugins).

#### model-transform
Runs built-in typed JSON model operations without external tools.
```json
{
  "task": "model-transform",
  "input": "./_temp/data/source.json",
  "out": "./_temp/data/transformed.json",
  "operations": [
    { "op": "set", "path": "site.name", "value": "PowerForge" },
    { "op": "replace", "path": "site.name", "value": "PowerForge.Web" },
    { "op": "insert", "path": "items", "index": 0, "value": { "id": 0 } },
    { "op": "copy", "from": "site.name", "path": "site.displayName" },
    { "op": "move", "from": "legacy.items", "path": "items" },
    { "op": "append", "path": "items", "value": { "id": 3 } },
    { "op": "merge", "path": "site", "value": { "environment": "ci" } },
    { "op": "remove", "path": "draft" }
  ],
  "strict": true,
  "pretty": true,
  "reportPath": "./_reports/model-transform.json"
}
```

Notes:
- Required:
  - input: `input` (`inputPath`, `source`, etc.)
  - output: `out` (`output`, `outputPath`, `destination`, etc.)
  - operations: `operations` (`ops`, `transforms`)
- Supported operations:
  - `set`: set/replace value at path
  - `replace`: replace value at existing path (fails when target is missing)
  - `insert`: insert value into array at `index`
  - `remove`: remove property/array item at path
  - `append`: append value to array at path
  - `merge`: shallow-merge object properties at path
  - `copy`: copy value from `from` path to `path`
  - `move`: move value from `from` path to `path`
- Path syntax:
  - dot notation: `site.name`
  - array index: `items[0]`
  - wildcard selector: `items[*].title` (and `*` for object keys)
  - recursive selector: `**.enabled` (matches property at any depth)
  - quoted property keys: `meta['x.y']`, `meta[\"z[0]\"]` (for keys containing dots/brackets/spaces)
  - root: `$`
- Wildcard transfer behavior (`copy`/`move`):
  - wildcard source + wildcard target: values are paired in deterministic order
  - single source + wildcard target: source value is applied to all targets
  - wildcard source + single target: requires exactly one source match
- Optional target guards (per operation):
  - `minTargets` / `maxTargets` / `exactTargets` enforce matched target count
  - `exactTargets` cannot be combined with `minTargets`/`maxTargets`
- Optional conditional guard (per operation):
  - `when` (`where`) object can filter targets before applying operation
  - supported keys: `exists` (`present`), `type` (`kind`), `equals` (`eq`), `notEquals` (`not-equals`, `neq`)
  - supports path-based operations (`set`, `replace`, `insert`, `remove`, `append`, `merge`) and destination filtering for `copy`/`move`
  - for wildcard `copy`/`move` source-to-destination pairing, filtered destination count must still match source wildcard count
  - for `copy`/`move`, source filtering is also supported via `fromWhen`/`sourceWhen` (same condition keys)
- Operation aliases:
  - op: `op` or `type`
  - path: `path` or `target`
  - insert index: `index` or `at`
  - source path (copy/move): `from` or `source`
  - target guards: `minTargets`/`min-targets`, `maxTargets`/`max-targets`, `exactTargets`/`exact-targets`
  - condition object: `when` or `where`
  - source condition object (copy/move): `fromWhen`, `sourceWhen`, `fromWhere`, `sourceWhere` (plus kebab-case aliases)
  - value: `value` / `with` / `item` (depending on operation)
- `strict` defaults to `true`:
  - when `true`, invalid paths/types fail the step
  - when `false`, operation-level errors are recorded in report and execution continues
- `pretty` defaults to `true`; `validateJson` defaults to `true`.
- `reportPath` writes operation outcomes and summary counts (including `TargetsApplied` per operation).

#### exec
Runs an external command from the pipeline (extensibility hook for custom generators/tools).
```json
{
  "task": "exec",
  "command": "dotnet",
  "args": "--version",
  "workingDirectory": ".",
  "timeoutSeconds": 120
}
```

Notes:
- `command` (aliases: `cmd`, `file`) is required.
- Security: `exec` runs arbitrary external commands from pipeline config. Use only with trusted pipeline files.
- Pass arguments with `args`/`arguments` or `argsList`/`argumentsList`.
- `allowFailure` (`continueOnError`) keeps the pipeline green when the command exits non-zero.
- `exec` steps are intentionally not cacheable (they can have external side effects).

#### git-sync
Syncs a local working folder from a Git repository (public/private via token env), so pipeline inputs do not need to be pre-cloned.
```json
{
  "task": "git-sync",
  "repo": "EvotecIT/IntelligenceX",
  "repoBaseUrl": "https://github.com",
  "authType": "ssh",
  "destination": "./_temp/src/IntelligenceX",
  "ref": "master",
  "tokenEnv": "GITHUB_TOKEN",
  "retry": 2,
  "retryDelayMs": 750,
  "lockMode": "update",
  "lockPath": "./.powerforge/git-sync-lock.json",
  "writeManifest": true,
  "manifestPath": "./_reports/git-sync.json",
  "clean": true,
  "depth": 1
}
```

Batch mode:
```json
{
  "task": "git-sync",
  "tokenEnv": "GITHUB_TOKEN",
  "repos": [
    {
      "repo": "EvotecIT/IntelligenceX",
      "destination": "./_temp/src/IntelligenceX",
      "ref": "main"
    },
    {
      "repo": "EvotecIT/CodeGlyphX",
      "destination": "./_temp/src/CodeGlyphX",
      "ref": "main",
      "submodules": true
    }
  ]
}
```

Notes:
- `repo` supports URL/path or `owner/name` shorthand (auto-expands to `https://github.com/<owner>/<name>.git`).
- `repoBaseUrl` (`repositoryBaseUrl`, `repoHost`) overrides shorthand expansion base (for example GitHub Enterprise, internal mirrors, or local filesystem mirrors), including nested paths like `group/subgroup/repo`.
- `destination` is required in single-repo mode.
- `repos`/`repositories` enables batch sync in one step (each item must define its own repo + destination).
- Optional `ref` can be a branch/tag/commit.
- `authType` supports `auto` (default), `token`, `ssh`, or `none`.
- For private repositories, set `tokenEnv` (defaults to `GITHUB_TOKEN`) in CI secrets.
- Inline `token` values are supported for compatibility but discouraged; pipeline runtime emits `[PFWEB.GITSYNC.SECURITY]` when detected.
- `authType: token` enforces credential presence and fails fast if `token`/`tokenEnv` is missing.
- `authType: ssh` disables HTTP auth headers and resolves shorthand repos to SSH-style remotes (for example `git@host:group/repo.git`).
- `retry` + `retryDelayMs` control retry attempts for transient git command failures.
- `lockMode`:
  - `off` (default): no lock file behavior.
  - `update`: writes/refreshes commit lock entries.
  - `verify`: requires lock file entries and fails when resolved commits differ from locked commits.
- `lockPath` (`lock`) sets the lock file path. Defaults to `.powerforge/git-sync-lock.json` when `lockMode` is `verify`/`update`.
- `sparseCheckout` (array) or `sparsePaths` (comma-separated) can reduce checkout size.
- `submodules: true` initializes submodules; add `submodulesRecursive` and `submoduleDepth` for large mono-repo trees.
- `writeManifest` + `manifestPath` can emit a JSON file with resolved refs and destinations for downstream automation.
- `git-sync` steps are intentionally not cacheable (remote state can change without pipeline spec changes).

#### sources-sync
Synchronizes `SiteSpec.Sources` from `site.json` using the same implementation as `git-sync`.
This is a convenience wrapper that lets you declare repos once (in `site.json`) and reuse them in both local builds and CI.

```json
{ "task": "sources-sync", "config": "./site.json", "lockMode": "update", "writeManifest": true }
```

Notes:
- Reads `Sources` from `site.json` and maps each entry to a `git-sync` repo item.
- Lock/manifest settings are passed through to the underlying `git-sync` implementation (`lockMode`, `lockPath`, `writeManifest`, `manifestPath`).
- Recommended workflow:
  - In CI: use `lockMode: verify` with a committed lock file.
  - In dev: use `lockMode: update` to refresh locks when you intentionally bump refs.

#### overlay
Copies a static overlay directory into another (useful for Blazor outputs).
```json
{
  "task": "overlay",
  "source": "./Artifacts/playground/wwwroot",
  "destination": "./Artifacts/site/playground",
  "clean": true,
  "include": "**/*",
  "exclude": "**/*.map"
}
```

Notes:
- `include` and `exclude` are comma-separated patterns in pipeline JSON.
- Example: `"include": "**/*.html,**/*.css"`
- `clean: true` deletes the destination folder before copying (avoids stale files).

#### hosting
Keeps only selected host redirect artifacts in the built site output (useful when one deployment target should not ship other host configs).
```json
{
  "task": "hosting",
  "siteRoot": "./_site",
  "targets": "apache,iis",
  "removeUnselected": true
}
```

Notes:
- Supported targets: `netlify`, `azure`, `vercel`, `apache`/`apache2`, `nginx`, `iis` (or `all`).
- Reads/writes host artifact files under `siteRoot`:
  - `netlify` -> `_redirects`
  - `azure` -> `staticwebapp.config.json`
  - `vercel` -> `vercel.json`
  - `apache` -> `.htaccess`
  - `nginx` -> `nginx.redirects.conf`
  - `iis` -> `web.config`
- `removeUnselected` defaults to `true`.
- `strict: true` fails if selected target artifacts are missing.
- `site-root` is supported as an alias for `siteRoot`.

#### cloudflare
Purges Cloudflare cache or verifies `cf-cache-status` on selected URLs.

```json
{
  "task": "cloudflare",
  "operation": "purge",
  "zoneId": "YOUR_ZONE_ID",
  "tokenEnv": "CLOUDFLARE_API_TOKEN",
  "baseUrl": "https://example.com",
  "paths": "/,/docs/,/api/,/blog/"
}
```

Verify mode (good for post-deploy smoke checks):

```json
{
  "task": "cloudflare",
  "operation": "verify",
  "baseUrl": "https://example.com",
  "paths": "/,/docs/,/api/,/blog/",
  "allowStatuses": "HIT,REVALIDATED,EXPIRED,STALE",
  "warmupRequests": 1,
  "timeoutMs": 15000,
  "reportPath": "./_reports/cloudflare-cache.json",
  "summaryPath": "./_reports/cloudflare-cache.md"
}
```

Site-profile mode (auto route discovery from `site.json`):

```json
{
  "task": "cloudflare",
  "operation": "verify",
  "siteConfig": "./site.json"
}
```

Notes:
- `operation` (`action`) supports `purge` (default) and `verify`.
- `purge` requires `zoneId` (`zone-id`) plus `token` or `tokenEnv` (defaults to `CLOUDFLARE_API_TOKEN`).
- `verify` does not require zone/token and fails the step when a URL returns a non-allowed cache status.
- `paths` are combined with `baseUrl`; `urls` accepts full URLs directly.
- `reportPath`/`summaryPath` (verify mode) write JSON + Markdown artifacts for CI diagnostics.
- When `siteConfig` is provided and no explicit `paths`/`urls` are passed:
  - `verify` uses route-derived verify paths.
  - `purge` uses verify paths plus purge-only artifacts (`/404.html`, `llms` files).
- `cloudflare` steps are intentionally not cacheable (external side effects).

#### engine-lock
Reads/verifies/updates `.powerforge/engine-lock.json` from pipeline runs.

Verify drift (default operation):
```json
{
  "task": "engine-lock",
  "path": "./.powerforge/engine-lock.json",
  "expectedRepository": "EvotecIT/PSPublishModule",
  "expectedRef": "ab58992450def6b736a2ea87e6a492400250959f",
  "failOnDrift": true,
  "requireImmutableRef": true
}
```

Update pin:
```json
{
  "task": "engine-lock",
  "operation": "update",
  "path": "./.powerforge/engine-lock.json",
  "repository": "EvotecIT/PSPublishModule",
  "ref": "0123456789abcdef0123456789abcdef01234567",
  "channel": "candidate",
  "reportPath": "./_reports/engine-lock.json",
  "summaryPath": "./_reports/engine-lock.md"
}
```

Notes:
- Task aliases: `engine-lock` and `enginelock`.
- Operations:
  - `verify` (default): fail on drift unless `failOnDrift:false`
  - `show`: read and report current lock
  - `update`: write/refresh lock file values
- `useEnv:true` can pull expected values from env (`POWERFORGE_REPOSITORY`, `POWERFORGE_REF`, `POWERFORGE_CHANNEL`) or custom env names via `repositoryEnv`/`refEnv`/`channelEnv`.
- `requireImmutableRef:true` (aliases: `require-immutable-ref`, `requireSha`, `require-sha`) enforces commit SHA refs (40/64 hex), recommended for CI.
- `continueOnError:true` keeps pipeline green if this step fails (useful for canary diagnostics).
- JSON/summary artifacts include `immutableRef` so CI logs can quickly spot branch/tag pins.
- `engine-lock` steps are intentionally not cacheable.

#### github-artifacts-prune
Prunes GitHub Actions artifacts to control repository storage quota.  
Safe by default: `dryRun` is `true` unless you set `apply:true`.

```json
{
  "task": "github-artifacts-prune",
  "repo": "EvotecIT/IntelligenceX",
  "tokenEnv": "GITHUB_TOKEN",
  "name": "test-results*,coverage*,github-pages",
  "keep": 5,
  "maxAgeDays": 7,
  "maxDelete": 200,
  "dryRun": true,
  "reportPath": "./_reports/github-artifacts.json",
  "summaryPath": "./_reports/github-artifacts.md"
}
```

Apply mode:
```json
{
  "task": "github-artifacts-prune",
  "repo": "EvotecIT/IntelligenceX",
  "tokenEnv": "GITHUB_TOKEN",
  "name": "test-results*,coverage*,github-pages",
  "keep": 5,
  "maxAgeDays": 7,
  "maxDelete": 200,
  "apply": true,
  "failOnDeleteError": true
}
```

Notes:
- Task aliases: `github-artifacts-prune` and `github-artifacts`.
- Repo/token resolution:
  - `repo`/`repository`, fallback env `repoEnv` (default `GITHUB_REPOSITORY`).
  - `token`, fallback env `tokenEnv` (default `GITHUB_TOKEN`).
- Pattern options:
  - include: `name`/`names`/`include`/`includes`
  - exclude: `exclude`/`excludes`/`excludeNames`
- Safety defaults:
  - `dryRun:true`, `keep:5`, `maxAgeDays:7`, `maxDelete:200`
- Use `apiBaseUrl` for GitHub Enterprise API endpoints or local integration testing.
- `reportPath`/`summaryPath` write JSON + Markdown outputs for CI diagnostics.
- `github-artifacts-prune` steps are intentionally not cacheable (external side effects).

#### indexnow
Submits changed or selected canonical URLs to IndexNow-compatible endpoints.
```json
{
  "task": "indexnow",
  "baseUrl": "https://intelligencex.dev",
  "siteRoot": "./_site",
  "scopeFromBuildUpdated": true,
  "keyEnv": "INDEXNOW_KEY",
  "keyLocation": "https://intelligencex.dev/your-indexnow-key.txt",
  "batchSize": 500,
  "retryCount": 2,
  "retryDelayMs": 750,
  "reportPath": "./_reports/indexnow.json",
  "summaryPath": "./_reports/indexnow.md"
}
```

Explicit URL/path mode:
```json
{
  "task": "indexnow",
  "baseUrl": "https://codeglyphx.com",
  "keyEnv": "INDEXNOW_KEY",
  "paths": "/,/docs/,/api/,/blog/",
  "sitemap": "./_site/sitemap.xml",
  "dryRun": true
}
```

Notes:
- URL sources can be combined:
  - `urls` / `url` (absolute URLs)
  - `paths` / `path` (combined with `baseUrl`)
  - `urlFile` (newline-separated URLs/paths)
  - `sitemap` (`<loc>` entries from sitemap XML)
  - `scopeFromBuildUpdated` (`--fast` defaults this behavior on) when a preceding `build` step updated HTML files.
- Auth/key options:
  - `key` (inline), `keyPath` (file), or `keyEnv` (recommended; defaults to `INDEXNOW_KEY`).
  - `optionalKey:true` (aliases: `optional-key`, `skipIfMissingKey`) turns missing key into a non-failing skip.
  - `keyLocation` can be explicit, otherwise defaults to `https://<host>/<key>.txt`.
- Endpoint options:
  - default endpoint is `https://api.indexnow.org/indexnow`
  - override via `endpoint`/`endpoints` (aliases: `engine`/`engines`).
- Reliability controls:
  - `batchSize`, `retryCount`/`retryDelayMs`, `timeoutSeconds`.
  - `continueOnError:true` keeps the step green even when some requests fail.
- Safety controls:
  - `failOnEmpty:true` fails when no URLs are collected.
  - `maxUrls` + `truncateToMaxUrls` keep submissions bounded.
- `indexnow` steps are intentionally not cacheable (external side effects).

## Publish spec (`powerforge-web publish`)

Publish specs wrap a typical build + publish flow into a single config.
Paths are resolved relative to the publish JSON file location.

Schema:
- `Schemas/powerforge.web.publishspec.schema.json`

Minimal publish:
```json
{
  "$schema": "./Schemas/powerforge.web.publishspec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Config": "./site.json",
    "Out": "./Artifacts/site",
    "Clean": true
  },
  "Publish": {
    "Project": "./src/MySite/MySite.csproj",
    "Out": "./Artifacts/publish",
    "Configuration": "Release"
  }
}
```

Full publish with overlay + optimize:
```json
{
  "$schema": "./Schemas/powerforge.web.publishspec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Config": "./site.json",
    "Out": "./Artifacts/site"
  },
  "Overlay": {
    "Source": "./Artifacts/site",
    "Destination": "./src/MySite/wwwroot",
    "Include": ["**/*"],
    "Exclude": ["**/*.map"]
  },
  "Publish": {
    "Project": "./src/MySite/MySite.csproj",
    "Out": "./Artifacts/publish",
    "Configuration": "Release",
    "Framework": "net9.0",
    "DefineConstants": "TRACE;DOCS_BUILD",
    "BaseHref": "/",
    "ApplyBlazorFixes": true
  },
  "Optimize": {
    "SiteRoot": "./Artifacts/site",
    "CriticalCss": "./themes/nova/critical.css",
    "MinifyHtml": true,
    "MinifyCss": true,
    "MinifyJs": true
  }
}
```

### Publish vs pipeline

- **Pipeline** is granular (step-by-step) and better for complex flows.
- **Publish** is a shortcut for the common build + overlay + dotnet publish + optimize flow.

If you need API docs, multiple overlays, or per-project outputs, use pipeline.

## Common pitfalls (and how to avoid them)

- **Generated output in source**: keep API docs in `Artifacts/` and overlay into output.
- **Wrong base URL**: set `BaseUrl` in `site.json` and either pass `baseUrl` in sitemap step or use `config` so the step can resolve it.
- **Paths resolve wrong**: remember specs resolve relative to their own JSON file.
