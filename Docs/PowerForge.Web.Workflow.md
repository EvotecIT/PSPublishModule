# PowerForge.Web Workflow (Draft)

This guide explains how PowerForge.Web turns source content into a deployable site,
what is considered source vs generated output, and how the pipeline is intended to
stay clean and repeatable.

## 1) Source vs output (what you commit)

Commit (source):
- `site.json`
- `content/` (markdown)
- `projects/` (project.json + project content)
- `themes/` (layouts, partials, assets)
- `data/` (JSON data for templates)
- `static/` (hand-authored static assets only)

Do not commit (generated):
- `Artifacts/` (build outputs, API docs, publish outputs)
- final site output (any `site/` or `wwwroot/` under Artifacts)
- `.cache/` (build cache, if enabled)

Rule of thumb: if a file can be regenerated from `site.json` + source content, it
belongs in `Artifacts/` and should not live in source folders like `static/`.

## 2) Core build stages (end-to-end)

PowerForge.Web splits work into small steps so the pipeline can be JSON-driven.
Recommended order:

1) **plan**: resolve `site.json` and discover content
2) **verify**: validate content routes and missing titles
3) **build**: render markdown + theme into static HTML
4) **apidocs**: generate API JSON/HTML (optional)
5) **sitemap**: create sitemap.xml + optional sitemap.html (optional)
6) **llms**: create llms.txt / llms.json (optional)
7) **optimize**: critical CSS + minify HTML/CSS/JS (optional)
8) **publish**: optional overlay + dotnet publish for Blazor host

You can run these via the CLI or use a single pipeline JSON.

## 3) Typical repo layout

```
site.json
content/
  pages/
  docs/
  blog/
projects/
  HtmlForgeX/
    project.json
    content/
      pages/
      docs/
    data/
    assets/
themes/
  nova/
    theme.json
    layouts/
    partials/
    assets/
data/
static/
Artifacts/        # generated, ignored
```

## 4) Build the site

```
powerforge-web build --config ./site.json --out ./Artifacts/site
```

Output:
- `Artifacts/site/` (final HTML, CSS, JS, images)
- `Artifacts/site/_powerforge/` (build meta, plan/spec/redirects)
- `Artifacts/site/search.json` (search index)
- `Artifacts/site/redirects.json` (if redirects were defined)
- `Artifacts/site/_powerforge/linkcheck.json` (if link check enabled)

## 5) Generate API docs (clean sources)

API docs should be generated into `Artifacts` and then copied into the output.
Do not keep generated API HTML/JSON in `static/`.

Example pipeline:
```json
{
  "steps": [
    { "task": "build", "config": "./site.json", "out": "./Artifacts/site" },
    {
      "task": "apidocs",
      "config": "./site.json",
      "type": "CSharp",
      "xml": "./Artifacts/generated/MyLibrary.xml",
      "assembly": "./Artifacts/generated/MyLibrary.dll",
      "out": "./Artifacts/site/api",
      "baseUrl": "/api",
      "format": "json"
    },
    {
      "task": "apidocs",
      "config": "./site.json",
      "type": "CSharp",
      "xml": "./Artifacts/generated/MyLibrary.xml",
      "assembly": "./Artifacts/generated/MyLibrary.dll",
      "out": "./Artifacts/site/api-docs",
      "baseUrl": "/api",
      "format": "hybrid",
      "headerHtml": "./apidocs/header.html",
      "footerHtml": "./apidocs/footer.html",
      "nav": "./site.json"
    },
    { "task": "sitemap", "siteRoot": "./Artifacts/site", "baseUrl": "https://example.com", "html": true },
    { "task": "optimize", "siteRoot": "./Artifacts/site" }
  ]
}
```

## 6) Redirects, aliases, and route overrides

PowerForge.Web emits redirects in two ways:
- `aliases` in front matter (legacy URLs for a page)
- explicit entries in `site.json` (`Redirects`, `RouteOverrides`)

Build output writes:
- `Artifacts/site/_powerforge/redirects.json` (full list)

That file can be transformed into host-specific formats later (Netlify, nginx, etc).

## 7) Assets and theming (central control)

The recommended pattern is:
- Put theme CSS/JS in `themes/<name>/assets/`
- Reference them from `theme.json` (or `site.json` asset registry)
- Use `AssetRegistry.RouteBundles` to decide which routes get which bundles
- Use `Head.Links` + `Head.Meta` in `site.json` for favicons, preconnects, and SEO

This keeps all CSS/JS loading decisions centralized and avoids hardcoding in layouts.

## 8) Blazor or other interactive apps

Blazor apps should be published separately and overlaid into the static output:
```
dotnet publish ./MyPlayground.csproj -c Release -o ./Artifacts/playground
powerforge-web overlay --source ./Artifacts/playground/wwwroot --destination ./Artifacts/site/playground --include "**/*"
```

Link to `/playground/` in markdown or navigation.

## 9) Local preview

```
powerforge-web serve --path ./Artifacts/site --port 8080
```

## 10) What should be ignored by git

Recommended ignore:
```
Artifacts/
**/obj/
**/bin/
```

## 11) Where to look for concrete examples

- `Docs/PowerForge.Web.QuickStart.md`
- `Docs/PowerForge.Web.ContentSpec.md`
- `Docs/PowerForge.Web.Theme.md`
- `Docs/PowerForge.Web.Pipeline.md`
- `Docs/PowerForge.Web.CodeGlyphX.Build.md`
