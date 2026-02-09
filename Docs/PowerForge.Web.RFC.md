# PowerForge.Web RFC (Draft)

## Summary
PowerForge.Web is a website/docs engine that builds static, high-performance sites from markdown + generated API docs, with optional Blazor islands for interactive content. It is driven by a JSON pipeline spec (like existing PowerForge pipeline specs) and designed to scale from single-project sites to a multi-project hub under one domain. The first pilot is HtmlForgeX + HtmlForgeX.Email. We will borrow proven concepts from Hugo/Jekyll/DocFX/Docusaurus (content collections, data files, front matter, API models, edit links), but implement them in a typed, PowerForge-native way.

## Goals
- Markdown as source of truth for content (pages, docs, blog).
- Generated API reference for C# and PowerShell projects (nice output, not DocFX clone).
- Static output by default; optional Blazor islands for interactive blocks.
- Templates/themes that are reusable by the community.
- First-class performance (Lighthouse 100 targets; preloads, critical CSS, minification).
- Pipeline orchestration inside PowerForge (typed specs + CLI + JSON schema).
- Centralized asset + accessibility registry for consistent loading and A11y defaults.
- Keep files maintainable: aim for < 700 LOC per file in PowerForge.Web.

## Non-goals
- Replacing PowerForge module build pipeline; this is an additional pipeline.
- Full CMS authoring UI; content is repo-first.
- Mandatory server-side features; output should work on static hosting.

## Terminology
- Site: a single website output (e.g., HtmlForgeX domain).
- Project: a product/library inside the site (HtmlForgeX.Email as a project under HtmlForgeX site).
- Content: author-written markdown pages/blogs/docs.
- API reference: generated from C# XML and PowerShell help metadata.

## Architecture overview
- PowerForge.Web (new library) contains:
  - Site pipeline spec + segments.
  - Content ingestion and markdown rendering.
  - API doc generator integration.
  - Theme/template engine.
  - Search index generator.
  - Asset optimization and validation.
  - Asset + A11y registry (policy-driven, per-route bundles and defaults).
- PowerForge.Cli gains: `powerforge site`, `powerforge site plan`, `powerforge site verify`.
- JSON schemas added under `schemas/` for new specs and segments.

## Content model (collections-first)
We should be flexible like Hugo/Jekyll: collections are first-class, and `projects/` is optional. This lets PowerForge.Web power simple company sites, doc portals, or multi-project hubs without changing the engine.

Recommended repo layout (per site):

- site.json
- content/
  - pages/
  - docs/
  - blog/
  - snippets/
- projects/ (optional)
  - HtmlForgeX/
    - project.json
    - content/
    - assets/
    - api/ (generated output, or input folder depending on step)
  - HtmlForgeX.Email/
    - project.json
    - content/
    - assets/
- themes/
  - base/
  - htmlforgex/
- shared/
  - snippets/
  - data/

Key files:
- site.json
  - site name, base url, default theme, search options, global nav, global redirects.
  - collections definition (pages/docs/blog/projects/etc).
  - asset registry (CSS/JS bundles, preloads, route->bundle mapping).
  - accessibility registry (default aria labels, skip links, link rules).
  - edit link template(s) for GitHub PR flow.
- project.json
  - title, slug, repo, package ids, versions, badges, documentation entrypoint, theme override.
- markdown front matter
  - title, description, date, tags, slug, status, draft, sidebar order.

The `shared/data` folder is Hugo-style structured data: teams, sponsors, feature matrices, version tables, global nav/footer, and other JSON/YAML that templates can use without duplicating content per project.

## Borrowed concepts (implemented our way)
- Jekyll: front matter + collections + permalink rules.
- Hugo: data files, taxonomies, content bundles, shortcodes.
- DocFX: API model output + UID linking + reference navigation.
- Docusaurus: edit links + versioned docs patterns.
We will adopt the ideas that work, but build a typed C# pipeline and generator rather than cloning any one tool.

