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

## Site spec layering (base + overrides)
You can compose a site from a shared base spec and project‑specific overrides
using `extends` in `site.json`.

Example:
```json
{
  "extends": ["../shared/site.base.json"],
  "name": "IntelligenceX",
  "baseUrl": "https://example.com",
  "defaultTheme": "intelligencex",
  "navigation": {
    "menus": [
      { "name": "main", "items": [{ "title": "Home", "url": "/" }] }
    ]
  }
}
```

Rules:
- `extends` accepts a string or array of strings.
- Paths are resolved relative to the current `site.json`.
- Multiple bases are merged in order; the current file wins.
- Objects are merged **deeply**; arrays are **replaced** (not concatenated).

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

Markdown parsing target is GitHub-like flavor (GFM-style) for predictable docs behavior.
Notably, definition-list parsing is disabled by default, so prose like `Q:` / `A:` does not
silently turn into `<dl>/<dt>/<dd>` structures.

Supported fields:
- `title`, `description`, `date`
- `tags`, `aliases`
- `slug`, `order`, `draft`
- `language` / `lang` (page language code when localization is enabled)
- `translation_key` (shared key used to map the same page across languages)
  - aliases: `translation.key`, `i18n.key`
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
  - `meta.content_engine` (render the page body as a template before Markdown)
  - `meta.prism` (enable/disable Prism syntax highlighting for fenced code blocks; default: auto)
  - `meta.prism_mode` (`auto`, `always`, `off`)
  - `meta.prism_source` (`local`, `cdn`, `hybrid`)
  - `meta.prism_cdn` (override Prism CDN base URL; default: `https://cdn.jsdelivr.net/npm/prismjs@1.29.0`)
  - `meta.prism_default_language` (default language for code blocks without a language)
  - `meta.prism_css_light` / `meta.prism_css_dark` (local Prism theme paths)
  - `meta.prism_core` / `meta.prism_autoloader` (local Prism script paths)
  - `meta.prism_lang_path` (local Prism language components base path)
  - `meta.xref` / `meta.xrefs` / `meta.uid` / `meta.uids` (optional xref IDs for this page)

### Xref links (docs <-> API)
Use `xref:` links in markdown to reference symbols/pages by ID:

```markdown
[Install guide](xref:docs.install)
[String.Length](xref:System.String#Length)
```

