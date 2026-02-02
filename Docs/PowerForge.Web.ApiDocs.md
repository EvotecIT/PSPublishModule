# PowerForge.Web API Docs Styling Guide

This guide documents the CSS hooks used by the built-in API docs templates and
the JavaScript behaviors they rely on. Use it when creating a custom theme or
overriding templates via `templateRoot`.

## Template variants

Two template modes are available:
- `simple` (minimal, single-column)
- `docs` (sidebar layout)

Starter copies:
- `Assets/ApiDocs/Templates/default`
- `Assets/ApiDocs/Templates/sidebar-right` (docs layout with right sidebar)

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
- `.sidebar-nav` – sidebar nav wrapper
- `.nav-section` – nav section block
- `.nav-section-header` – section header
- `.nav-section-header.main-api` – main API section header
- `.nav-section-content` – section body
- `.nav-section-content.collapsed` – collapsed state
- `.chevron` / `.chevron.expanded` – collapse indicator
- `.type-item` – sidebar type link
- `.type-item.active` – active type link
- `.sidebar-footer` – footer block (optional)

URL behavior:
- Docs template uses clean URLs ending with `/` (for example: `/api/my-type/`).
- The generator writes `index.html` under `/api/<slug>/` so static servers render correctly.

Overview chips:
- `.api-overview` – overview wrapper
- `.type-chips` – chips container
- `.type-chip` – type chip link
- `.type-chip.<kind>` – kind class (`class`, `struct`, `enum`, `interface`, `delegate`)

## JavaScript expectations

`docs.js` expects:
- `#api-filter` input
- `.type-item` and `.type-chip` elements with `data-search` attribute
- `.nav-section-header` + `.nav-section-content` for collapse/expand
- `.sidebar-toggle` + `.sidebar-overlay` for mobile drawer

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
