# PowerForge.Web Content Specification (Draft)

This document describes the recommended content model for PowerForge.Web.
It is designed to be reusable across many projects (products, docs, blogs).

## Folder structure (recommended)
```
site.json
content/
  pages/
  docs/
  blog/
  snippets/
data/
projects/
  ProjectA/
    project.json
    content/
      pages/
      docs/
      blog/
    data/
static/
  images/
  data/
  vendor/
themes/
  base/
  nova/
shared/
  snippets/
  data/
```

## Markdown + front matter
All content files are Markdown with optional front matter:
```
---
title: Getting Started
description: Quick start guide for ProductX.
date: 2026-01-26
tags:
  - docs
  - quickstart
aliases:
  - /getting-started/
layout: docs
template: page
outputs:
  - html
  - json
meta.eyebrow: Documentation
---

# Getting Started
```

Supported fields:
- `title`, `description`, `date`
- `tags`, `aliases`
- `slug`, `order`, `draft`
- `layout`, `template`, `collection`
- `canonical`, `editpath`
- `meta.*` (custom data exposed to templates)
  - `meta.raw_html` (skip markdown rendering, treat content as HTML)
  - `meta.render` = `html` (skip markdown rendering)
  - `meta.format` = `html` (skip markdown rendering)
  - `meta.body_class` (optional body class)
  - `meta.social` (enable social tags for this page when configured)
  - `meta.structured_data` (enable JSON‑LD breadcrumbs for this page when configured)
  - `meta.head_html` (raw HTML appended into `<head>`)
  - `meta.head_file` (path to a file appended into `<head>`)
  - `meta.extra_css` (raw CSS link tags appended into `<head>`)
  - `meta.extra_scripts` (raw script tags appended before `</body>`)
  - `meta.extra_scripts_file` (path to a file appended before `</body>`)

### Front matter resolution rules
PowerForge.Web resolves missing values using simple defaults:
- `title`: `front matter title` → first `# H1` → filename
- `slug`: `front matter slug` → filename
- `description`: `front matter description` (no automatic fallback)
- `date`: `front matter date` (no git/mtime fallback yet)

### Route + slug rules
Routes are built from `Collection.Output` + `slug`:
- `index.md` or `slug: index` maps to the collection root
- `slug` can include slashes to create nested paths

Examples (collection output = `/docs`):
- `getting-started.md` → `/docs/getting-started/`
- `index.md` → `/docs/`
- `slug: guides/install` → `/docs/guides/install/`

### Folder-driven routes
When files live inside folders, the folder path becomes part of the route:
```
content/docs/guides/install.md -> /docs/guides/install/
```

### Section index pages (`_index.md`)
If a folder contains `_index.md`, it becomes a **section/list page** for that folder.
The route is the folder path:
```
content/docs/guides/_index.md -> /docs/guides/
```

### Page bundles (`index.md`)
If a folder contains `index.md` and no other markdown files, it becomes a **leaf bundle**.
All non‑markdown files in that folder are copied to the page output folder:
```
content/docs/getting-started/index.md
content/docs/getting-started/hero.png
```
Output:
```
/docs/getting-started/index.html
/docs/getting-started/hero.png
```

Trailing slash behavior is controlled by `site.json` → `TrailingSlash`:
- `always` / `never` / `ignore`

## Collections
Collections map markdown inputs to output routes:
```json
{
  "Name": "docs",
  "Input": "content/docs",
  "Output": "/docs",
  "DefaultLayout": "docs",
  "ListLayout": "docs-list",
  "Include": ["*.md", "**/*.md"]
}
```

`ListLayout` is used for `_index.md` section pages.

### TOC overrides
Collections can define a table of contents file (DocFX‑style):
```json
{
  "TocFile": "content/docs/toc.json"
}
```
If present, this TOC drives navigation instead of folder structure.
Supported formats: `toc.json`, `toc.yml`, `toc.yaml`.

## Data files
JSON files under `data/` become `data.<fileName>` in Scriban.
```
data/
  hero.json        -> data.hero
  sections.json    -> data.sections
  showcase.json    -> data.showcase
```

Use data for repeated UI blocks and long lists.

### Markdown-friendly data fields
Data keys ending with `_md` or `_markdown` are automatically rendered as HTML.
The rendered value is exposed under the base key (suffix removed) if the base key is missing.
Examples:
```
{
  "answer_md": "This **supports Markdown**.",
  "paragraphs_md": ["First paragraph.", "Second paragraph."]
}
```
Becomes:
```
answer: "<p>This <strong>supports Markdown</strong>.</p>"
paragraphs: ["<p>First paragraph.</p>", "<p>Second paragraph.</p>"]
```