## Markdown front matter (proposed v1 schema)
Front matter is YAML at the top of each content file (only for site content; not required for internal docs).
Required fields should be minimal to keep authoring simple.

Required:
- title

Common optional fields:
- description
- date (ISO 8601)
- tags (array)
- slug (override)
- order (number)
- draft (bool)
- collection (pages/docs/blog/projects)
- aliases (array of legacy URLs to redirect)
- canonical (override canonical URL)
- editPath (override edit link target)
 - layout (override layout)
 - template (override template)

Fallbacks when front matter is missing:
- title: first H1
- slug: derived from file path
- date: git history or file mtime
- tags: inferred by folder or empty
- collection: inferred by folder

Schema: `schemas/powerforge.web.frontmatter.schema.json`

## Markdown rendering spec (v1)
Markdown should be rendered consistently across sites with a small, opinionated extension set.
Recommended baseline features:
- Headings with stable IDs (GitHub-style slugs).
- Fenced code blocks with language + optional title (displayed in UI).
- Callouts/admonitions (note/warn/tip) mapped to theme components.
- Task lists and tables.
- Snippet includes (pull in shared markdown from `shared/snippets` or project snippets).
- Link auto-normalization (internal links resolved to final routes).

Proposed extension syntax (v1):
- Callouts: GitHub-style blocks
  - `> [!NOTE]`, `> [!TIP]`, `> [!WARNING]`, `> [!IMPORTANT]`
- Includes: PowerForge shortcode
  - `{{< include path=\"shared/snippets/install.md\" >}}`
