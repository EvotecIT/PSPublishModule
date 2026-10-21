# PowerForge.Web API Docs Styling Guide

This guide documents the CSS hooks used by the built-in API docs templates and
the JavaScript behaviors they rely on. Use it when creating a custom theme or
overriding templates via `templateRoot`.

## Documentation inputs and inheritance

Generated API indexes, type pages, PowerShell command pages and suite portals
inherit the site's `StructuredData` settings. Set `WebPage: true` for linked
page, website and publisher metadata; `Breadcrumbs: true` adds the reference
hierarchy. Each canonical page uses its own URL, title and description. The
generator uses `SiteConfigPath` (or a site configuration supplied as `NavJsonPath`)
and requires a public HTTP or HTTPS base URL. Disabling structured data in the
site configuration also disables it on generated references. Custom API templates
include this output through `{{HEAD_HTML}}`.

Source-link coverage measures whether readers can open the implementation from
the API reference. It is separate from test code coverage. Enum declarations and
fields use Roslyn syntax from checksum-verified portable PDB documents; methods
continue to use execution sequence points. Missing or changed source files do
not receive inferred declaration links.

The `apidocs` pipeline option `sourceMemberCoverageScope: "assembly"` measures
member source links against members declared by the documented assemblies.
Inherited members from other assemblies remain visible in the API reference,
but do not count against source links into the project's repository. The default
`"all"` retains the full denominator. Assembly scope requires matching DLL inputs;
XML-only references use `"all"`. The report records the chosen `scope` and
`excludedExternalMembers` under `source.members`; summary and example coverage
continue to include the full API. Use `--source-member-coverage-scope assembly`
with the CLI, or `SourceMemberCoverageScope = "assembly"` with the library.

For C# references, supply both the XML documentation and its matching assembly.
The assembly restricts output to the public API and supplies C# signatures.
For a project built from several assemblies, pass each XML file through `xmls`
and each matching DLL through `assemblies` in the `apidocs` pipeline step. The
equivalent library options are `XmlPaths` and `AssemblyPaths`; the existing
`xml`/`assembly` or `XmlPath`/`AssemblyPath` inputs remain supported. Each XML
file's `<assembly><name>` must match a supplied DLL. The generator combines the
public types into one reference and rejects an XML/DLL mismatch.
XML-only generation remains available, but reports a warning because XML member
records do not encode accessibility. Treat that warning as a generation limit
when publishing a reference from XML alone.

With an assembly, implicit `<inheritdoc/>` resolves documented base members and
implemented interface members, including generic interfaces, properties, and
events. Documentation supplied through `XmlPaths` shares one inheritance lookup,
so contract and implementation records can live in separate files. Supply the
inherited documentation with the inputs for contracts outside the local build.
Assembly-backed generation also discovers adjacent dependency XML and the
matching installed .NET reference-pack XML. Those records supply inherited
summaries without adding dependency or framework types to the reference.
This discovery uses each supplied assembly's path, including references built
with only the plural `assemblies` input. If framework XML is unavailable,
`System.Object` and `System.ValueType` overrides of `Equals`, `GetHashCode`, and
`ToString`, and implementations of `IEquatable<T>.Equals`, receive built-in
summary and return documentation. Available XML documentation takes precedence.
Pipeline caches account for its bytes and additional `xmls`/`xmlPaths` inputs.
The search continues past undocumented library candidates to documented library
ancestors or interfaces, without substituting a distant framework summary.
Imported XML records can themselves inherit from another base or interface;
generation follows those chains and reports unresolved imported library contracts.
Unresolved implicit inheritance on library contracts still produces a warning
and fails pipeline steps with `failOnWarnings` enabled (the CI default).
An explicit `cref` remains supported.

Assembly inspection reads a fresh snapshot on each generation and releases its
collectible load context afterward. Rebuilding a DLL at the same path updates
the reference without holding the build output open. Dependency types resolve
in the inspected assembly's own context, even when the documentation host loads
another copy of the same library.

Malformed C# XML or PowerShell help stops generation before replacing existing
reference files. Correct the input and rerun the generator; an empty reference
is not a successful recovery from invalid documentation.

## API and command index copy

Set `indexLead` and `indexDescription` on each `apidocs` pipeline step to write
the visible introduction and the search/social description independently. Batch
`inputs` and `entries` inherit these settings; child values override parent values
even when they use the other supported spelling:

```json
{
  "task": "apidocs",
  "title": "ChartForgeX API reference",
  "indexLead": "Build charts, dashboards, and visual stories with ChartForgeX.",
  "indexDescription": "Explore ChartForgeX types, signatures, and members for building charts, dashboards, and visual stories."
}
```