The engine resolves xrefs from:
- page IDs declared in front matter (`xref`/`xrefs`/`uid`/`uids`)
- implicit page IDs (`collection:slug`, route, translation key, source-based IDs)
- optional external map files from `site.json -> Xref.MapFiles`
  - API docs generators can emit DocFX-style `xrefmap.json` files (C# + PowerShell), which are valid `MapFiles` inputs.

Optional `site.json` config:

```json
{
  "Xref": {
    "Enabled": true,
    "MapFiles": ["xrefmap.json"],
    "WarnOnMissing": true,
    "EmitMap": true,
    "MaxWarnings": 25
  }
}
```

When `EmitMap` is enabled, the build writes `_powerforge/xrefmap.json`.

### Syntax highlighting
PowerForge.Web automatically injects Prism assets when a page contains fenced code blocks.
To disable highlighting on a page, set `meta.prism: false`. To force it on a page
without detected code blocks, set `meta.prism: true`. Use `meta.prism_source` to
force local vs CDN per page.

### Front matter resolution rules
PowerForge.Web resolves missing values using simple defaults:
- `title`: `front matter title` → first `# H1` → filename
- `slug`: `front matter slug` → filename
- `description`: `front matter description` (no automatic fallback)
- `date`: `front matter date` (no git/mtime fallback yet)

### Content templating
Use `meta.content_engine` when you need template logic inside page content.
The content is rendered **before** Markdown, so you can generate Markdown or HTML.

Supported engines:
- `scriban` (full template language, loops/conditionals)
- `simple` (token replacement: `{{TITLE}}`, `{{DESCRIPTION}}`, `{{CONTENT}}`, etc.)
- `theme` (use the current theme engine)

Example:
```
---
title: Home
meta.content_engine: scriban
meta.raw_html: true
---

<section class="faq">
  {{ for item in data.faq }}
    <details>
      <summary>{{ item.question }}</summary>
      <div>{{ item.answer }}</div>
    </details>
  {{ end }}
</section>
```

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

### Localization (multi-language routing)
Enable multi-language URLs in `site.json`:
```json
{
  "Localization": {
    "Enabled": true,
    "DefaultLanguage": "en",
    "PrefixDefaultLanguage": false,
    "DetectFromPath": true,
    "Languages": [
      { "Code": "en", "Label": "English", "Default": true, "BaseUrl": "https://evotec.xyz" },
      { "Code": "pl", "Label": "Polski", "BaseUrl": "https://evotec.pl" }
    ]
  }
}
```

When `DetectFromPath` is enabled, a leading language folder in content paths is used as language marker and removed from slug generation:
- `content/docs/en/index.md` → `/docs/` (default language, no prefix when `PrefixDefaultLanguage=false`)
- `content/docs/pl/index.md` → `/pl/docs/`

Use `translation_key` when page paths differ per language and you still want reliable language switcher links.
This is the recommended way to keep one logical page mapped across translated slugs.

Use language-level `BaseUrl` when localized variants live on different domains.
When set, `hreflang` head links and sitemap alternates use the language domain instead of the site `BaseUrl`.

When localization is enabled and at least two languages are configured:
- Page `<head>` output includes `rel="alternate"` language links (`hreflang`) for localized variants.
- The default language variant also emits `hreflang="x-default"`.
- Search output is emitted as:
  - `/search/index.json` (all languages)
  - `/search/<language>/index.json` (language shard, for example `/search/pl/index.json`)
  - `/search/collections/<collection>/index.json` (collection shard, for example `/search/collections/docs/index.json`)
  - `/search/manifest.json` (discoverability manifest for shards + search surface path)
  - `/search/index.html` (auto-generated search page when `Features` includes `search` and no custom page exists)

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

To force **auto navigation** from folders even when a TOC exists, disable it:
```json
{
  "UseToc": false
}
```
When `UseToc` is true, `powerforge web verify` warns about pages that are
missing from the TOC. If `UseToc` is true but no `toc.json` / `toc.yml` is found,
`powerforge web verify` emits a warning so you don’t silently fall back.

## Data files
JSON files under `data/` become `data.<fileName>` in Scriban.
```
data/
  hero.json        -> data.hero
  sections.json    -> data.sections
  showcase.json    -> data.showcase
```

Use data for repeated UI blocks and long lists.

### Data validation
`powerforge web verify` validates known data files when present (`faq.json`, `showcase.json`,
`pricing.json`, `benchmarks.json`). It emits warnings for missing required keys and malformed shapes.

### Generated data outputs
When no `data/site-nav.json` exists in the site output, PowerForge.Web will
emit one based on `site.json` navigation (including auto‑generated menus).
If you provide your own `data/site-nav.json` (for example via `static/data`),
the generator will **not** overwrite it.

The generated `site-nav.json` also includes:
- `schemaVersion`: export payload version (allows safe evolution for external consumers)
- `format`: stable payload identifier (`powerforge.site-nav`)
- `surfaceCanonicalNames`: canonical surface keys (`main`, `docs`, `apidocs`, `products`)
- `surfaceAliases`: alias map for compatibility (`api` -> `apidocs`)
- `profiles`: exported `Navigation.Profiles` rules (so other tools can select the same nav your theme uses)
- `surfaces`: resolved navigation projections for stable "nav surfaces" like `main`, `docs`, `apidocs`, `products`.
  - surface keys are exported in canonical form (`apidocs`, not `api`/`apiDocs`).
  - Use `Navigation.Surfaces` in `site.json` to define/override; each surface captures the selected profile context and resolved menu projections (`primary`, optional `sidebar`, optional `products`).

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
{{< map query="Evotec Services, Mikolow" title="Office map" >}}
{{< app src="/playground/" label="Launch Playground" title="Playground" >}}
{{< edit-link >}}
```

Shortcodes can be implemented as:
- code handlers (C#), or
- theme partials `partials/shortcodes/<name>.html`

## Navigation
Navigation lives in `site.json` under `Navigation`:
- `Navigation.Menus` (primary structures)
- `Navigation.Actions` (header CTA/icon links)
- `Navigation.Regions` (named slots like `header.left`, `header.right`, `mobile.drawer`)
- `Navigation.Footer` (columns + legal links)
- `Navigation.Profiles` (path/layout/collection/project scoped overrides)

Templates receive a computed `navigation` object with active states.

Best practice (theme rendering):
- Use menu names (e.g. `main`, `docs`) rather than index-based access (`navigation.menus[0]`) so your theme is deterministic across sites.
- In Scriban themes you can use built-in helpers exposed as `pf`:
  - `{{ pf.nav_links "main" }}` renders the main menu as a flat list of `<a>` elements (supports `is-active` / `is-ancestor` classes).
  - `{{ pf.nav_actions }}` renders `Navigation.Actions` as links/buttons.
  - `{{ pf.menu_tree "docs" 4 }}` renders a nested `<ul>` tree for sidebar menus.
  - `{{ pf.editorial_cards }}` renders reusable card markup from current list context (`items`) with optional arguments:
    - `max_items` (default `0` = all)
    - `excerpt_length` (default `160`)
    - `show_collection`, `show_date`, `show_tags`, `show_image` (defaults `true`)
    - `image_aspect` (default `"16/9"`, accepts `/` or `:` like `"4:3"`)
    - `fallback_image` (default empty; if omitted, helper falls back to `site.social.image` when set)
    - `variant` (default `"default"`, options: `"default"`, `"compact"`, `"hero"`, `"featured"`; controls helper-emitted CSS classes)
    - `grid_class` (optional extra classes appended to the wrapper grid container)
    - `card_class` (optional extra classes appended to each rendered card)
    - Example: `{{ pf.editorial_cards 12 180 true true true true "4:3" "/images/fallback.png" "hero" "news-grid custom-grid" "news-card custom-card" }}`
    - Verify integration: if the theme defines `featureContracts.blog/news`, verify checks that variant and override selectors used by `pf.editorial_cards` are declared in `requiredCssSelectors`, with copy/paste contract hints when missing.
  - `{{ pf.editorial_pager }}` renders previous/next pagination links from runtime `pagination`:
    - `newer_label` (default `"Newer posts"`)
    - `older_label` (default `"Older posts"`)
    - `css_class` (default `"pf-pagination"`)
    - Example: `{{ pf.editorial_pager "Newer" "Older" }}`

### Navigation surfaces (stable projections)
The runtime `navigation` object exposes `navigation.surfaces` as named projections.
PowerForge can infer defaults (`main`, `docs`, `apidocs`, `products`) from features/menus, but production sites should define `Navigation.Surfaces` explicitly so verify/nav-lint checks stay deterministic.
This helps themes avoid hard-coding assumptions like "docs sidebar == menu named docs" and supports multi-surface headers (main/products/docs).
Example:
```json
{
  "Navigation": {
    "Surfaces": [
      { "Name": "main", "PrimaryMenu": "main" },
      { "Name": "docs", "PrimaryMenu": "main", "SidebarMenu": "docs" },
      { "Name": "products", "PrimaryMenu": "main", "ProductsMenu": "products" }
    ]
  }
}
```
In Scriban you can then do:
```scriban
{{ if navigation.surfaces && navigation.surfaces.size > 0 }}
  {{ for s in navigation.surfaces }}
    {{ if s.name == "docs" && s.sidebar }}
      {{ for link in s.sidebar.items }} ... {{ end }}
    {{ end }}
  {{ end }}
{{ end }}
```

### Default auto navigation
If `Navigation` is **omitted**, PowerForge.Web generates sensible defaults:
- A `docs` menu from the `docs` collection (or any collection whose output starts with `/docs`).
- A `main` menu from the `pages` collection (or any collection whose output is `/`).

To disable this default behavior, define `Navigation` and set:
```json
{ "Navigation": { "AutoDefaults": false } }
```

`Navigation.Actions` is for header buttons/icons (theme toggles, GitHub, CTA).
These items are exposed as `navigation.actions`.

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

### Visibility rules (per menu/per item)
Use visibility filters to scope nav elements:
```json
{
  "Navigation": {
    "Menus": [
      {
        "Name": "main",
        "Visibility": { "Paths": ["/docs/**"] },
        "Items": [
          { "Title": "Docs", "Url": "/docs/" },
          {
            "Title": "API",
            "Url": "/api/",
            "Visibility": { "Projects": ["core"] }
          }
        ]
      }
    ]
  }
}
```

### Regions + footer + profiles
Use regions for complex headers and profiles for route-specific nav variants:
```json
{
  "Navigation": {
    "Regions": [
      { "Name": "header.left", "Menus": ["main"] },
      { "Name": "header.right", "IncludeActions": true }
    ],
    "Footer": {
      "Label": "default",
      "Menus": ["footer-product", "footer-company"],
      "Legal": [
        { "Title": "Privacy", "Url": "/privacy/" },
        { "Title": "Terms", "Url": "/terms/" }
      ]
    },
    "Profiles": [
      {
        "Name": "api",
        "Paths": ["/api/**"],
        "Priority": 20,
        "InheritMenus": false,
        "Menus": [
          {
            "Name": "main",
            "Items": [
              { "Title": "API Home", "Url": "/api/" },
              { "Title": "Docs", "Url": "/docs/" }
            ]
          }
        ]
      }
    ]
  }
}
```

### Mega menu sections
Each menu item can optionally define `Sections`/`Columns` for richer dropdowns:
```json
{
  "Title": "Products",
  "Sections": [
    {
      "Title": "SDK",
      "Items": [
        { "Title": ".NET", "Url": "/docs/library/overview/" },
        { "Title": "PowerShell", "Url": "/docs/powershell/overview/" }
      ]
    }
  ]
}
```

### Verify policy defaults
Set global verify policy defaults in `site.json`:
```json
{
  "Verify": {
    "FailOnWarnings": true,
    "FailOnNavLint": true,
    "FailOnThemeContract": true,
    "SuppressWarnings": [
      "PFWEB.NAV.LINT",
      "PFWEB.THEME.CONTRACT"
    ]
  }
}
```
Notes:
- Applied by `powerforge-web verify` and `powerforge-web doctor`.
- CLI flags with the same names also enable these checks.
- Pipeline `verify`/`doctor` step fields override these defaults when specified.

`SuppressWarnings` supports:
- warning codes (for warnings prefixed like `[PFWEB.NAV.LINT] ...`)
- substring matches (case-insensitive)
- wildcard patterns (`*`, `?`)
- regex patterns prefixed with `re:`

## Redirects + aliases
Use `aliases` in front matter for old URLs.  
Use `Redirects` or `RouteOverrides` in `site.json` for permanent site‑level rules.

Notes:
- Aliases emit **exact** 301 redirects to the final page route.
- Alias redirects are emitted for both slash variants (for example `/old-post` and `/old-post/`) so legacy blog links stay non-breaking.
- Set `EnableLegacyAmpRedirects: true` in `site.json` to auto-generate compatibility redirects from `/.../amp` routes to canonical HTML pages.
- `RouteOverrides` are applied before `Redirects` when generating redirect outputs.
- Redirects are emitted to host-specific formats:
  - `_redirects` (Netlify)
  - `staticwebapp.config.json` (Azure Static Web Apps)
  - `vercel.json` (Vercel)
  - `.htaccess` (Apache/mod_rewrite)
  - `nginx.redirects.conf` (Nginx include snippet)
  - `web.config` (IIS URL Rewrite)
- A full machine-readable list is written to:
  - `_powerforge/redirects.json`

Example (`site.json`):
```json
{
  "EnableLegacyAmpRedirects": true
}
```

## Sitemap behavior
The `sitemap` task generates entries automatically and can be augmented by
explicit entries in the pipeline spec.

Defaults:
- All generated `.html` files under `_site/` are included.
- `robots.txt`, `llms.txt`, `llms.json`, `llms-full.txt` are included when present.
- Paths are normalized to trailing‑slash routes when they map to `index.html`.
- If localization config exists in `_powerforge/site-spec.json`, sitemap entries include localized alternates (`xhtml:link`, `hreflang`, `x-default`).
- When localization languages define `BaseUrl`, sitemap alternates use those per-language domains.
- Optional JSON output (`json: true`) writes a resolved machine-readable sitemap (`sitemap/index.json`) including URL/title/section metadata.
- Optional news sitemap output (`newsOut`) emits a Google News-compatible sitemap from selected routes.
- Optional image sitemap output (`imageOut`) emits image discovery entries for selected routes.
- Optional video sitemap output (`videoOut`) emits video discovery entries for selected routes.
- Optional sitemap index output (`sitemapIndex`) emits a `sitemapindex` file referencing generated XML sitemap files.

Explicit entries:
- `entries` in the sitemap task override auto‑generated metadata for the same path.
- `entriesJson` can load entries from a JSON file (`[{...}]` or `{ "entries":[...] }`).
- If a path is not present in auto output, it is still added to the sitemap.
- `newsPaths` controls which routes are included in `newsOut`; defaults target `**/news/**`.
- `newsMetadata` can set publication name/language and optional genres/access/keywords.
- `imagePaths`/`videoPaths` control which routes are included in `imageOut`/`videoOut`.
- `entries[].images` and `entries[].videos` can be used to provide explicit media URLs when route discovery is not enough.

Example task:
```json
{
  "task": "sitemap",
  "siteRoot": "./_site",
  "baseUrl": "https://example.com",
  "entries": [
    { "path": "/docs/getting-started/", "priority": "0.9", "changefreq": "weekly" },
    { "path": "/api/", "priority": "0.8", "changefreq": "weekly" }
  ]
}
```

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
    "ImageWidth": 1200,
    "ImageHeight": 630,
    "TwitterCard": "summary_large_image",
    "TwitterSite": "@productx",
    "TwitterCreator": "@author",
    "AutoGenerateCards": true,
    "GeneratedCardsPath": "/assets/social/generated",
    "GeneratedCardWidth": 1200,
    "GeneratedCardHeight": 630
  },
  "StructuredData": {
    "Enabled": true,
    "Breadcrumbs": true,
    "Website": true,
    "Organization": true,
    "Article": true,
    "FaqPage": true,
    "HowTo": true,
    "SoftwareApplication": true,
    "Product": true,
    "NewsArticle": true
  }
}
```
When enabled in `site.json`, social tags and structured data are emitted by default for pages.
When `Social.AutoGenerateCards` is `true`, PowerForge.Web generates PNG social cards from
page title/description and uses them for `og:image`/`twitter:image` (unless a page sets `meta.social_image`).
By default this applies to share-priority pages (`home`, section pages, `pages` collection, editorial collections like `blog`/`news`).
Per-page override:
`meta.social_card: true` to force generation, `meta.social_card: false` to skip.
If `meta.social_image` is not set, blog posts also try to use the first markdown image in the post body
before falling back to generated/default site image.
Pages can opt out using front matter:
```
meta.social: false
meta.structured_data: false
```

Structured-data profile metadata (per-page front matter):
```
# FAQPage: list items as "Question|Answer"
meta.faq.questions:
  - "What does this tool do?|It automates release validation."
  - "Can I run offline?|Yes, for local workflows."

# HowTo: list steps as "Step Name|Step text"
meta.howto.name: Publish Module
meta.howto.description: End-to-end release flow.
meta.howto.total_time: PT20M
meta.howto.tools:
  - PowerShell 7
meta.howto.supplies:
  - Repository access
meta.howto.steps:
  - "Build|Run dotnet build."
  - "Test|Run dotnet test."

# Product
meta.product.name: IntelligenceX Pro
meta.product.brand: Evotec
meta.product.sku: IX-PRO-01
meta.product.price: 49.99
meta.product.price_currency: USD
meta.product.rating_value: 4.8
meta.product.rating_count: 137

# SoftwareApplication
meta.software.name: IntelligenceX CLI
meta.software.application_category: DeveloperApplication
meta.software.operating_system: Windows, Linux, macOS
meta.software.version: 1.4.0
meta.software.download_url: /downloads/ix-cli
meta.software.price: 0
meta.software.price_currency: USD
```

News article profile behavior:
- if `StructuredData.NewsArticle` is `true`, collection `news` pages emit `NewsArticle` JSON-LD.
- if both `Article` and `NewsArticle` are enabled, `NewsArticle` is preferred for news pages, while non-news article-like pages still emit `Article`.

Optional per-page social overrides:
```
meta.social_title: "Custom share title"
meta.social_description: "Custom share description"
meta.social_image: "/assets/social/custom-card.png"
meta.social_image_alt: "Card image alt text"
meta.social_image_width: 1200
meta.social_image_height: 630
meta.social_twitter_site: "@productx"
meta.social_twitter_creator: "@author"
```
Image aliases also accepted as explicit social overrides: `meta.image`, `meta.cover_image`, `meta.thumbnail`, `meta.og_image`, `meta.twitter_image`.

## SEO templates (site + collection)
Define snippet-style templates in `site.json`:
```json
{
  "Seo": {
    "Enabled": true,
    "Templates": {
      "Title": "{title} | {site}",
      "Description": "{title}: {description}"
    }
  }
}
```

Collection-level override:
```json
{
  "Collections": [
    {
      "Name": "blog",
      "Input": "content/blog",
      "Output": "/blog",
      "Seo": {
        "Templates": {
          "Title": "{title} ({date}) | {site}",
          "Description": "{collection} update: {title}"
        }
      }
    }
  ]
}
```

Supported tokens:
- `{title}`
- `{site}`
- `{collection}`
- `{date}` (`yyyy-MM-dd`)
- `{project}`
- `{lang}`

Optional per-page overrides in front matter:
```yaml
meta.seo_title: "Custom SEO title"
meta.seo_description: "Custom SEO description"
```

Each build emits resolved SEO metadata at `_powerforge/seo-preview.json`.

### Crawl policy (robots directives)
Use route-scoped crawl directives with optional bot-specific overrides:
```json
{
  "Seo": {
    "CrawlPolicy": {
      "Enabled": true,
      "DefaultRobots": "index,follow",
      "Bots": [
        { "Name": "googlebot", "Directives": "index,follow,max-image-preview:large" }
      ],
      "Rules": [
        {
          "Name": "search-noindex",
          "Match": "/search/*",
          "MatchType": "wildcard",
          "Robots": "noindex,follow",
          "Bots": [
            { "Name": "googlebot", "Directives": "noindex,follow" }
          ]
        }
      ]
    }
  }
}
```

Rule behavior:
- first matching rule wins
- match types: `exact`, `prefix`, `wildcard`
- page-level overrides win over policy:
  - `meta.robots`
  - `meta.googlebot` / `meta.bingbot` (or `meta.robots.googlebot`)

Build output diagnostics:
- `_powerforge/crawl-policy.json` (resolved directives per generated page)

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
Enable multiple outputs (HTML/JSON/RSS/Atom/JSON Feed) per page kind:
```json
{
  "Outputs": {
    "Rules": [
      { "Kind": "section", "Formats": ["html", "rss", "atom", "jsonfeed"] },
      { "Kind": "page", "Formats": ["html", "json"] }
    ]
  }
}
```

Implicit defaults (when no explicit output rule/override exists):
- editorial section pages (`blog`, `news`): `html` + `rss`
- `tags`/`categories` taxonomy and term pages: `html` + `rss`

Optional implicit feed formats:
```json
{
  "Feed": {
    "IncludeAtom": true,
    "IncludeJsonFeed": true
  }
}
```
This adds `atom` (`index.atom.xml`) and `jsonfeed` (`index.feed.json`) to implicit editorial/taxonomy outputs while keeping existing RSS defaults.

This gives zero-config feeds for common editorial/taxonomy layouts while keeping other page kinds HTML-only by default.

## Versioning
Versioning metadata can be stored in `site.json` and used in templates:
```json
{
  "Versioning": {
    "Enabled": true,
    "BasePath": "/docs",
    "HubPath": "./data/version-hub.json",
    "GenerateAliasRedirects": true,
    "Current": "v2",
    "LatestAliasPath": "/docs/latest/",
    "LtsAliasPath": "/docs/lts/",
    "Versions": [
      { "Name": "v2", "Label": "v2", "Url": "/docs/v2/", "Latest": true, "Aliases": ["stable"] },
      { "Name": "v1", "Label": "v1 (LTS)", "Url": "/docs/v1/", "Lts": true, "Deprecated": true, "Aliases": ["legacy"] }
    ]
  }
}
```
`HubPath` is optional. When set and `Versions` is empty, PowerForge loads versions from the generated `version-hub.json`.

This data is available under `site.versioning` in templates.
PowerForge also exposes a resolved runtime object under `versioning` (plus shortcuts `current_version`, `latest_version`, `versions`) with:
- normalized URLs
- resolved current version (from `Current` or current page path)
- resolved `versioning.lts` when any version is marked `Lts`
- `is_current` flags for each version entry

Recommended contract:
- mark exactly one version as `Default`
- mark exactly one version as `Latest`
- optionally mark one version as `Lts`
- keep `Current` aligned to a configured `Name`
- use root-relative URLs (for example `/docs/v2/`)

Redirect alias generation:
- when `GenerateAliasRedirects` is true, build output includes host redirect files for:
  - latest alias (default `<BasePath>/latest/` unless `LatestAliasPath` is set)
  - lts alias when any version has `Lts:true` (default `<BasePath>/lts/` unless `LtsAliasPath` is set)
  - per-version `Aliases` (for example `"stable"` -> `/docs/stable/`)

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