- Code titles: fenced info string supports `title=...`
  - ```powershell title=\"Install\"

Optional v2 features:
- Tabs/groups for multi-language code samples.
- Mermaid diagrams.
- Per-section metadata (for advanced nav).

## Sample site.json (v1, draft)
This example is intentionally explicit to avoid guesswork.
It shows collections, asset/a11y registries, edit links, redirects, and analytics.

```json
{
  "$schema": "./schemas/powerforge.web.sitespec.schema.json",
  "SchemaVersion": 1,
  "Name": "ExampleSite",
  "BaseUrl": "https://example.com",
  "DefaultTheme": "base",
  "TrailingSlash": "always",
  "ContentRoot": "content",
  "ProjectsRoot": "projects",
  "ThemesRoot": "themes",
  "SharedRoot": "shared",

  "Collections": [
    {
      "Name": "pages",
      "Input": "content/pages",
      "Output": "/",
      "DefaultLayout": "page",
      "Include": ["**/*.md"]
    },
    {
      "Name": "docs",
      "Input": "content/docs",
      "Output": "/docs",
      "DefaultLayout": "docs",
      "Include": ["**/*.md"]
    },
    {
      "Name": "blog",
      "Input": "content/blog",
      "Output": "/blog",
      "DefaultLayout": "post",
      "Include": ["**/*.md"],
      "SortBy": "date",
      "SortOrder": "desc"
    },
    {
      "Name": "projects",
      "Input": "projects/*/content",
      "Output": "/projects/{project}",
      "DefaultLayout": "project",
      "Include": ["**/*.md"]
    }
  ],

  "EditLinks": {
    "Enabled": true,
    "Template": "https://github.com/EvotecIT/Repo/edit/main/{path}",
    "PathBase": ""
  },

  "RouteOverrides": [
    { "From": "/about", "To": "/company/about" }
  ],
  "Redirects": [
    { "From": "/old-blog-url", "To": "/blog/new-url", "Status": 301 },
    { "From": "/legacy/*", "To": "/blog", "Status": 302 }
  ],

  "AssetRegistry": {
    "Bundles": [
      { "Name": "global", "Css": ["css/app.css"], "Js": ["js/site.js"] },
      { "Name": "docs", "Css": ["css/docs.css"], "Js": ["js/docs.js"] },
      { "Name": "api", "Css": ["css/api.css"], "Js": [] },
      { "Name": "playground", "Css": ["css/playground.css"], "Js": ["js/playground.js"] }
    ],
    "RouteBundles": [
      { "Match": "/docs/**", "Bundles": ["global", "docs"] },
      { "Match": "/api/**", "Bundles": ["global", "api"] },
      { "Match": "/playground/**", "Bundles": ["global", "playground"] },
      { "Match": "/**", "Bundles": ["global"] }
    ],
    "Preloads": [
      { "Href": "/fonts/inter/Inter-600-latin.woff2", "As": "font", "Type": "font/woff2", "Crossorigin": "anonymous" }
    ],
    "CriticalCss": [
      { "Name": "base", "Path": "themes/base/critical.css" }
    ]
  },

  "A11y": {
    "SkipLinkLabel": "Skip to content",
    "ExternalLinkLabel": "Opens in a new tab",
    "NavToggleLabel": "Toggle navigation",
    "SearchLabel": "Search"
  },

  "LinkRules": {
    "ExternalRel": "noopener",
    "ExternalTarget": "_blank",
    "AddExternalIcon": true
  },

  "Analytics": {
    "Enabled": false,
    "Provider": "FirstParty",
    "Endpoint": "/api/track",
    "RespectDnt": true,
    "SampleRate": 1.0
  }
}
```

## Sample project.json (v1, draft)
```json
{
  "$schema": "../schemas/powerforge.web.projectspec.schema.json",
  "SchemaVersion": 1,
  "Name": "HtmlForgeX",
  "Slug": "htmlforgex",
  "Theme": "htmlforgex",
  "Repository": {
    "Provider": "GitHub",
    "Owner": "EvotecIT",
    "Name": "HtmlForgeX",
    "Branch": "main",
    "PathBase": ""
  },
  "Packages": [
    { "Type": "NuGet", "Id": "HtmlForgeX", "Version": "1.2.3" },
    { "Type": "NuGet", "Id": "HtmlForgeX.Email", "Version": "0.9.1" }
  ],
  "Links": [
    { "Title": "GitHub", "Url": "https://github.com/EvotecIT/HtmlForgeX" },
    { "Title": "NuGet", "Url": "https://www.nuget.org/packages/HtmlForgeX" }
  ],
  "ApiDocs": {
    "Type": "CSharp",
    "AssemblyPath": "artifacts/HtmlForgeX.dll",
    "XmlDocPath": "artifacts/HtmlForgeX.xml",
    "OutputPath": "api"
  },
  "Redirects": [
    { "From": "/htmlforgex/docs/getting-started", "To": "/projects/htmlforgex/docs/getting-started", "Status": 301 }
  ],
  "EditLinks": {
    "Enabled": true,
    "Template": "https://github.com/EvotecIT/HtmlForgeX/edit/main/{path}"
  }
}
```

## Sample content files (v1)
Docs page (`content/docs/getting-started.md`):
```markdown
---
title: Getting Started
description: Install HtmlForgeX and build your first dashboard.
date: 2026-01-10
tags: [install, quickstart]
slug: getting-started
order: 1
collection: docs
aliases:
  - /docs/intro
---

# Getting Started

> [!NOTE]
> HtmlForgeX is UI-only. It does not host data or APIs for you.

```powershell title="Install"
Install-Module HtmlForgeX -Scope CurrentUser
```

{{< include path="shared/snippets/support.md" >}}
```

Blog post (`content/blog/2026-01-05-htmlforgex-1-2.md`):
```markdown
---
title: HtmlForgeX 1.2 Released
description: New components, performance fixes, and better docs.
date: 2026-01-05
tags: [release, htmlforgex]
slug: htmlforgex-1-2
collection: blog
---

# HtmlForgeX 1.2 Released

