# PowerForge.Web API Docs Styling Guide

This guide documents the CSS hooks used by the built-in API docs templates and
the JavaScript behaviors they rely on. Use it when creating a custom theme or
overriding templates via `templateRoot`.

## Template variants

Two template modes are available:
- `simple` (minimal, single-column)
- `docs` (sidebar layout)

Sidebar position (`docs` template):
- Use `sidebar: "right"` (pipeline) or `--sidebar right` (CLI) to move the sidebar.
- Default is `left`.
Body class:
- `bodyClass` (pipeline) or `--body-class` (CLI) to set the `<body>` class.
- Default: `pf-api-docs` (used by fallback CSS to align theme tokens).

To customize templates, copy the embedded defaults from:
`PowerForge.Web/Assets/ApiDocs` into your own folder and pass it as
`templateRoot`. Use `index.html`/`type.html` for the simple layout and
`docs-index.html`/`docs-type.html` for the docs layout.

## Common tokens (all templates)

- `{{CSS}}` injects a stylesheet link or inline fallback CSS.
- `{{CRITICAL_CSS}}` injects optional critical CSS HTML into `<head>` (typically `<style>...</style>`).
- `{{HEADER}}` / `{{FOOTER}}` are optional HTML fragments.

When `nav`/`navJsonPath` is set and `headerHtml`/`footerHtml` are not provided, the generator falls back to embedded header/footer fragments so API pages still include basic site navigation. Provide explicit fragments to fully control branding and markup.
If your fragments include `{{NAV_*}}` placeholders but you forgot to provide `nav`/`navJsonPath`, the generator emits `[PFWEB.APIDOCS.NAV.REQUIRED]` and (by default) the pipeline fails in CI.

Optional critical CSS:
- In the pipeline `apidocs` step, use `injectCriticalCss: true` (requires `config`) to inline `assetRegistry.criticalCss` from `site.json` into API pages.
- Or use `criticalCssPath` to inline a single CSS file.

Overview identity and quick start:
- `title` is used as both document title and visible API overview heading (`<h1>`).
- `quickStartTypes` (aliases: `quickstartTypes`, `quick-start-types`) accepts comma-separated type names for the "Quick Start" and "Main API" sections.
- CLI equivalent: `--quickstart-types TypeA,TypeB`.
- `displayNameMode` (pipeline) / `--display-name-mode` (CLI) controls type labels in docs + JSON:
  - `short` keeps short names only
  - `namespace-suffix` (default) disambiguates duplicates as `Type (Namespace.Part)`
  - `full` uses full type names
- `generateGitFreshness` (pipeline) / `--git-freshness` (CLI) enables opt-in git freshness metadata:
  - `gitFreshnessNewDays` / `--git-freshness-new-days` controls the `new` window (default `14`)
  - `gitFreshnessUpdatedDays` / `--git-freshness-updated-days` controls the `updated` window (default `90`)
  - emitted JSON adds `freshness.status`, `freshness.lastModifiedUtc`, `freshness.commitSha`, `freshness.ageDays`, and `freshness.sourcePath`
  - HTML badges render only for `new` / `updated`; `stable` remains unbadged to avoid visual noise

If your site uses `Navigation.Profiles` (route/layout specific menus), set:
- `navContextPath` (defaults to `/`)
  - Set this when you want API pages to match a specific `Navigation.Profile` (for example `"/api/"`).
- optionally `navContextLayout` / `navContextCollection` / `navContextProject`
 so the generator can select the same profile your theme uses. For best results, point `nav` at `site-nav.json` (the nav export) when available.
- optionally `navSurface` (pipeline) / `--nav-surface` (CLI)
  - Forces API docs to consume a specific `site-nav.json` surface (for example `apidocs`, `docs`, `main`) when present.

## Best practice: enforce CSS + fragments with featureContracts
To prevent API regressions (generator adds new UI but the theme does not style it), define a theme-level contract in `theme.manifest.json`:
```json
{
  "name": "mytheme",
  "schemaVersion": 2,
  "features": ["apiDocs"],
  "featureContracts": {
    "apiDocs": {
      "requiredPartials": ["api-header", "api-footer"],
      "cssHrefs": ["/css/app.css", "/css/api-docs.css"],
      "requiredCssSelectors": [
        ".api-layout", ".api-sidebar", ".api-content",
        ".sidebar-toggle", ".type-item", ".filter-button",
        ".member-card", ".member-signature"
      ]
    }
  }
}
```
Notes:
- `powerforge-web verify` emits `Theme CSS contract:` warnings when selectors are missing (code: `[PFWEB.THEME.CSS.CONTRACT]`).
- The `apidocs` pipeline step can fail in CI on API docs warnings (including `API docs CSS contract:`; code: `[PFWEB.APIDOCS.CSS.CONTRACT]`).