These are plain-text values; the generator HTML-encodes them. The CLI accepts
`--index-lead` and `--index-description`, and pipeline steps also accept the
hyphenated names. Custom index templates can use `{{INDEX_LEAD}}` and
`{{INDEX_DESCRIPTION}}`; `{{DESCRIPTION_META}}` includes the description meta tag.
An explicit description is preserved without automatic length padding or truncation.

Generated API and command descriptions share a 160-character limit across
`description`, `og:description`, and `twitter:description`. When text exceeds
the limit, the generator keeps the last complete sentence that fits, even when
that produces fewer than 120 characters. If no complete sentence fits, it ends
at a word boundary with an ellipsis (`…`), removing trailing commas, colons,
and dangling English articles, prepositions, or conjunctions. A single token
longer than the available space is shortened without splitting a Unicode scalar.

Without an explicit lead, C# references use assembly descriptions from either
`assembly` or `assemblies`, then a product-specific introduction using `packageId`,
the page title, or the assembly name. PowerShell references use the description
from an explicit or discovered module manifest, then a command introduction
naming the module inferred from the help file or `moduleName`.

Pre-rendered `headerHtml` and `footerHtml` fragments do not need navigation
placeholders. Fragments that contain navigation placeholders still require a
navigation configuration or supplied template-token values.

## Cross-package API search

API generation emits `search.json` for types. The `docs` and `sidebar` templates
also emit `members.json` for constructors, methods, properties, fields, events,
and extension methods. The simple template emits an empty member catalog because
it does not render member anchors. Member entries carry
the rendered anchor, signature, namespace, declaring type, and extension receiver.
Use `packageId` in an `apidocs` pipeline step, or `--package-id` in the CLI, when
the NuGet package name differs from the assembly name.
Long signature anchors have compact deterministic aliases for search and copy-link
actions. Their original HTML IDs remain valid for existing links.
When generic and nongeneric types would share a page slug, the nongeneric route
is retained and the generic route includes its arity. Noncolliding routes stay stable.

Set `Search.ApiRoots` to output-relative API directories in the site configuration.
The site builder imports both catalogs and links results to existing HTML pages
and member anchors. Its search manifest includes bounded query shards, grouped by
package. Small chunks and compact Bloom hints narrow cold searches; the builder
retires obsolete generated chunks after refreshing the manifest.
The shared visible and WebMCP search runtime selects shards for the query,
ranks exact names and aliases first, and supports `project` and `kind` filters in
`PowerForgeWebMcpSearch.search`. `PowerForgeWebMcpSearch.facets` lists package names
from the small `search/facets.json` artifact. Pass an `AbortSignal` as `signal` to
stop superseded searches before the next request batch. Query loading is limited
to 64 shards and 8 MiB of decoded JSON; a broader query rejects with
`SEARCH_QUERY_TOO_BROAD` so the interface can ask for a longer query or a package
filter. A worker parses and ranks shards from the same runtime asset URL, including
hashed URLs. Browsers that block workers use the same ranking on the main thread.
The runtime retains at most 16 MiB of raw shard data for subsequent queries.
The legacy bounded `search/index.json` remains available; use the manifest-aware
runtime to search catalogs larger than that single artifact's entry limit.

Type pages in catalogs larger than 200 types start with a small navigation menu.
Filtering loads the full type catalog on demand. The package index remains a
complete HTML navigation route when JavaScript or the catalog request is unavailable.

## Template variants

Two template modes are available:
- `simple` (minimal, single-column)
- `docs` (sidebar layout)

Sidebar position (`docs` template):
- Use `sidebar: "right"` (pipeline) or `--sidebar right` (CLI) to move the sidebar.
- Default is `left`.
Body class:
- `bodyClass` (pipeline) or `--body-class` (CLI) to set the `<body>` class.
- The generator retains `pf-api-docs` and adds `pf-api-docs--dotnet` or
  `pf-api-docs--powershell` alongside the configured classes. A suite current ID
  adds a normalized `pf-api-docs--suite-<id>` class.

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
- Generic labels retain C# parameter names, such as `Prompt<T>` and `Dictionary<TKey, TValue>`.
  Generic links include the complete type in their text and title. When short labels collide across
  namespaces, links receive namespace-qualified accessible names. Type identities and routes stay unchanged.
  PowerShell input, output, and parameter metadata also retain generic type arguments.