### Normalized data shapes (FAQ/Showcase/Pricing/Benchmarks)
PowerForge.Web normalizes common content data so JSON stays portable and forgiving:
- `faq.sections[*].items[*].answer` is always treated as a **list** of blocks (strings), even if provided as a single string.
- `pricing.note.paragraphs`, `pricing.cards[*].features`, `benchmarks.about.cards[*].paragraphs`, and `benchmarks.notes.items`
  are normalized to **lists**.
- Common camelCase keys are aliased to snake_case for compatibility with existing templates:
  - `iconSvg` → `icon_svg`
  - `iconClass` → `icon_class`
  - `amountClass` → `amount_class`
  - `thumbLabel` → `thumb_label`
  - `thumbSrc` → `thumb_src`
  - `dotStyle` → `dot_style`

Recommended shapes:

FAQ (`data/faq.json`):
```
{
  "hero": { "label": "Support", "title": "FAQ", "description": "..." },
  "sections": [
    {
      "title": "General",
      "items": [
        { "id": "what-is", "question": "What is X?", "answer": ["<p>...</p>"] }
      ]
    }
  ]
}
```

Showcase (`data/showcase.json`):
```
{
  "hero": { "label": "Built with X", "title": "Showcase", "description": "..." },
  "cards": [
    {
      "id": "app1",
      "title": "App One",
      "icon_svg": "<svg>...</svg>",
      "meta": [{ "label": ".NET 8" }],
      "features": ["Fast", "Portable"]
    }
  ]
}
```

Pricing (`data/pricing.json`):
```
{
  "hero": { "label": "Pricing", "title": "Simple pricing", "description": "..." },
  "cards": [
    {
      "title": "Free",
      "amount": "Free",
      "features": ["All features"],
      "cta": { "label": "Install", "href": "https://..." }
    }
  ],
  "note": { "title": "Why support?", "paragraphs": ["<p>...</p>"] }
}
```

Benchmarks (`data/benchmarks.json`):
```
{
  "hero": { "title": "Benchmarks", "intro": "..." },
  "about": { "cards": [{ "title": "Quick vs Full", "paragraphs": ["<p>...</p>"] }] },
  "notes": { "items": ["Note 1", "Note 2"] }
}
```

## Dual input model (JSON or Markdown)
Some pages can be rendered from JSON when present, while still supporting normal Markdown.
Enable this via front matter:
```
meta.data_shortcode: faq
meta.data_path: faq
meta.data_mode: override
```

Behavior:
- If `data_path` exists in `data/`, the shortcode renders and overrides the body.
- If the data does not exist, the markdown body is used.
- `meta.data_mode` supports: `override` (default), `prepend`, `append`.

This is intended for FAQ/Showcase/Benchmarks/Pricing pages where JSON may be preferred.
Shortcodes available by default: `faq`, `showcase`, `benchmarks`, `pricing`, `cards`, `metrics`, `edit-link`.

## Static assets
Use `StaticAssets` in `site.json` to copy folders/files to the output root.
This is intended for images, data feeds, vendor files, or other assets
that should live at `/images`, `/data`, `/vendor`, etc.

```json
{
  "StaticAssets": [
    { "Source": "static" }
  ]
}
```

Each entry supports:
- `Source`: path relative to the site root (file or folder)
- `Destination`: optional output subfolder (defaults to `/`)

## Shortcodes
Shortcodes keep markdown clean while rendering complex blocks.
Examples:
```
{{< cards data="features" >}}
{{< metrics data="stats" >}}
{{< showcase data="showcase" >}}
{{< app src="/playground/" label="Launch Playground" title="Playground" >}}
{{< edit-link >}}
```

