# PowerForge.Web Quick Start (Draft)

This guide shows the minimum structure and commands needed to build a site with PowerForge.Web.
Use it alongside `Samples/PowerForge.Web.Sample` for a working reference.

## 1) Create a site scaffold
```
powerforge-web scaffold --out ./MySite --name "Evotec" --base-url "https://example.com" --engine scriban
```

Result (minimal structure):
```
MySite/
  site.json
  content/
    pages/
      index.md
  themes/
    base-scriban/
      theme.json
      layouts/
      partials/
      assets/
```

## 2) Define collections in site.json
Collections map markdown input folders to output URLs.
```json
{
  "SchemaVersion": 1,
  "Name": "Evotec",
  "BaseUrl": "https://example.com",
  "Collections": [
    {
      "Name": "docs",
      "Input": "content/docs",
      "Output": "docs",
      "DefaultLayout": "docs",
      "Include": ["**/*.md"],
      "Exclude": ["**/drafts/**"]
    }
  ]
}
```

## 3) Add front matter to markdown
```
---
title: Getting Started
description: First steps with PowerForge.Web.
tags:
  - docs
  - quickstart
aliases:
  - /getting-started/
layout: docs
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

## 3b) Create new content quickly
Use archetypes to scaffold content:
```
powerforge-web new --config ./MySite/site.json --collection docs --title "Getting Started"
```
Archetypes live under `archetypes/` (or `ArchetypesRoot` in `site.json`).

## 4) Create a theme
Theme manifests live in `themes/<name>/theme.json`:
```json
{
  "name": "base-scriban",
  "engine": "scriban",
  "defaultLayout": "page",
  "layouts": {
    "page": "layouts/page.html",
    "docs": "layouts/docs.html"
  },
  "partials": {
    "header": "partials/header.html",
    "footer": "partials/footer.html"
  }
}
```

## 5) Build the site
```
powerforge-web build --config ./MySite/site.json --out ./Artifacts
```

## 6) Serve locally
```
powerforge-web serve --path ./Artifacts --port 8080
```

## 7) Pipelines and publish specs
For repeatable builds, use pipeline or publish specs:
```
powerforge-web pipeline --config ./pipeline.json
powerforge-web publish --config ./publish.json
```

Optional: run the built-in audit to validate links/assets/nav and (optionally) rendered checks:
```
powerforge-web audit --site-root ./Artifacts --summary
powerforge-web audit --site-root ./Artifacts --rendered --rendered-max 8
```

See `Docs/PowerForge.Web.Workflow.md` for the full end-to-end pipeline flow.
See `Docs/PowerForge.Web.Pipeline.md` for spec details and examples.

## 7b) API docs (optional)
If your library emits XML docs, you can auto-generate an API reference:
```json
{
  "task": "apidocs",
  "xml": "./Artifacts/generated/MyLib.xml",
  "assembly": "./Artifacts/generated/MyLib.dll",
  "out": "./Artifacts/site/api",
  "baseUrl": "/api",
  "title": "MyLib API",
  "template": "docs"
}
```
Notes:
- Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in your `.csproj`.
- `template: "docs"` renders the sidebar layout; `simple` is minimal.
- Wire the output into navigation (`/api/`) or link from `/docs/`.

## Asset registry (bundles, preloads, critical CSS)
Define shared CSS/JS once and map to routes. This keeps performance rules centralized.
```json
{
  "AssetRegistry": {
    "Bundles": [
      { "Name": "global", "Css": ["themes/base-scriban/assets/site.css"], "Js": ["themes/base-scriban/assets/site.js"] },
      { "Name": "docs", "Css": ["themes/base-scriban/assets/docs.css"] }
    ],
    "RouteBundles": [
      { "Match": "/**", "Bundles": ["global"] },
      { "Match": "/docs/**", "Bundles": ["docs"] }
    ],
    "Preloads": [
      { "Href": "/themes/base-scriban/assets/site.css", "As": "style" }
    ],
    "CriticalCss": [
      { "Name": "base", "Path": "themes/base-scriban/critical.css" }
    ]
  }
}
```

Notes:
- `AssetRegistry` on the site overrides theme asset choices.
- `RouteBundles` let you load only what a route needs.
- Use `CriticalCss` for above-the-fold content only.

### Route match patterns (cheat sheet)
Patterns are glob-style and match output paths:
```
/**            # everything
/docs/**       # docs section
/blog/**       # blog section
/projects/**   # project listing + pages
/docs/*.html   # only top-level doc pages
```

### Theme vs site assets (decision table)
```
Use case                          Place assets in
---------------------------------------------------------------
Single theme shared everywhere    theme.json
Project/site overrides            site.json AssetRegistry
Route-specific bundles            site.json AssetRegistry
```

## Data and shortcodes
Data files in `data/` are exposed as `data` in Scriban.
Shortcodes are rendered from handlers or theme partials:
```
{{< cards data="features" >}}
```

To override a shortcode, add:
`themes/<name>/partials/shortcodes/cards.html`

## Navigation + breadcrumbs
Default auto navigation:
- Generates a `docs` menu from the `docs` collection (or any `/docs` collection).
- Generates a `main` menu from the `pages` collection (or any root `/` collection).

Disable defaults if you want full manual control:
```json
{ "Navigation": { "AutoDefaults": false } }
```

Define menus in `site.json`:
```json
{
  "Navigation": {
    "Menus": [
      { "Name": "main", "Items": [
        { "Title": "Docs", "Url": "/docs/" }
      ]}
    ],
    "Actions": [
      { "Title": "GitHub", "Url": "https://github.com/org/repo", "External": true, "CssClass": "nav-icon" }
    ]
  }
}
```

Scriban context:
- `navigation.menus` (active + ancestor flags)
- `navigation.actions` (header buttons/icons)
- `breadcrumbs` (computed per page)

## Multi-project folder conventions
Recommended layout for a single repo that hosts many projects:
```
site.json
content/
  pages/
  docs/
  blog/
  snippets/
projects/
  HtmlForgeX/
    project.json
    content/
      pages/
      docs/
      blog/
    data/
    assets/
  TestimoX/
    project.json
    content/
    data/
    assets/
themes/
  base/
  codeglyphx/
shared/
  snippets/
  data/
```

Guidelines:
- Keep each project self-contained (content + data + assets).
- Use `shared/` for cross‑project snippets or data.
- Use project data via `data.projects.<slug>` or `data.project`.

## Minimal combined site.json example
```json
{
  "SchemaVersion": 1,
  "Name": "Evotec",
  "BaseUrl": "https://example.com",
  "Collections": [
    {
      "Name": "docs",
      "Input": "content/docs",
      "Output": "docs",
      "DefaultLayout": "docs",
      "Include": ["**/*.md"]
    },
    {
      "Name": "blog",
      "Input": "content/blog",
      "Output": "blog",
      "DefaultLayout": "post",
      "Include": ["**/*.md"]
    }
  ],
  "Navigation": {
    "Menus": [
      {
        "Name": "main",
        "Items": [
          { "Title": "Home", "Url": "/" },
          { "Title": "Docs", "Url": "/docs/" },
          { "Title": "Blog", "Url": "/blog/" }
        ]
      }
    ]
  },
  "AssetRegistry": {
    "Bundles": [
      { "Name": "global", "Css": ["themes/base-scriban/assets/site.css"], "Js": ["themes/base-scriban/assets/site.js"] },
      { "Name": "docs", "Css": ["themes/base-scriban/assets/docs.css"] }
    ],
    "RouteBundles": [
      { "Match": "/**", "Bundles": ["global"] },
      { "Match": "/docs/**", "Bundles": ["docs"] }
    ]
  }
}
```

## Next references
- `Docs/PowerForge.Web.ContentSpec.md`
- `Docs/PowerForge.Web.RFC.md`
- `Docs/PowerForge.Web.Theme.md`
- `Docs/PowerForge.Web.CodeGlyphX.Build.md`
- `Samples/PowerForge.Web.Sample/README.md`