- `generateGitFreshness` (pipeline) / `--git-freshness` (CLI) enables opt-in git freshness metadata:
  - `gitFreshnessNewDays` / `--git-freshness-new-days` controls the `new` window (default `14`)
  - `gitFreshnessUpdatedDays` / `--git-freshness-updated-days` controls the `updated` window (default `90`)
  - emitted JSON adds `freshness.status`, `freshness.lastModifiedUtc`, `freshness.commitSha`, `freshness.ageDays`, and a normalized `freshness.sourcePath` (relative when possible; file-name fallback otherwise)
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
- `.member-returns` – combined return type and documentation (omitted for void)
- `.member-returns-type`, `.member-returns-text` – return block content
- `.member-subhead` – per-member detail label without a page heading
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
- `.type-usage` – reverse usage section for inferred API relationships
- `.type-usage-summary` – reverse usage lead paragraph
- `.usage-group` – reverse usage category card
- `.usage-list` – reverse usage list
- `.usage-item` – reverse usage entry
- `.usage-kind` – reverse usage relationship badge
- `.usage-link` – reverse usage member link
- `.type-related-content` – curated guides/samples section on a type page
- `.related-content-list` – curated guides/samples list
- `.related-content-item` – curated guides/samples card
- `.related-content-kind` – curated guides/samples badge
- `.member-related-content` – curated guides/samples block inside a member card
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
- reverse usage relationships for documented types (`usage` in JSON and `Usage` in docs pages), grouped into:
  - `returnedOrExposedBy` for methods/properties/fields/events that surface the type
  - `acceptedByParameters` for constructors/methods/syntax entries that accept the type
- curated related content (`relatedContent` in JSON and `Guides & Samples` in docs-template HTML) from manifest entries that target:
  - type ids like `T:Namespace.Type`
  - member ids like `M:Namespace.Type.Run(System.String)` or `P:Namespace.Type.Name`
  - xref-style aliases that already resolve through the generated symbol map
- extension methods (shown under "Extension Methods")
- constructors are rendered in their own section and overloads are grouped by name
- optional source links (when `sourceRoot` + `sourceUrl` are set and PDBs exist)

`search.js` (simple template) expects:
- `#api-search` input
- `#api-results` container
- `index.json` + `search.json` present under `/api`

If you change class names or IDs, provide your own JS via `docsScript` /
`searchScript`.

## Default theme and CSS variables

The shared theme styles API indexes, classes, structs, enums, PowerShell commands
and suite portals. It provides a rail tree, compact filters, member rows with
boxed signatures, a facts list with hairline rules, and a slide-in phone sidebar.
Only the type tree scrolls inside the desktop sidebar. The table of contents sits
in a sticky right rail from 1240px; below that width it is hidden. The navigation
drawer replaces the desktop sidebar at 960px and below. These two breakpoints
are structural CSS media queries, rather than custom properties.

Without `css`, generation inlines the theme. Each HTML output directory also
receives `powerforge-api.css`, containing the same stylesheet. Load that generated
asset followed by your site's token map to reuse the theme without keeping a
copy of its selectors:

```json
{
  "task": "apidocs",
  "template": "docs",
  "out": "./_site/api",
  "baseUrl": "/api",
  "css": "/css/site-tokens.css,/api/powerforge-api.css,/css/api-theme.css"
}
```

Use the corresponding output URL for each reference. A PowerShell reference at
`/powershell/commands` emits `/powershell/commands/powerforge-api.css`. Each output
also supports `templateRoot/fallback.css` as the source of its inline and emitted
theme. Keep these generated assets with their pages when deploying a reference.

Providing `css` still replaces the inline theme. Existing sites that supply a
complete stylesheet keep that behavior. The small `controls.css` asset is always
inlined to keep screen-reader labels hidden, icons inside their controls and
grouped status links free of offsets from older themes. It contains control
layout, without a color palette or page composition. `css` accepts a single href or a comma-
or whitespace-separated list, rendered in the supplied order. To migrate a
copied stylesheet, include the generated theme explicitly, then the token map;
remove only the site's duplicated layout rules after reviewing the result.
This does not require a separate theme package.

Map properties on `body.pf-api-docs`. Palette defaults use zero-specificity root
selectors; derived defaults such as kind colors and the heading font resolve on
the API body, so they follow body-level accent and font overrides. Components
use `:where(body.pf-api-docs)` so ordinary site rules can override
them. Light and dark selectors assign variables only; they do not restyle each
component. The default is dark. Set `data-theme="light"` or `data-theme="dark"`
on `html`; an explicit `data-theme="light"` on the API body is also supported.
The host owns its theme preference and toggle. Map theme-dependent site tokens
on the body once, so switching the host theme updates every reference component.

For example, a site with an existing token layer can use this small map. The
`--site-*` names below represent that site's own tokens; define them in the site's
token stylesheet or substitute its actual names.