Highlights:
- New DataGrid behaviors
- Improved markdown rendering
- Smaller bundle size
```

Static page (`content/pages/about.md`):
```markdown
---
title: About Evotec
description: Company overview and open-source mission.
slug: about
collection: pages
---

# About Evotec

We build open-source tooling for PowerShell and .NET.
```

## Docs vs deep docs (ingestion model)
We should separate docs into three layers, each with its own ingestion rules:

1) Guides (handwritten)
- Source: markdown in `content/docs` and `content/pages`.
- Purpose: onboarding, quickstart, tutorials, examples.
- Rendering: theme templates + markdown.

2) Reference (generated)
- Source: C# XML docs + assembly metadata, or PowerShell help data.
- Output: structured JSON + HTML pages.
- Rendering: API theme template with consistent layout, type navigation, search.

3) Deep reference (optional)
- Source: detailed metadata (attributes, XML param docs, PS parameter sets, examples).
- Output: richer JSON for interactive docs, advanced search, playground binding.

This split answers the ingestion question: guides come from repo content, reference from build artifacts, and deep reference from the compiler/help metadata.

## Edit links and GitHub PR flow
Every page should support an auto-generated \"Edit on GitHub\" link:
- `site.json` defines default repo/branch/path templates.
- Each content source may override repo/branch/base path.
- The renderer injects edit links into templates when configured.
This enables community PR-based edits on static hosting.

## Link management (aliases, overrides, redirects, 404)
This is a core engine feature, not tied to any single site migration.

Planned support:
- Front matter `aliases` (list of old URLs) to generate redirects automatically.
- Global redirects in `site.json` for bulk rules.
- Per-project redirects in `project.json`.
- Route overrides in `site.json` (explicit slug -> target mapping).
- Static HTML redirect pages for hosts without redirect support.
- Optional output for host-specific formats:
  - `_redirects` (Netlify)
  - `vercel.json` (Vercel)
  - `staticwebapp.config.json` (Azure Static Web Apps)
  - `redirects.json` (custom, for other hosts)
- Canonical URL handling and consistent trailing-slash rules.
- Custom 404 page with site search and project index.

Rules and precedence:
- Explicit overrides in `site.json` win.
- Per-project redirects apply next.
- Front matter `aliases` apply last.
- Collisions are surfaced as errors in Verify unless explicitly allowed.

Redirect rule spec (v1):
- `From`: source path (string)
- `To`: destination path (string)
- `Status`: 301/302/307/308 (default 301)
- `MatchType`: Exact | Prefix | Wildcard | Regex (default Exact)
- `PreserveQuery`: true/false (default true)

Matching behavior:
- Exact: full path match.
- Prefix: `From` is a path prefix; remainder is captured as `{path}`.
- Wildcard: `From` may include one `*` to capture `{path}`.
- Regex: `From` is a regex with named group `path`; `{path}` in `To` will be replaced.

Internal link hygiene:
- Normalize markdown links (relative -> absolute site routes).
- Validate internal links during Verify segment.

## Analytics / tracking (first-party)
Goal: first-party, privacy-friendly analytics without third-party services.

Options:
1) Static-only (default): no tracking, no external calls.
2) First-party endpoint: tiny JS beacon posts to your own endpoint (self-hosted or Cloudflare Worker).
3) Local logs: generate per-build instrumentation hooks that can be wired to a private endpoint later.

PowerForge.Web should provide:
- Optional analytics script injection (controlled by `site.json`).
- Event payload schema (page view, referrer, path, UA, timestamp).
- Sampling + Do-Not-Track respect by default.
- No cookies unless explicitly enabled.

## Pipeline spec (new)
Introduce `SitePipelineSpec` (parallel to ModulePipelineSpec):

- SchemaVersion
- Site (SiteBuildSpec)
- Segments (SiteSegment[])

SiteBuildSpec fields (initial):
- Name
- SourcePath
- OutputPath
- BaseUrl
- Theme
- ContentPaths
- ProjectsPath
- AssetsPath
- ApiInputPath
- ApiOutputPath

SiteSegment types (initial list):
- Content
- Markdown
- ApiDocs
- Theme
- StaticPages
- SearchIndex
- AssetsOptimize
- Sitemap
- Redirects
- Verify

## Segment details (initial)
Content
- Discovers content files and parses front matter.
- Supports includes/snippets.

Markdown
- Converts markdown to HTML.
- Injects fenced code block metadata for syntax highlighting.

ApiDocs
- C#:
  - Input: assembly path(s) + xml docs.
  - Output: JSON + HTML, with type slugs and searchable index.
- PowerShell:
  - Input: cmdlet help metadata + comment-based help extraction.
  - Output: JSON + HTML similar to C#.

Theme
- Loads theme manifest and partials.
- Applies layout + component rendering.

StaticPages
- Renders page templates to HTML.
- Allows per-project overrides and shared layouts.

SearchIndex
- Emits JSON index (title, summary, tags, content, url, project).
- Optional per-project index + global index.

AssetsOptimize
- Uses PSParseHTML / HtmlTinkerX for minification:
  - Optimize-HTML, Optimize-CSS, Optimize-JavaScript.
- Handles critical CSS extraction (initially manual or theme-provided).
- Optionally hashes asset filenames for long-term caching.

Sitemap
- Generates sitemap.xml and robots.txt.
- Supports per-route overrides for `priority`, `changefreq`, and `lastmod` (via pipeline or CLI entries list).

Redirects
- Builds redirect map (WordPress to new slugs).

Verify
- Link checks (internal), missing assets, duplicate slugs, missing front matter.
- Optional Lighthouse hints (headings order, image dimensions).

## Asset + accessibility registry (central policy)
To keep CSS/JS loading and A11y consistent, we define a central registry (in `site.json` and optional theme defaults):
- assetBundles: named CSS/JS groups (global, home, docs, api, playground).
- routeBundles: mapping of route/collection -> bundle list.
- preloads: fonts/critical assets with priority rules.
- a11yStrings: default labels (skip link, menu toggle, external link text).
- linkRules: external link handling (rel, target, icon, aria).
This ensures we don’t scatter performance and accessibility decisions across templates.

## Performance baseline
We should formalize the CodeGlyphX performance practices as defaults:
- Critical CSS inlined in base layout.
- Remaining CSS deferred via preload + onload media swap.
- Font preloads (single critical weight only) + `font-display: swap`.
- Explicit width/height on all images.
- JS deferred; no blocking scripts in head.
- Static home page by default; Blazor islands only on routes that need it.
- Service worker scope limited to WASM routes only.

## Templates and theming
- Theme manifest (theme.json) defines:
  - Name, version, author, layout files, assets, default variables.
- Themes are loaded from `themes/` or from a NuGet/local package.
- Common components (nav, footer, hero, callouts) stored as partials.
- Theme overrides at project level.
- Scriban engine (opt-in via theme.json `engine: "scriban"`):
  - Context: `site`, `page`, `content`, `assets`, `canonical_html`, `description_meta_html`, `site_name`, `base_url`.
  - Assets: `assets.css_html`, `assets.js_html`, `assets.preloads_html`, `assets.critical_css_html`.
  - Partials: `{{ include "header" }}` / `{{ include "footer" }}`.
- Site-level override (optional): `ThemeEngine` in `site.json` forces the engine for all themes.
- Data files: JSON files in `data/` are loaded and exposed as `data.*` to Scriban templates.
- Shortcodes (initial): `{{< cards data="features" >}}`, `{{< metrics data="metrics" >}}`, `{{< showcase data="showcase" >}}`.
- Theme tokens: `themes/<name>/theme.json` exposes CSS variables via the `theme-tokens` partial for fast theming (data overrides can be added later if needed).
- CLI: `powerforge-web` provides `plan/build/verify/scaffold/serve/publish/pipeline` commands, separate from the main `powerforge` tool.
- Publish spec (`web.publish`): one config that chains `build → overlay → dotnet publish → optimize` with optional Blazor fixes.
- Theme system details live in `Docs/PowerForge.Web.Theme.md`.

## Navigation + page metadata
- `site.json` supports `Navigation.Menus[]` with nested items (main, footer, docs sidebar).
- Templates access runtime menus via `navigation` (active/ancestor flags).
- Front matter supports arbitrary metadata via `page.meta`:
  - Unknown keys are stored as metadata.
  - Dot-notation (`hero.title`, `cta.primary`) builds nested objects.
  - Lists are supported for custom meta fields.
  
## Shortcodes
- Default shortcodes: `cards`, `metrics`, `showcase`.
- Themes can override any shortcode via `partials/shortcodes/<name>.html`.
- Scriban receives `shortcode.name`, `shortcode.attrs`, and `shortcode.data`.

## Search index
- Search index includes `project` and `meta` fields per entry.
- Frontend can filter using optional controls:
  - `[data-pf-search-collection]`
  - `[data-pf-search-project]`
  - `[data-pf-search-tag]`

## Data merge rules
- Site data: `data/<name>.json` → `data.<name>`
- Project data: `projects/<slug>/data/<name>.json` → `data.projects.<slug>.<name>`
- While rendering a project page, `data.project` is injected to point at that project's data bag.

### Publish spec example
```json
{
  "$schema": "./schemas/powerforge.web.publishspec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Config": "./site.json",
    "Out": "./Artifacts/site"
  },
  "Overlay": {
    "Source": "./Artifacts/site",
    "Destination": "./Artifacts/publish/wwwroot",
    "Include": ["**/*"]
  },
  "Publish": {
    "Project": "./PowerForge.Web.Sample.App/PowerForge.Web.Sample.App.csproj",
    "Out": "./Artifacts/publish",
    "Configuration": "Release",
    "Framework": "net10.0",
    "BaseHref": "/",
    "ApplyBlazorFixes": true
  },
  "Optimize": {
    "SiteRoot": "./Artifacts/publish/wwwroot",
    "CriticalCss": "./themes/codeglyphx/critical.css",
    "MinifyHtml": true,
    "MinifyCss": true,
    "MinifyJs": true
  }
}
```

## Playground and interactive blocks
- Markdown blocks define a demo with structured metadata.
- Default is static: render code + output snippet.
- Optional provider per project for interactive execution.
- Blazor islands are optional and isolated per route.

## Migration from WordPress
- Use existing HTML exports as source input.
- Normalize HTML via PSParseHTML/HtmlTinkerX (strip shortcodes, clean markup).
- Convert HTML -> Markdown (PowerForge.Web stage or separate tool).
- Preserve slugs and build redirect map.
- Manual cleanup for low-quality posts.

## Pilot: HtmlForgeX + HtmlForgeX.Email
- Single site output with two projects.
- Shared base theme with per-project overrides.
- Docs + API + blog per project.
- Pipeline file stored in HtmlForgeX repo (or site repo if split).

## Testing and validation
- Unit tests for:
  - slug generation
  - markdown rendering
  - API docs data model
  - search index builder
- Integration tests for:
  - site build pipeline
  - validation of output paths
- CI checks:
  - Verify stage must pass before publishing.

## Open questions
- Content location: per-project repos vs central content repo.
- API docs model: finalize schema for C# + PS output.
- Asset hashing strategy: deterministic vs hash-based URLs.
- Search: local JSON only vs optional hosted search.

## Milestones
1) Add PowerForge.Web project + SitePipelineSpec + basic segments.
2) Build HtmlForgeX pilot with static output and API docs.
3) Add search index and validation.
4) Add optional Blazor islands/playground.
5) Migrate CodeGlyphX to PowerForge.Web.
