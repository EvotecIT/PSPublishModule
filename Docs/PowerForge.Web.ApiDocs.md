# PowerForge.Web API Docs Styling Guide

This guide documents the CSS hooks used by the built-in API docs templates and
the JavaScript behaviors they rely on. Use it when creating a custom theme or
overriding templates via `templateRoot`.

## Template variants

Two template modes are available:
- `simple` (minimal, single-column)
- `docs` (sidebar layout)

To customize templates, copy the embedded defaults from:
`PowerForge.Web/Assets/ApiDocs` into your own folder and pass it as
`templateRoot`. Use `index.html`/`type.html` for the simple layout and
`docs-index.html`/`docs-type.html` for the docs layout.

## Common tokens (all templates)

- `{{CSS}}` injects a stylesheet link or inline fallback CSS.
- `{{HEADER}}` / `{{FOOTER}}` are optional HTML fragments.

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
- `.sidebar-nav` – sidebar nav wrapper
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

## Type metadata

When assembly reflection is available, the generator emits:
- base type + implemented interfaces
- static/abstract/sealed flags
- attributes for types and members
- extension methods (shown under “Extension Methods”)
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

## XML docs vs reflection

If the XML documentation file is missing or empty, the generator falls back to
reflection (public types only). This still produces the API reference pages,
but summaries/remarks/parameter descriptions will be empty until `///` comments
are added.

`<see cref="...">` and `<seealso cref="...">` tags are converted into links when the
referenced type exists in the generated API docs.

## PowerShell help

Set `type: PowerShell` and point `help`/`helpPath` to a PowerShell help XML file
(for example `Module/en-US/MyModule-help.xml`) or a directory containing one.
Each command is treated as a “type” with parameter sets rendered as methods.

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
  "sourceRoot": "..",
  "sourceUrl": "https://github.com/YourOrg/YourRepo/blob/main/{path}#L{line}"
}
```

CLI:
```bash
powerforge-web apidocs --type csharp --xml ./bin/Release/net10.0/MyLib.xml --assembly ./bin/Release/net10.0/MyLib.dll --out ./_site/api --format both --template docs --css /css/api-docs.css --source-root .. --source-url "https://github.com/YourOrg/YourRepo/blob/main/{path}#L{line}"
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
  "css": "/css/api-docs.css"
}
```

CLI:
```bash
powerforge-web apidocs --type powershell --help-path ./Module/en-US/MyModule-help.xml --out ./_site/api --format both --template docs --css /css/api-docs.css
```

Notes:
- If `helpPath` points to a directory with multiple `*-help.xml` files, the first one is used.
- For deterministic output, point to a specific file.