## Simple template CSS hooks

Element + class / id:
- `main.pf-api` – main container
- `.pf-api-search` – search block wrapper
- `#api-search` – search input (from `search.js`)
- `#api-results` / `.pf-api-results` – search results list
- `.pf-api-result` – search result entry
- `.pf-api-empty` – “no results” message
- `.pf-api-types` – grid of type links
- `.pf-api-type` – individual type link tile
- `.pf-api-section` – members section
- `.pf-api-params` – parameter list wrapper
- `.pf-api-returns` – returns summary
- `.pf-api-remarks` – remarks block

## Docs template CSS hooks (sidebar layout)

Layout + nav:
- `.api-layout` – layout wrapper
- `.api-layout.sidebar-right` – swaps sidebar to the right column
- `.api-content` – main content column
- `.api-sidebar` – sidebar container
- `.sidebar-toggle` – mobile toggle button
- `.sidebar-overlay` – overlay for mobile drawer
- `.sidebar-open` – applied to `.api-sidebar` when open
- `.active` – applied to `.sidebar-overlay` when open

Sidebar sections:
- `.sidebar-header` – sidebar header block
- `.sidebar-title` – title link
- `.sidebar-title.active` – active state for sidebar title
- `.sidebar-search` – search input wrapper
- `#api-filter` – filter input (from `docs.js`)
- `.clear-search` – clear button for filter
- `.sidebar-filters` – filter wrapper (type + namespace)
- `.filter-label` – label text
- `.filter-buttons` – type filter button row
- `.filter-button` – type filter button
- `.filter-button.active` – active type filter
- `.namespace-select` – namespace dropdown
- `.sidebar-count` – “showing X of Y types” label
- `.sidebar-tools` – wrapper for expand/collapse controls
- `.sidebar-expand-all` – expand all namespaces button
- `.sidebar-collapse-all` – collapse all namespaces button
- `.sidebar-reset` – reset all filters button
- `.sidebar-nav` – sidebar nav wrapper
- `.sidebar-empty` – “no matching types” placeholder
- `.nav-section` – nav section block
- `.nav-section-header` – section header
- `.nav-section-header.main-api` – main API section header
- `.nav-section-content` – section body
- `.nav-section-content.collapsed` – collapsed state
- `.chevron` / `.chevron.expanded` – collapse indicator
- `.type-item` – sidebar type link
- `.type-item.active` – active type link
- `.type-item` includes `data-kind` and `data-namespace` for filtering
- `.sidebar-footer` – footer block (optional)

URL behavior:
- Docs template uses clean URLs ending with `/` (for example: `/api/my-type/`).
- The generator writes `index.html` under `/api/<slug>/` so static servers render correctly.
- Legacy flat alias behavior for `/api/<slug>.html` is configurable via `legacyAliasMode`:
  - `noindex` (default): write alias page with `noindex,follow`
  - `redirect`: write lightweight alias redirect page to `/api/<slug>/`
  - `omit`: do not emit alias page
- The "Back to Docs" link defaults to `/docs/` and can be overridden via `docsHomeUrl`.

Overview chips:
- `.api-overview` – overview wrapper
- `.type-chips` – chips container
- `.type-chip` – type chip link
- `.type-chip.<kind>` – kind class (`class`, `struct`, `enum`, `interface`, `delegate`)
- `.type-chip` includes `data-kind` and `data-namespace` for filtering
- `.freshness-badge` – freshness badge shell
- `.freshness-badge.new` – recent/new state
- `.freshness-badge.updated` – recently updated state
- `.type-freshness-badge` – type detail header placement
- `.type-list-freshness` – sidebar row placement
- `.quick-card-freshness` – quick-start card placement
- `.type-chip-freshness` – namespace chip placement

