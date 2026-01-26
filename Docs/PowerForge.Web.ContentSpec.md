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
  "Include": ["*.md", "**/*.md"]
}
```

## Data files
JSON files under `data/` become `data.<fileName>` in Scriban.
```
data/
  hero.json        -> data.hero
  sections.json    -> data.sections
  showcase.json    -> data.showcase
```

Use data for repeated UI blocks and long lists.

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
```

Shortcodes can be implemented as:
- code handlers (C#), or
- theme partials `partials/shortcodes/<name>.html`

## Navigation
Navigation lives in `site.json` under `Navigation.Menus`.
Templates receive a computed `navigation` object with active states.

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

## Recommended defaults
- Markdown for most pages
- JSON data for repeated blocks
- Avoid HTML in markdown except for edge cases
- Keep layouts in themes, not in content