Shortcodes can be implemented as:
- code handlers (C#), or
- theme partials `partials/shortcodes/<name>.html`

## Navigation
Navigation lives in `site.json` under `Navigation.Menus` with optional `Navigation.Actions`.
Templates receive a computed `navigation` object with active states.

`Navigation.Actions` is for header buttons/icons (theme toggles, GitHub, CTA).
These items can be links or buttons and are exposed as `navigation.actions`.

### Auto navigation (folder‑driven)
You can also generate navigation from folder structure:
```json
{
  "Navigation": {
    "Auto": [
      { "Collection": "docs", "Menu": "docs", "MaxDepth": 3 }
    ]
  }
}
```

### Front matter nav overrides
Use `meta.nav.*` to override auto‑nav labels:
```
meta.nav.title: Install
meta.nav.weight: 20
meta.nav.hidden: true
```

### Actions (header buttons/icons)
Example actions (link + button):
```json
{
  "Navigation": {
    "Actions": [
      { "Title": "Theme", "Kind": "button", "CssClass": "nav-icon theme-cycle-btn" },
      { "Title": "GitHub", "Url": "https://github.com/org/repo", "External": true, "CssClass": "nav-icon" }
    ]
  }
}
```

## Redirects + aliases
Use `aliases` in front matter for old URLs.  
Use `Redirects` or `RouteOverrides` in `site.json` for permanent site‑level rules.

Notes:
- Aliases emit **exact** 301 redirects to the final page route.
- `RouteOverrides` are applied before `Redirects` when generating redirect outputs.
- Redirects are emitted to host-specific formats:
  - `_redirects` (Netlify)
  - `staticwebapp.config.json` (Azure Static Web Apps)
  - `vercel.json` (Vercel)
- A full machine-readable list is written to:
  - `_powerforge/redirects.json`

## Themes + tokens
Themes provide layouts/partials/assets. Tokens map to CSS variables:
```
themes/nova/theme.json
themes/nova/partials/theme-tokens.html
themes/nova/assets/app.css
```

## Social + structured data
`site.json` can enable social tags and JSON‑LD breadcrumbs:
```
{
  "Social": {
    "Enabled": true,
    "SiteName": "ProductX",
    "Image": "/assets/icon.png",
    "TwitterCard": "summary"
  },
  "StructuredData": {
    "Enabled": true,
    "Breadcrumbs": true
  }
}
```
Pages opt‑in using front matter:
```
meta.social: true
meta.structured_data: true
```

## Head links + meta (site.json)
Use structured head tags so themes don’t repeat favicons or preconnects.
```
{
  "Head": {
    "Links": [
      { "Rel": "icon", "Href": "/favicon.png", "Type": "image/png" },
      { "Rel": "apple-touch-icon", "Href": "/apple-touch-icon.png" },
      { "Rel": "preconnect", "Href": "https://img.shields.io", "Crossorigin": "anonymous" },
      { "Rel": "dns-prefetch", "Href": "https://img.shields.io" }
    ],
    "Meta": [
      { "Name": "theme-color", "Content": "#0a0e14" }
    ]
  }
}
```

## Playgrounds / apps
Publish the app separately and overlay it under `/playground/`.
Use the `app` shortcode to link to it.

## Taxonomies
Define taxonomies (tags/categories) in `site.json`:
```json
{
  "Taxonomies": [
    { "Name": "tags", "BasePath": "/tags", "ListLayout": "taxonomy", "TermLayout": "term" }
  ]
}
```
PowerForge.Web generates:
- `/tags/` (taxonomy list)
- `/tags/<term>/` (term pages)

## Outputs
Enable multiple outputs (HTML/JSON/RSS) per page kind:
```json
{
  "Outputs": {
    "Rules": [
      { "Kind": "section", "Formats": ["html", "rss"] },
      { "Kind": "page", "Formats": ["html", "json"] }
    ]
  }
}
```

## Versioning
Versioning metadata can be stored in `site.json` and used in templates:
```json
{
  "Versioning": {
    "Enabled": true,
    "BasePath": "/docs",
    "Current": "v2",
    "Versions": [
      { "Name": "v2", "Label": "v2", "Url": "/docs/v2/", "Latest": true },
      { "Name": "v1", "Label": "v1 (LTS)", "Url": "/docs/v1/", "Deprecated": true }
    ]
  }
}
```
This data is available under `site.versioning` in templates.

## Link checking
Enable link checking in `site.json`:
```json
{
  "LinkCheck": {
    "Enabled": true,
    "IncludeExternal": false,
    "Skip": ["**/external/**"]
  }
}
```
Results are written to `_powerforge/linkcheck.json`.

## Build cache
Enable a simple markdown render cache:
```json
{
  "Cache": {
    "Enabled": true,
    "Root": ".cache/powerforge-web",
    "Mode": "contenthash"
  }
}
```
You can override per page via front matter:
```
outputs:
  - html
  - json
```

## Recommended defaults
- Markdown for most pages
- JSON data for repeated blocks
- Avoid HTML in markdown except for edge cases
- Keep layouts in themes, not in content