Member layout:
- `.member-toolbar` – member filter toolbar
- `.member-filter` – member search input wrapper
- `#api-member-filter` – member search input
- `.member-kind-filter` – member kind filter buttons
- `.member-kind` – member kind button
- `.member-kind.active` – active member kind
- `.member-kind[data-member-kind="extension"]` – extension methods filter
- `.member-kind[data-member-kind="constructor"]` – constructors filter
- `.member-toggle` – toggle for inherited members
- `.member-actions` – member toolbar actions (expand/collapse/reset)
- `.member-expand-all` – expand all member sections
- `.member-collapse-all` – collapse all member sections
- `.member-reset` – reset member filters
- `.member-section` – section wrapper (methods/properties/fields/events)
- `.member-section-header` – section header row
- `.member-section-toggle` – collapse/expand button
- `.member-section-body` – section content container
- `.member-section.collapsed` – collapsed section state
- `.member-group` – overload group wrapper
- `.member-group-header` – overload group header
- `.member-group-name` – overload group name
- `.member-group-count` – overload count
- `.member-group-body` – overload group body
- `.member-card` – member card block (data attributes: `data-kind`, `data-inherited`, `data-search`)
- `.member-signature` – signature code line
- `.member-anchor` – anchor link
- `.member-source` – source link row (optional)
- `.member-return` – return type row
- `.member-inherited` – inherited badge row
- `.member-value` – enum value row
- `.param-default` – parameter default value
- `.member-attributes` – attribute list for members
- `.member-summary` – summary paragraph
- `.typeparam-list` – type parameter list
- `.exception-list` – exception list
- `.see-also-list` – see-also list
- `.type-meta-attributes` – attribute list for types
- `.type-meta-interfaces` – implemented interfaces list
- `.type-meta-inheritance` – base type row
- `.type-meta-flags` – modifiers row
- `.type-meta-source` – source link row (optional)
- `.type-toc` – per-type table of contents
- `.type-toc-title` – TOC title label
- `.type-toc-toggle` – collapse/expand button
- `.type-inheritance` – inheritance chain section
- `.inheritance-list` – inheritance list
- `.inheritance-current` – current type within inheritance list
- `.type-derived` – derived types section
- `.derived-list` – derived types list
- `.type-parameters` – type parameter section
- `.type-examples` – example section
- `.example-media` – example media figure wrapper
- `.example-media-frame` – media surface (image/video/link frame)
- `.example-media-link` – terminal/download link for non-inline example media
- `.example-media-caption` – media caption
- `.example-media-meta` – capture recency / freshness metadata line
- `.type-see-also` – see also section

## JavaScript expectations

`docs.js` expects:
- `#api-filter` input
- `.type-item` and `.type-chip` elements with `data-search` attribute
- `.nav-section-header` + `.nav-section-content` for collapse/expand
- `.sidebar-toggle` + `.sidebar-overlay` for mobile drawer
- `.filter-button` elements with `data-kind`
- `#api-namespace` select for namespace filtering
- `#api-member-filter` input
- `.member-kind` buttons with `data-member-kind`
- `#api-show-inherited` checkbox
- `.sidebar-empty` placeholder (optional)
- URL hash state (optional): `#k=class&ns=My.Namespace&q=filter&mk=method&mq=member&mi=1&mc=methods,fields&tc=1`

## Type metadata

When assembly reflection is available, the generator emits:
- base type + implemented interfaces
- static/abstract/sealed flags
- attributes for types and members
- extension methods (shown under "Extension Methods")
- constructors are rendered in their own section and overloads are grouped by name
- optional source links (when `sourceRoot` + `sourceUrl` are set and PDBs exist)

`search.js` (simple template) expects:
- `#api-search` input
- `#api-results` container
- `index.json` + `search.json` present under `/api`

If you change class names or IDs, provide your own JS via `docsScript` /
`searchScript`.

## Fallback CSS

When `css` is not provided, the generator inlines `fallback.css`.
To fully control the visuals, provide `css` or supply a custom `fallback.css`
via `templateRoot`.

`css` may be a single href or a comma/whitespace-separated list of hrefs. When
multiple are provided, the generator emits multiple `<link rel="stylesheet" ...>`
tags (for example `"/css/app.css,/css/api.css"`).

## XML docs vs reflection