```css
body.pf-api-docs {
  --pf-bg: var(--site-page);
  --pf-bg-alt: var(--site-sunken);
  --pf-panel: var(--site-panel);
  --pf-card-bg: var(--site-surface);
  --pf-ink: var(--site-ink);
  --pf-muted: var(--site-muted);
  --pf-dim: var(--site-dim);
  --pf-border: var(--site-line);
  --pf-border-strong: var(--site-line-strong);
  --pf-accent: var(--site-link);
  --pf-code-bg: var(--site-code-bg);
  --pf-code-ink: var(--site-code-ink);
  --pf-font-body: var(--site-sans);
  --pf-font-heading: var(--site-display);
  --pf-font-mono: var(--site-mono);
  --pf-header-offset: var(--site-header-height);
  --pf-page-width: var(--site-width);
  --pf-page-gutter: var(--site-gutter);
  --pf-radius: var(--site-radius);
  --pf-kind-class: var(--site-link);
  --pf-kind-struct: var(--site-warning);
  --pf-kind-enum: var(--site-mark);
  --pf-kind-interface: var(--site-success);
  --pf-kind-delegate: var(--site-success);
  --pf-kind-command: var(--site-link);
}
```

All properties are optional. Override the palette, fonts and header offset first;
keep the spacing and size defaults unless the site's composition needs more.
No font download is required by the shared theme.

| Properties | Meaning and defaults |
| --- | --- |
| `--pf-bg`, `--pf-bg-alt`, `--pf-panel`, `--pf-card-bg` | Page and surface palette. Dark defaults: `#111318`, `#181b22`, `#1c2029`, `#181b22`; light: `#fcfcfd`, `#f3f4f7`, `#eceef3`, `#f7f8fa`. `panel` and `card-bg` remain available to site extensions. |
| `--pf-ink`, `--pf-muted`, `--pf-dim` | Main, secondary and metadata text. Dark: `#ecedf1`, `#b6bdcc`, `#9ca7ba`; light: `#202530`, `#515b6d`, `#626d80`. |
| `--pf-border`, `--pf-border-strong` | Hairlines and control boundaries; also scrollbar colors. Dark: `#343b49`, `#515d71`; light: `#dce0e8`, `#aab3c3`. |
| `--pf-accent`, `--pf-accent-soft` | Links, selected controls and current tree item. Dark accent `#c4b5fd`, light `#6743b5`; soft is a 12% mix with transparent. |
| `--pf-code-bg`, `--pf-code-ink` | Signature and example palette. Dark: `#161920`, `#e2e8f0`; light: `#f3f4f7`, `#253047`. |
| `--pf-overlay`, `--pf-shadow`, `--pf-color-scheme` | Drawer scrim, popup shadow and native control scheme. Dark: `#080b12aa`, `0 12px 32px #0004`, `dark`; light: `#10182777`, `0 12px 32px #1820331a`, `light`. |
| `--pf-kind-class`, `--pf-kind-struct`, `--pf-kind-enum`, `--pf-kind-interface`, `--pf-kind-delegate`, `--pf-kind-command` | Kind glyph and badge colors; each defaults to the accent. Component-local `--pf-kind` selects the corresponding color. |
| `--pf-font-body`, `--pf-font-heading`, `--pf-font-mono` | System sans, the body font, and system monospace, respectively. |
| `--pf-text-xs`, `--pf-text-sm`, `--pf-text-code`, `--pf-text-base`, `--pf-text-lead` | Text sizes: `.75rem`, `.8125rem`, `.84375rem`, `.9375rem`, `1.0625rem`. |
| `--pf-text-h3`, `--pf-text-h2`, `--pf-text-h1` | Heading sizes: `1rem`, `1.25rem`, `clamp(1.75rem, 1.2rem + 1.6vw, 2.5rem)`. |
| `--pf-leading`, `--pf-leading-tight` | Body and heading line heights: `1.65`, `1.3`. |
| `--pf-weight`, `--pf-weight-medium`, `--pf-weight-heading` | Body, emphasis and heading weights: `400`, `600`, `700`. |
| `--pf-space-1` through `--pf-space-8` | Spacing scale: `.25rem`, `.5rem`, `.75rem`, `1rem`, `1.5rem`, `2rem`, `2.5rem`, `3rem`. |
| `--pf-radius`, `--pf-radius-sm`, `--pf-radius-pill` | Control, inline and chip corners: `.5rem`, `.25rem`, `999px`. |
| `--pf-line`, `--pf-focus-width` | Hairline and focus/current-item widths: `1px`, `2px`. |
| `--pf-control-height`, `--pf-chip-height` | Search/select height `2.5rem`; compact chip height `1.625rem` (26px at the default root font size). |
| `--pf-icon-size`, `--pf-glyph-size` | Search/action icons `1rem`; kind glyph boxes `1.125rem`. |
| `--pf-inline-code-padding`, `--pf-inline-code-size`, `--pf-underline-offset` | Inline code inset `.1em .35em`, relative size `.9em`, link underline offset `.18em`. |
| `--pf-page-width`, `--pf-page-gutter`, `--pf-layout-gap` | Maximum frame `96rem`; page inset `clamp(1rem, 3vw, 2.5rem)`; column gap `clamp(1.5rem, 3vw, 3rem)`. |
| `--pf-sidebar-width`, `--pf-toc-width`, `--pf-header-offset` | Tree width `17rem`, right rail `12rem`, host sticky-header offset `0px`. |
| `--pf-measure`, `--pf-facts-label-width` | Summary reading width `72ch`; facts label column `7.5rem`. |
| `--pf-search-width`, `--pf-card-min-width`, `--pf-drawer-width` | Member search flex basis `13.75rem`, index card minimum `13rem`, phone drawer maximum `21rem`. |
| `--pf-syntax-indent` | PowerShell parameter-group indentation `2ch`; continuation lines hang a further `2ch` inside each group. |
| `--pf-combobox-max-height`, `--pf-scrollbar-size`, `--pf-motion` | Namespace popup maximum `18rem` (also bounded to 40dvh), scrollbar `.5rem`, transition `.18s`. Reduced-motion preferences disable transitions. |
| `--pf-z-menu`, `--pf-z-overlay`, `--pf-z-drawer` | Popup, scrim and drawer stacking levels: `70`, `1001`, `1002`; map these if host chrome uses a different stacking scheme. |

