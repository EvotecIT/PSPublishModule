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

If your site uses `Navigation.Profiles` (route/layout specific menus), set:
- `navContextPath` (defaults to `/`)
  - Set this when you want API pages to match a specific `Navigation.Profile` (for example `"/api/"`).
- optionally `navContextLayout` / `navContextCollection` / `navContextProject`
 so the generator can select the same profile your theme uses. For best results, point `nav` at `site-nav.json` (the nav export) when available.

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
- The "Back to Docs" link defaults to `/docs/` and can be overridden via `docsHomeUrl`.

Overview chips:
- `.api-overview` – overview wrapper
- `.type-chips` – chips container
- `.type-chip` – type chip link
- `.type-chip.<kind>` – kind class (`class`, `struct`, `enum`, `interface`, `delegate`)
- `.type-chip` includes `data-kind` and `data-namespace` for filtering

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

## PowerShell help

Set `type: PowerShell` and point `help`/`helpPath` to a PowerShell help XML file
(for example `Module/en-US/MyModule-help.xml`) or a directory containing one.
Each command is treated as a "type" with parameter sets rendered as methods.
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
- PowerShell fallback examples are enabled by default (`generatePowerShellFallbackExamples:true`) and can source snippets from `psExamplesPath` or discovered `Examples/` folders.
- In pipeline `apidocs` steps, you can gate quality with coverage thresholds (for example `minPowerShellCodeExamplesPercent`, `minMemberSummaryPercent`) and enforce via `failOnCoverage:true`.