If the XML documentation file is missing or empty, the generator falls back to
reflection (public types only). This still produces the API reference pages,
but summaries/remarks/parameter descriptions will be empty until `///` comments
are added.

To limit output to *only* documented types, set `includeUndocumented: false`
in the pipeline or pass `--documented-only` to the CLI.

`<see cref="...">` and `<seealso cref="...">` tags are converted into links when the
referenced type exists in the generated API docs.

XML-doc `<example>` blocks can also include optional media nodes for richer API examples:

```xml
<example>
  <code>Sample.Run();</code>
  <image src="/images/sample-output.png" alt="Rendered output" caption="Example screenshot." />
  <media kind="terminal" src="/casts/sample.cast" title="Terminal playback" caption="Recorded terminal output." poster="/images/sample-terminal.png" mimeType="application/x-asciicast" />
</example>
```

Supported media nodes:
- `<image ... />` / `<img ... />` / `<screenshot ... />`
- `<video ... />`
- `<terminal ... />`
- `<media kind="image|video|terminal|link" ... />`

Generated JSON keeps those entries under `examples[]` with `kind: "media"` and a structured `media` object (`type`, `url`, `title`, `alt`, `caption`, `posterUrl`, `mimeType`, `width`, `height`, optional `capturedAtUtc`, optional `sourceUpdatedAtUtc`).

## PowerShell help

Set `type: PowerShell` and point `help`/`helpPath` to a PowerShell help XML file
(for example `Module/en-US/MyModule-help.xml`) or a directory containing one.
Each command is treated as a "type" with parameter sets rendered as methods.
When explicit parameter set names are unavailable in help XML, PowerForge derives
stable labels (for example `By Name`, `By Id`, `Set 1`) so users can distinguish syntax choices.
PowerForge also reads parameter allowed-values metadata (for example `ValidateSet`
and enum names when present in help payload), showing those values in syntax placeholders,
parameter details, and fallback examples.
PowerForge classifies command kinds (`Cmdlet` / `Function` / `Alias`) using
best-effort module metadata discovery (manifest exports + root module functions)
when available. If help XML includes `commandType`, it takes precedence over
manifest hints. Manifest parsing supports multiline export arrays and inline
comments.

When `help` points at a directory (or a directory can be inferred from the help
xml location), `about_*` files are also imported into the API output:
- `about_*.help.txt`
- `about_*.txt`
- `about_*.md` / `about_*.markdown`

Imported `about_*` topics render as `About` entries and are linkable from command
remarks (for example `about_CommonParameters`).

## Usage scenarios

### C# library with XML docs

Pipeline step:
```json
{
  "task": "apidocs",
  "type": "CSharp",
  "xml": "./bin/Release/net10.0/MyLib.xml",
  "assembly": "./bin/Release/net10.0/MyLib.dll",
  "out": "./_site/api",
  "format": "both",
  "template": "docs",
  "css": "/css/api-docs.css",
  "sidebar": "right",
  "sourceRoot": "..",
  "sourceUrl": "https://github.com/YourOrg/YourRepo/blob/main/{path}#L{line}"
}
```

CLI:
```bash
powerforge-web apidocs --type csharp --xml ./bin/Release/net10.0/MyLib.xml --assembly ./bin/Release/net10.0/MyLib.dll --out ./_site/api --format both --template docs --css /css/api-docs.css --sidebar right --source-root .. --source-url "https://github.com/YourOrg/YourRepo/blob/main/{path}#L{line}"
```

### PowerShell module help

Pipeline step:
```json
{
  "task": "apidocs",
  "type": "PowerShell",
  "help": "./Module/en-US/MyModule-help.xml",
  "out": "./_site/api",
  "format": "both",
  "template": "docs",
  "css": "/css/api-docs.css",
  "coverageReport": "./_reports/apidocs-coverage.json",
  "psExamplesPath": "./Module/Examples"
}
```

CLI:
```bash
powerforge-web apidocs --type powershell --help-path ./Module/en-US/MyModule-help.xml --out ./_site/api --format both --template docs --css /css/api-docs.css --coverage-report ./_reports/apidocs-coverage.json --ps-examples ./Module/Examples
```