## Template semantics and control state

Type headers place the kind, name and namespace in `.type-title-row`, with a
`.type-namespace` paragraph after the heading. The breadcrumb supplies the
reference context, so type headers do not repeat an API Reference eyebrow.
Facts use `dl.type-meta`, `dt.type-meta-label` and `dd` inside the main content
column, before the lead paragraph. Stacked values align with their labels.
Existing row classes remain available to site styles. Source actions retain `.type-source-action`
and add `.type-edit-action` or `.type-view-source-action`.

Sidebar and member searches have associated screen-reader labels and positioned
icon spans. `.sidebar-status` groups the count, browse-all link and tree tools.
Kind buttons expose `aria-pressed`; their groups have accessible names. Script
updates the pressed state when filters change, reset or restore from navigation.
Member section toggles expose `aria-expanded` and `aria-controls` for their body,
including after expand/collapse-all or saved-state restoration.

Member detail labels use `.member-subhead` paragraphs rather than page headings.
One `.member-returns` block combines `.member-returns-type` and
`.member-returns-text`; void methods have no returns block. .NET parameter order
is conveyed by the signature. PowerShell positional and pipeline metadata have
`.param-meta-chip--position` and `.param-meta-chip--pipeline` hooks.
Bodies expose `.pf-api-docs--dotnet` or `.pf-api-docs--powershell` alongside
`.pf-api-docs`, so sites can style either language without inspecting descendants.

PowerShell syntax uses `.powershell-syntax`, with a `.syntax-command` span and
one `.syntax-parameter` span per parameter group. Groups begin on separate lines;
long groups wrap with a hanging indent. The Description section uses
`.command-description` as ordinary content. When its text starts with the synopsis,
the matching prefix is removed after whitespace and case normalization.
The syntax block identifies its language through `data-language`; it omits code
highlighter classes so host highlighters preserve its parameter-group markup.

C# signatures and displayed type references use keyword aliases such as `void`,
`string` and `int?`. Documentation IDs and cross-reference targets retain their
qualified identities, and similarly named library types keep their names.

Example titles use sentence case. Example provenance badges are hidden by
default; set `showExampleOrigins: true` on the `apidocs` pipeline step,
`--show-example-origins` on the CLI or `ShowExampleOrigins = true` on
`WebApiDocsOptions` for help-authoring views. Provenance remains in JSON output.

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
- `<media kind="image|video|terminal|story|link" ... />`

Use `kind="story"` for a complete code-to-result presentation. `src` points at animated SVG/GIF/APNG and `poster` points at the completed PNG outcome. Generated API pages automatically select the completed PNG under `prefers-reduced-motion`:

```xml
<media kind="story"
       src="/stories/chart-five-lines/demo.svg"
       poster="/stories/chart-five-lines/demo.png"
       title="Create a chart in five lines"
       alt="Five lines of source followed by the rendered chart."
       caption="The completed scene contains the promised chart." />
```

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

### Command pages

PowerShell references render as a command reference rather than a .NET type page:

- Section order follows Get-Help and Microsoft Learn: syntax (one signature per
  parameter set, positional names written as `[-Path] <String>`), description,
  examples, parameters, common parameters, outputs, related links.