Notes:
- If `helpPath` points to a directory with multiple `*-help.xml` files, the first one is used.
- For deterministic output, point to a specific file.
- `coverageReport` defaults to `coverage.json` under API output and includes completeness metrics (summary/remarks/examples/member docs).
- Coverage report also includes source-link metrics (`source.types`, `source.members`, `source.powershell`) with URL/path coverage and broken-link hints (invalid URLs, unresolved template tokens, repo mismatch hints).
- API docs also generate `xrefmap.json` by default (DocFX-style `references` payload) to support cross-site `xref:` links.
  - CLI: `--xref-map <file>` to customize location, `--no-xref-map` to disable.
  - Pipeline: `xrefMap`/`xref-map` and `generateXrefMap`/`generate-xref-map`.
  - Member-level entries are enabled by default; disable with CLI `--no-member-xref` or pipeline `generateMemberXrefs:false`.
  - Filter member entries with CLI `--member-xref-kinds <list>` (for example `methods,properties`) or pipeline `memberXrefKinds`.
  - Cap member entries with CLI `--member-xref-max-per-type <n>` or pipeline `memberXrefMaxPerType`.
  - Use `powerforge-web xref-merge --out <file> --map <file|dir> [--max-references <n>] [--max-duplicates <n>] [--max-reference-growth-count <n>] [--max-reference-growth-percent <n>]` to combine multiple maps into one shared map with optional growth guardrails.
  - Use the generated map from website builds via `site.json -> Xref.MapFiles`.
  - C# xref maps now include member-level entries (methods/properties/fields/events) with deep links to rendered member anchors.
  - PowerShell xref maps now include parameter-level entries (`parameter:<Command>.<Parameter>`) that deep-link to command syntax cards.
- Source-link diagnostics also emit `[PFWEB.APIDOCS.SOURCE]` warnings for common misconfigurations:
  - `sourceUrlMappings` prefixes that never match discovered source paths
  - likely duplicated GitHub path prefixes (a common cause of 404 "Edit on GitHub" links)
  - `sourceRoot` pointing one level above the GitHub repo while `sourceUrl` targets a single repo without `{root}`
  - source URL templates missing a path token (`{path}`, `{pathNoRoot}`, or `{pathNoPrefix}`)
  - unsupported source URL template tokens (anything outside `{path}`, `{line}`, `{root}`, `{pathNoRoot}`, `{pathNoPrefix}`)
- Display + member diagnostics:
  - `[PFWEB.APIDOCS.DISPLAY]` when `displayNameMode` is unknown (falls back to `namespace-suffix`)
  - `[PFWEB.APIDOCS.MEMBER.SIGNATURES]` when duplicate member signature groups are detected
- PowerShell syntax signatures append `[<CommonParameters>]` for command kinds that support common parameters,
  and docs pages render a dedicated `Common Parameters` section with an `about_CommonParameters` reference.
- PowerShell fallback examples are enabled by default (`generatePowerShellFallbackExamples:true`) and can source snippets from `psExamplesPath` or discovered `Examples/` folders.
- When importing script-based fallback examples, command-specific files (for example `Invoke-Thing.ps1`) are preferred over generic demo scripts when multiple snippets match the same command.
- When PowerShell help has no authored examples, generated fallback examples prefer the most user-friendly parameter sets and can emit multiple examples per command up to `powerShellFallbackExampleLimit`.
- API docs now emit `[PFWEB.APIDOCS.POWERSHELL]` warnings when PowerShell commands rely only on generated fallback examples, so CI can distinguish “has some example” from “has authored examples”.
- Optional PowerShell example validation can parse imported example scripts with the PowerShell parser before publish:
  - CLI: `--validate-ps-examples`
  - Pipeline: `validatePowerShellExamples: true`
  - report path: `--ps-example-validation-report <file>` / `powerShellExampleValidationReport`
  - timeout: `--ps-example-validation-timeout <seconds>` / `powerShellExampleValidationTimeoutSeconds`
  - fail on invalid scripts: `--fail-on-ps-example-validation` / `failOnPowerShellExampleValidation: true`
- Optional matched-example execution can run curated PowerShell example scripts after syntax validation:
  - CLI: `--execute-ps-examples`
  - Pipeline: `executePowerShellExamples: true`
  - execution timeout: `--ps-example-execution-timeout <seconds>` / `powerShellExampleExecutionTimeoutSeconds`
  - fail on execution failures: `--fail-on-ps-example-execution` / `failOnPowerShellExampleExecution: true`
  - only scripts that both parse cleanly and reference documented commands are executed
  - enabling execution implicitly enables validation
- Validation reports default to `powershell-example-validation.json` under the API output root when validation is enabled.
- When execution is enabled, report writing also emits reusable transcript artifacts under a sibling `powershell-example-validation-artifacts/` folder and records each path as `executionArtifactPath`.
- When generated docs also receive that validation result, successful imported PowerShell examples can surface those transcript artifacts as terminal-style example media links in the rendered API reference.
- Imported PowerShell example scripts can also ship richer playback sidecars next to the `.ps1` file: matching `.cast` / `.asciinema` files are staged into API output automatically, and matching `.png` / `.jpg` / `.jpeg` / `.webp` files are used as poster art when present.
- Generated PowerShell playback/transcript media also records `capturedAtUtc` and `sourceUpdatedAtUtc`, so rendered docs can show when a terminal asset was captured and when the backing example script last changed.
- The generator also emits `[PFWEB.APIDOCS.POWERSHELL]` warnings when curated playback sidecars look unhealthy: unsupported same-name sidecars (for example `.gif` / `.mp4` / `.webm`), oversized casts/posters, or stale playback assets that are older than the `.ps1` script they document.
- Validation emits `[PFWEB.APIDOCS.POWERSHELL]` warnings when imported example scripts fail syntax validation, when a script does not reference any documented command from the selected help file, or when a matched example script fails execution.
- PowerShell `examples` entries in generated JSON now include an `origin` field when PowerForge can identify provenance:
  - `AuthoredHelp` for examples from MAML help XML
  - `ImportedScript` for examples imported from `psExamplesPath` / `Examples/`
  - `GeneratedFallback` for auto-generated fallback examples
- Docs-template HTML now surfaces that provenance with example badges, so readers can immediately tell whether a snippet was authored in help, imported from a curated script, or generated as fallback guidance.
- Coverage reports now split PowerShell example coverage into `authoredHelpCodeExamples`, `importedScriptCodeExamples`, and `generatedFallbackCodeExamples`, alongside the existing `generatedFallbackOnlyExamples` guardrail.
- Coverage reports also track richer imported playback quality via `importedScriptPlaybackMedia`, `importedScriptPlaybackMediaWithPoster`, and `importedScriptPlaybackMediaWithoutPoster`, plus command lists for playback media usage and posterless playback assets.
- Coverage reports also track playback asset-health issues via `importedScriptPlaybackMediaUnsupportedSidecars`, `importedScriptPlaybackMediaOversizedAssets`, and `importedScriptPlaybackMediaStaleAssets`, with matching command lists so CI can flag rough media curation precisely.
- API generation emits `[PFWEB.APIDOCS.POWERSHELL]` warnings when imported playback media exists without matching poster art, so teams can catch rough terminal embeds before publish.
- API generation also emits `[PFWEB.APIDOCS.POWERSHELL]` warnings for unhealthy playback assets, but those issues are now measurable in coverage too instead of living only in console warnings.
- Pipeline coverage thresholds can also gate provenance-specific example quality via `minPowerShellAuthoredHelpCodeExamplesPercent` and `minPowerShellImportedScriptCodeExamplesPercent`.
- Pipeline coverage thresholds can also gate playback richness via `minPowerShellImportedScriptPlaybackMediaPercent`, `minPowerShellImportedScriptPlaybackMediaWithPosterPercent`, and `maxPowerShellImportedScriptPlaybackMediaWithoutPosterCount`.
- Pipeline coverage thresholds can also gate playback asset health via `maxPowerShellImportedScriptPlaybackMediaUnsupportedSidecarCount`, `maxPowerShellImportedScriptPlaybackMediaOversizedAssetCount`, and `maxPowerShellImportedScriptPlaybackMediaStaleAssetCount`.
- Pipeline coverage thresholds can now gate generated-fallback-only quality via `maxPowerShellGeneratedFallbackOnlyExamplePercent` or `maxPowerShellGeneratedFallbackOnlyExampleCount`.
- In pipeline `apidocs` steps, you can gate quality with coverage thresholds (for example `minPowerShellCodeExamplesPercent`, `minMemberSummaryPercent`) and enforce via `failOnCoverage:true`.