- Parameters are listed once for the command (`section#parameters`,
  `dl.param-list--command`, anchors `#parameter-<name>`). Chips show `required`,
  `required in <set>` or `optional`, the position only when positional, pipeline
  input only when accepted, aliases, and `sets:` when a parameter is not in every set.
  Syntax cards no longer repeat the parameter list or outputs, and the member
  filter toolbar is omitted.
- The page meta says `Module` instead of `Namespace`. The module name comes from
  `moduleName` (pipeline) / `--module-name` (CLI); otherwise from a module manifest
  or `packageId` whose name matches the help file case-insensitively; otherwise
  from the help file name.
- Type pages drop the eyebrow above the title because the breadcrumb already names
  the section. The sidebar hides kind and module filters that cannot narrow
  anything (one kind, one module) and labels them `Command type` / `Module`.
- Example introductions that are only a prompt (`PS>`, `PS C:\>`) are dropped.
  Example code keeps relative indentation but loses indentation inherited from the
  XML layout (including the common case where the first line was trimmed and the
  rest kept the XML indent). A prompt at the start of example code renders as
  `span.example-code__prompt` outside `<code>`, so copy actions get only the command.

`sectionLabel` (pipeline) / `--section-label` (CLI) sets the short label used in
the breadcrumb root, sidebar heading and overview eyebrow. It defaults to
`Commands` for PowerShell and `API Reference` for C#. Templates can use the same
text through the `{{SECTION_LABEL}}` token (for example in the mobile sidebar toggle).

Example code blocks in both PowerShell and C# pages render as
`<pre class="language-<lang> example-code-block"><code class="language-<lang>">`
with the code placed directly between the tags.

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

### Source fidelity

Use `{revision}` in `sourceUrl` to bind links to the Git HEAD resolved from
`sourceRoot`, for example `https://github.com/YourOrg/YourRepo/blob/{revision}/{path}#L{line}`.
Generated source records contain `revision` and `workingTreeChanged`; type pages
label tracked edits that differ from that revision. Revision URLs are omitted with
a warning when the Git revision cannot be resolved. Pipeline caching does not skip
API generation that uses this token.

Portable PDB paths mapped beneath `/_/` resolve relative to the source root.
Nullable reference annotations, nested generic arguments, and array ranks appear
in signatures while existing member anchor identities remain stable. Duplicate
partial-class XML entries merge complementary documentation; the first nonempty
summary wins, and conflicting summaries produce a warning. Keep the authoritative
type summary on one partial declaration.

### PowerShell module help

Pipeline step:
```json
{
  "task": "apidocs",
  "type": "PowerShell",
  "help": "./Module/en-US/MyModule-help.xml",
  "out": "./_site/api",
  "moduleName": "MyModule",
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
- Curated related-content manifests can attach authored guides/samples to API types and members:
  - CLI: `--related-content-manifest <file>` or `--related-content-manifests <list>`
  - Pipeline: `relatedContentManifest`, `related-content-manifest`, `relatedContentManifests`, or `related-content-manifests`
  - matching prefers stable xref-style ids like `T:Namespace.Type` and `M:Namespace.Type.Run(System.String)`
  - aliases from generated xref maps also work, but explicit ids are the most future-proof option
  - unresolved manifest paths or unresolved targets emit `[PFWEB.APIDOCS.RELATED]` warnings

### Curated related-content manifest

Use a JSON manifest when examples/guides live outside XML docs or PowerShell help but should still be discoverable from API detail pages.

```json
{
  "entries": [
    {
      "title": "Styling axis titles",
      "url": "/docs/charts/axis-titles/",
      "summary": "Walkthrough for configuring [[cref:T:ExampleCharts.AxisTitle]].",
      "kind": "guide",
      "targets": [
        "T:ExampleCharts.AxisTitle",
        "M:ExampleCharts.AxisTitle.SetText(System.String)"
      ]
    }
  ]
}
```

Notes:
- Supported root shapes: top-level array, or object with `entries` / `items`.
- Supported entry aliases: `title`/`name`, `url`/`href`, `target`/`targets`, `uid`/`uids`, `xref`/`xrefs`.
- Supported kinds are open-ended; PowerForge normalizes common values like `guide`, `sample`, `tutorial`, `walkthrough`, `reference`, and `recipe`.
- In docs-template output, type-level matches render as `Guides & Samples`; member-level matches render inside the relevant member card.
- In per-type JSON, matches are emitted under `relatedContent.entries[]` and `relatedContent.members[]`, with member copies also repeated on the matching member object for easier client consumption.
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
- Validation JSON normalizes help/example/artifact paths when possible, so reports do not leak build-agent absolute paths. Execution entries also include captured `executionStdOut`, `executionStdErr`, and `executionSkippedReason` when available.
- When execution is enabled, report writing also emits reusable transcript artifacts under a sibling `powershell-example-validation-artifacts/` folder and records each path as `executionArtifactPath`.
- When generated docs also receive that validation result, successful imported PowerShell examples can surface those transcript artifacts as terminal-style example media links in the rendered API reference.
- Imported PowerShell example scripts can also ship richer playback sidecars next to the `.ps1` file: matching `.cast` / `.asciinema` files are staged into API output automatically, and matching `.png` / `.jpg` / `.jpeg` / `.webp` files are used as poster art when present.
- When imported terminal media exists, PowerForge also writes `powershell-example-media-manifest.json` under the API output root so sites and CI jobs can diff capture history, health flags, and staged media linkage per command.
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
- Reverse usage is intentionally inferential, not prescriptive:
  - it answers “where does this type show up in the public surface?”
  - it does not replace authored conceptual docs or curated examples for “when should I choose this pattern?”
- For fluent/configuration-heavy libraries, usage plus examples are complementary:
  - usage helps a reader discover owning/related types and entry points
  - examples/remarks explain intent and sequence
- Coverage reports now also track curated related-content relevance:
  - `types.relatedContent`
  - `members.relatedContent`
  - `types.quickStartRelatedContent`
  - `types.quickStartMissingRelatedContent`
- Pipeline coverage thresholds can now gate curated related-content quality with:
  - `minTypeRelatedContentPercent`
  - `minMemberRelatedContentPercent`
  - `minQuickStartRelatedContentPercent`
  - `maxQuickStartMissingRelatedContentCount`
- When `quickStartTypes` are configured and some of those types still lack curated related content, API generation emits `[PFWEB.APIDOCS.RELATED]` warnings so CI can flag “important API, but no walkthrough” gaps early.
- API docs now support first-class suite metadata for multi-project API portals:
  - generator options / pipeline fields:
    - `suiteTitle`
    - `suiteCurrentId`
    - `suiteHomeUrl`
    - `suiteHomeLabel`
    - `suiteSearchUrl`
    - `suiteXrefMapUrl`
    - `suiteCoverageUrl`
    - `suiteEntries`
  - per-type and index JSON now emit `suite` metadata, and docs-template HTML renders project/module switchers when more than one suite entry exists.
  - when `suiteSearchUrl` is present, docs-template overview pages now render a built-in “Search across suite” experience powered by the shared suite search artifact.
- Batch `apidocs` can infer suite entries automatically when a step contains multiple `inputs`/`entries` with distinct `baseUrl` values.
  - optional parent-step overrides like `suiteTitle` and `suiteHomeUrl` are then applied consistently to every generated API.
- `project-apidocs` now auto-injects suite entries across all prepared project APIs and writes `api-suite.json` under the `outRoot` by default.
  - this gives multi-project API sites one reusable API-suite manifest plus in-API project switching without maintaining a second hand-authored registry.
- `project-apidocs` now also emits suite-root aggregate artifacts by default when more than one project API is generated:
  - `api-suite-search.json`
  - `api-suite-xrefmap.json`
  - `api-suite-coverage.json`
  - `api-suite-related-content.json`
  - `api-suite-narrative.json`
  - `api-suite/index.html`
  - `api-suite/index.json`
  - `api-suite.json` includes an `artifacts` object with relative paths to those files when they were produced.
  - merged suite search items also carry `suiteEntryId` / `suiteEntryLabel` so future suite search UIs can group/filter by project.
  - generated per-project APIs also get relative suite artifact URLs injected automatically, so the built-in suite search widget works without site-specific scripting.
  - the generated `api-suite/` route gives the suite a real landing/search page instead of only embedding suite search inside each project API.
  - when `suiteNarrativeManifest` / `suiteNarrativeManifests` are configured on `project-apidocs`, PowerForge now normalizes those manifests into `api-suite-narrative.json`.
  - generated per-project APIs and the suite portal receive `suiteNarrativeUrl` / `narrativeUrl` metadata so sites can treat suite onboarding guidance as a first-class artifact, not a theme-specific convention.
  - the generated suite portal now reads `api-suite-narrative.json` and renders a built-in `Start Here` section for ordered onboarding/workflow links across projects.
  - the built-in suite search UI now also supports filtering by project/module directly in the portal.
  - the generated suite portal now reads `api-suite-coverage.json` and surfaces a coverage summary widget for quick-start guidance gaps and top-level counts.
  - the generated suite portal now also reads `api-suite-related-content.json` and renders a built-in `Guides & Samples` section so the suite can surface curated onboarding or walkthrough content across projects.
  - when `suiteHomeUrl` is omitted, `project-apidocs` now uses the generated suite landing route as the default suite home link.
- `project-apidocs` can now also consume project-specific API-doc relevance hints from catalog entries via an `apiDocs` / `api-docs` object.
  - supported fields:
    - `quickStartTypes`
    - `relatedContentManifest`
    - `relatedContentManifests`
  - those overrides are applied per generated project API before suite aggregation, so multi-project portals can define “important entry points” and curated guides per project instead of forcing one global list across every API.
- `project-apidocs` now supports explicit suite-level coverage gating through a `suiteCoverage` object plus `suiteFailOnCoverage`.
  - the nested `suiteCoverage` object uses the same threshold names as `apidocs` coverage gating, for example:
    - `minQuickStartRelatedContentPercent`
    - `maxQuickStartMissingRelatedContentCount`
    - `minTypeRelatedContentPercent`
  - this evaluates the merged `api-suite-coverage.json`, not the individual per-project `coverage.json` files.
  - `project-apidocs` also supports:
    - `generateSuiteLandingPage`
    - `suiteLandingUrl`
    - `suiteNarrativeManifest`
    - `suiteNarrativeManifests`
  - the landing page is generated under `outRoot/api-suite/` by default and reuses the built-in API docs styles/scripts.
- `project-apidocs` can now also gate suite onboarding quality through a `suiteNarrative` object plus `suiteFailOnNarrative`.
  - supported thresholds:
    - `minSectionCount`
    - `minItemCount`
    - `requireSummary`
    - `minSuiteEntryCoveragePercent`
    - `maxUncoveredSuiteEntryCount`
  - this evaluates the normalized `api-suite-narrative.json` artifact, so CI checks authored suite guidance the same way the portal consumes it.
- When `project-apidocs` generates a multi-project suite without the recommended starter wiring, it now emits `[PFWEB.APIDOCS.SUITE]` recommendations and summarizes them in the step message.
  - current recommendations focus on:
    - missing `suiteNarrativeManifest(s)`
    - missing or partial project-level `apiDocs.quickStartTypes`
    - missing or partial project-level `apiDocs.relatedContentManifest(s)`
    - untouched suite scaffold state (empty catalog with scaffold starter templates still present)
    - leaked scaffold placeholders such as `sample-project` or `sample-project-api-guides.json`
  - this is intentionally advisory, not failing, so teams can adopt the suite contract incrementally.
- `powerforge-web scaffold --starter-profile multi-project-api-suite` now creates the recommended starter shape directly:
  - `data/projects/catalog.json`
  - `data/projects/catalog.project-template.json`
  - `data/projects/api-suite-narrative.json`
  - `data/projects/sample-project-api-guides.json`
  - `content/docs/projects/api-guide-template.md`
  - `projects-sources/README.md`
  - theme `api-header` / `api-footer` partials plus `assets/api.css`
  - a placeholder `/projects/api-suite/` page that `project-apidocs` can replace once real project APIs exist
  - optional `--suite-project-slug` / `--suite-project-name` / `--suite-project-surface` can now materialize a real first project entry, guide page, related-content manifest, and project source staging folder during scaffold time
  - seeded projects now also get surface-specific source folders that match `project-apidocs` discovery (`projects-sources/<slug>/powershell/` or `projects-sources/<slug>/dotnet/`)
  - seeded source folders now include safe `templates/` handoff files whose names intentionally do not match real discovery patterns, so maintainers can copy/replace them without accidentally generating placeholder APIs
  - seeded source folders now also include promotion helpers (`promote-from-templates.ps1` for PowerShell, `promote-from-build.ps1` for .NET) so teams have an engine-shaped path from starter files to real inputs
- Pipeline coverage thresholds can also gate provenance-specific example quality via `minPowerShellAuthoredHelpCodeExamplesPercent` and `minPowerShellImportedScriptCodeExamplesPercent`.
- Pipeline coverage thresholds can also gate playback richness via `minPowerShellImportedScriptPlaybackMediaPercent`, `minPowerShellImportedScriptPlaybackMediaWithPosterPercent`, and `maxPowerShellImportedScriptPlaybackMediaWithoutPosterCount`.
- Pipeline coverage thresholds can also gate playback asset health via `maxPowerShellImportedScriptPlaybackMediaUnsupportedSidecarCount`, `maxPowerShellImportedScriptPlaybackMediaOversizedAssetCount`, and `maxPowerShellImportedScriptPlaybackMediaStaleAssetCount`.
- Pipeline coverage thresholds can now gate generated-fallback-only quality via `maxPowerShellGeneratedFallbackOnlyExamplePercent` or `maxPowerShellGeneratedFallbackOnlyExampleCount`.
- In pipeline `apidocs` steps, you can gate quality with coverage thresholds (for example `minPowerShellCodeExamplesPercent`, `minMemberSummaryPercent`) and enforce via `failOnCoverage:true`.
