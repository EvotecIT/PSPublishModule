# PowerForge.Web Pipeline & Publish Specs (Draft)

This guide documents the JSON formats used by `powerforge-web pipeline` and
`powerforge-web publish`, with examples and field meanings.

## Pipeline spec (`powerforge-web pipeline`)

Pipeline specs execute a list of steps in order. Paths are resolved relative to
the pipeline JSON file location.

Schema:
- `Schemas/powerforge.web.pipelinespec.schema.json`

Minimal pipeline:
```json
{
  "$schema": "./Schemas/powerforge.web.pipelinespec.schema.json",
  "steps": [
    {
      "task": "build",
      "config": "./site.json",
      "out": "./Artifacts/site"
    }
  ]
}
```

### Supported steps

#### build
Builds markdown + theme into static HTML.
```json
{ "task": "build", "config": "./site.json", "out": "./Artifacts/site", "clean": true }
```
Notes:
- `clean: true` clears the output directory before building (avoids stale files).

#### verify
Validates content + routing consistency from the site config.
```json
{ "task": "verify", "config": "./site.json" }
```
Notes:
- Emits warnings for missing titles, duplicate routes, missing assets, and TOC coverage.
- Fails the pipeline only when errors are found.

#### apidocs
Generates API reference output from XML docs (optionally enriched by assembly).
```json
{
  "task": "apidocs",
  "xml": "./Artifacts/generated/MyLib.xml",
  "assembly": "./Artifacts/generated/MyLib.dll",
  "out": "./Artifacts/site/api",
  "title": "API Reference",
  "baseUrl": "/api",
  "format": "json",
  "includeNamespace": "MyLib",
  "excludeType": "MyLib.InternalHelper,MyLib.Internal*"
}
```
Notes:
- `format`: `json`, `html`, `hybrid`, or `both` (json + html)
- HTML mode can include `headerHtml` + `footerHtml` fragments
- `template`: `simple` (default) or `docs` (sidebar layout)
- `type`: `CSharp` (default) or `PowerShell` (uses PowerShell help XML)
- `templateRoot` lets you override built-in templates/assets by placing files like
  `index.html`, `type.html`, `docs-index.html`, `docs-type.html`, `docs.js`,
  `search.js`, or `fallback.css` in that folder
- `templateIndex`, `templateType`, `templateDocsIndex`, `templateDocsType` let you
  override a single template file without a template root
  - `docsScript` / `searchScript` let you override the embedded JS files
  - `docsHome` / `docsHomeUrl` override the "Back to Docs" link in the sidebar (default `/docs/`)
  - `sidebar` (`left` or `right`) controls the docs sidebar position (`template: docs`)
  - `bodyClass` sets the `<body>` class on API docs pages (default `pf-api-docs`)
  - `sourceRoot` / `sourceUrl` enable source links in the API docs (requires PDB)
- `includeUndocumented` (default `true`) adds public types/members missing from XML docs
- `nav`: path to `site.json` or `site-nav.json` to inject navigation tokens into header/footer
- `includeNamespace` / `excludeNamespace` are comma-separated namespace prefixes (pipeline only)
- `includeType` / `excludeType` accept comma-separated full type names (supports `*` suffix for prefix match)

##### Template overrides
You can fully control the API docs layout by providing a template root or per-file overrides.
Starter templates live in:
- `Assets/ApiDocs/Templates/default` (matches the embedded defaults)
- `Assets/ApiDocs/Templates/sidebar-right` (example that moves the sidebar)
For CSS hooks and JS expectations, see `Docs/PowerForge.Web.ApiDocs.md`.

Recommended usage:
```json
{
  "task": "apidocs",
  "type": "CSharp",
  "xml": "./Artifacts/MyLib.xml",
  "assembly": "./Artifacts/MyLib.dll",
  "out": "./Artifacts/site/api",
  "format": "both",
  "template": "docs",
  "templateRoot": "./Assets/ApiDocs/Templates/default",
  "css": "/css/api-docs.css",
  "headerHtml": "./themes/nova/partials/api-header.html",
  "footerHtml": "./themes/nova/partials/api-footer.html",
  "nav": "./site.json"
}
```

PowerShell help usage:
```json
{
  "task": "apidocs",
  "type": "PowerShell",
  "help": "./Module/en-US/MyModule-help.xml",
  "out": "./Artifacts/site/api",
  "format": "both",
  "template": "docs",
  "css": "/css/api-docs.css"
}
```
Notes:
- If `help` points to a folder, the first `*-help.xml` file is used.
- For deterministic output, point to a specific help XML file.

##### Template tokens
Common tokens (all templates):
- `{{CSS}}` – stylesheet link or inline fallback CSS
- `{{HEADER}}` / `{{FOOTER}}` – injected header/footer fragments (optional)

Simple templates:
- `index.html`: `{{TITLE}}`, `{{TYPE_COUNT}}`, `{{TYPE_LINKS}}`, `{{SEARCH_SCRIPT}}`
- `type.html`: `{{TYPE_TITLE}}`, `{{TYPE_FULLNAME}}`, `{{TYPE_SUMMARY}}`, `{{TYPE_REMARKS}}`, `{{MEMBERS}}`

Docs templates (`template: docs`):
  - `docs-index.html`: `{{TITLE}}`, `{{SIDEBAR}}`, `{{SIDEBAR_CLASS}}`, `{{MAIN}}`, `{{DOCS_SCRIPT}}`
  - `docs-type.html`: `{{TITLE}}`, `{{SIDEBAR}}`, `{{SIDEBAR_CLASS}}`, `{{MAIN}}`, `{{DOCS_SCRIPT}}`

Header/footer fragments can use nav tokens when `nav` is provided:
- `{{SITE_NAME}}`, `{{BRAND_NAME}}`, `{{BRAND_URL}}`, `{{BRAND_ICON}}`
- `{{NAV_LINKS}}`, `{{NAV_ACTIONS}}`
- `{{FOOTER_PRODUCT}}`, `{{FOOTER_RESOURCES}}`, `{{FOOTER_COMPANY}}`
- `{{YEAR}}`

Project.json example (metadata you can reuse across repos):
```json
{
  "ApiDocs": {
    "Type": "CSharp",
    "AssemblyPath": "artifacts/MyLib.dll",
    "XmlDocPath": "artifacts/MyLib.xml",
    "OutputPath": "api"
  }
}
```
Notes:
- `ApiDocs` in `project.json` is **metadata**; today you still add the `apidocs` pipeline step.
- See `Samples/PowerForge.Web.Sample/projects/ApiDocsDemo/project.json` for a full example.

#### changelog
Generates a `data/changelog.json` file from a local `CHANGELOG.md` or GitHub releases.
```json
{
  "task": "changelog",
  "source": "auto",
  "changelog": "./CHANGELOG.md",
  "repo": "EvotecIT/IntelligenceX",
  "out": "./Artifacts/site/data/changelog.json"
}
```
Notes:
- `source`: `auto` (default), `file`, or `github`
- Use `repo` (`owner/name`) or `repoUrl` for GitHub releases.
- Use `max` to limit number of releases.
- The generator emits `body_md`; the build converts it to `body` HTML automatically.

Usage scenarios:

Local changelog only:
```json
{
  "task": "changelog",
  "source": "file",
  "changelog": "./CHANGELOG.md",
  "out": "./Artifacts/site/data/changelog.json"
}
```

GitHub releases only:
```json
{
  "task": "changelog",
  "source": "github",
  "repo": "EvotecIT/IntelligenceX",
  "token": "%GITHUB_TOKEN%",
  "max": 20,
  "out": "./Artifacts/site/data/changelog.json"
}
```

Template usage (Scriban):
```scriban
{{ for item in data.changelog.items }}
<article class="release">
  <h2>{{ item.title }}</h2>
  {{ if item.publishedAt }}<div class="release-date">{{ item.publishedAt }}</div>{{ end }}
  <div class="release-body">{{ item.body }}</div>
</article>
{{ end }}
```

#### llms
Generates `llms.txt`, `llms.json`, and `llms-full.txt`.
```json
{
  "task": "llms",
  "siteRoot": "./Artifacts/site",
  "project": "./src/MyLib/MyLib.csproj",
  "apiIndex": "./Artifacts/site/api/index.json",
  "apiBase": "/api",
  "name": "MyLib",
  "packageId": "MyLib",
  "version": "1.2.3",
  "apiLevel": "Summary",
  "apiMaxTypes": 200,
  "apiMaxMembers": 1500
}
```
Notes:
- `apiLevel`: `None` (default), `Summary`, or `Full`
- `apiMaxTypes` / `apiMaxMembers` cap the size of API detail sections in `llms-full.txt`

#### sitemap
Generates `sitemap.xml` and (optionally) `sitemap.html`.
```json
{
  "task": "sitemap",
  "siteRoot": "./Artifacts/site",
  "baseUrl": "https://example.com",
  "extraPaths": ["/robots.txt"],
  "html": true,
  "htmlTemplate": "./themes/nova/templates/sitemap.html",
  "htmlTitle": "Sitemap",
  "htmlCss": "/themes/nova/assets/app.css",
  "entries": [
    { "path": "/docs/", "changefreq": "weekly", "priority": "0.8" }
  ]
}
```
Notes:
- By default, **all HTML pages** under `siteRoot` are auto‑included.
- `entries` only override metadata (priority/changefreq/lastmod) for specific paths.
- Set `includeHtmlFiles: false` for a strict/manual sitemap.

#### optimize
Applies critical CSS, minifies HTML/CSS/JS, and can hash assets + generate cache headers.
```json
{
  "task": "optimize",
  "siteRoot": "./Artifacts/site",
  "config": "./site.json",
  "criticalCss": "./themes/codeglyphx/critical.css",
  "cssPattern": "(app|api-docs)\\.css",
  "minifyHtml": true,
  "minifyCss": true,
  "minifyJs": true,
  "hashAssets": true,
  "hashExtensions": [".css", ".js"],
  "hashExclude": ["**/nohash/**"],
  "cacheHeaders": true
}
```
Notes:
- `config` loads `AssetPolicy` from `site.json` (rewrites, hashing defaults, cache headers).
- `hashAssets` fingerprints files and rewrites references (HTML + CSS).
- `cacheHeaders` writes `_headers` with cache-control rules (Netlify/Cloudflare Pages compatible).

#### audit
Runs static (and optional rendered) checks against generated HTML.
```json
{
  "task": "audit",
  "siteRoot": "./Artifacts/site",
  "checkLinks": true,
  "checkAssets": true,
  "checkNav": true,
  "rendered": true,
  "renderedMaxPages": 10,
  "renderedInclude": "index.html,docs/**,benchmarks/**",
  "renderedExclude": "api/**,docs/api/**",
  "summary": true
}
```
Notes:
- Static checks run by default; set `rendered: true` to enable Playwright checks.
- `renderedInclude` / `renderedExclude` are comma-separated glob patterns (paths are relative to `siteRoot`).
- `summary: true` writes `audit-summary.json` under `siteRoot` unless `summaryPath` is provided.
- Use `noDefaultIgnoreNav` to disable the built-in API docs nav ignore list.
- Use `navRequired: false` (or `navOptional: true`) if some pages intentionally omit a nav element.
- Use `navIgnorePrefixes` to skip nav checks for path prefixes (comma-separated, e.g. `api/,docs/api/`).
- Use `noDefaultExclude` to include partial HTML files like `*.scripts.html`.
- `renderedBaseUrl` lets you run rendered checks against a running server (otherwise a local server is started).
- `renderedServe`, `renderedHost`, `renderedPort` control the temporary local server used for rendered checks.
- `renderedEnsureInstalled` auto-installs Playwright browsers before rendered checks (defaults to `true` in CLI/pipeline when `rendered` is enabled).
CLI note:
- Use `--rendered-no-install` to skip auto-install (for CI environments with preinstalled browsers).

#### dotnet-build
Runs `dotnet build`.
```json
{
  "task": "dotnet-build",
  "project": "./src/MySite/MySite.csproj",
  "configuration": "Release",
  "framework": "net9.0",
  "noRestore": true
}
```

#### dotnet-publish
Runs `dotnet publish` and (optionally) applies Blazor fixes.
```json
{
  "task": "dotnet-publish",
  "project": "./src/MySite/MySite.csproj",
  "out": "./Artifacts/publish",
  "configuration": "Release",
  "framework": "net9.0",
  "selfContained": false,
  "noBuild": true,
  "noRestore": true,
  "baseHref": "/",
  "defineConstants": "DOCS_BUILD",
  "blazorFixes": true
}
```

Notes:
- `blazorFixes` defaults to `true` in the CLI.
- Schema uses `noBlazorFixes` today; CLI reads `blazorFixes`. We should align these later.
- `defineConstants` maps to `-p:DefineConstants=...` for multi-variant Blazor publishes.

#### overlay
Copies a static overlay directory into another (useful for Blazor outputs).
```json
{
  "task": "overlay",
  "source": "./Artifacts/playground/wwwroot",
  "destination": "./Artifacts/site/playground",
  "clean": true,
  "include": "**/*",
  "exclude": "**/*.map"
}
```

Notes:
- `include` and `exclude` are comma-separated patterns in pipeline JSON.
- Example: `"include": "**/*.html,**/*.css"`
- `clean: true` deletes the destination folder before copying (avoids stale files).

## Publish spec (`powerforge-web publish`)

Publish specs wrap a typical build + publish flow into a single config.
Paths are resolved relative to the publish JSON file location.

Schema:
- `Schemas/powerforge.web.publishspec.schema.json`

Minimal publish:
```json
{
  "$schema": "./Schemas/powerforge.web.publishspec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Config": "./site.json",
    "Out": "./Artifacts/site",
    "Clean": true
  },
  "Publish": {
    "Project": "./src/MySite/MySite.csproj",
    "Out": "./Artifacts/publish",
    "Configuration": "Release"
  }
}
```

Full publish with overlay + optimize:
```json
{
  "$schema": "./Schemas/powerforge.web.publishspec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Config": "./site.json",
    "Out": "./Artifacts/site"
  },
  "Overlay": {
    "Source": "./Artifacts/site",
    "Destination": "./src/MySite/wwwroot",
    "Include": ["**/*"],
    "Exclude": ["**/*.map"]
  },
  "Publish": {
    "Project": "./src/MySite/MySite.csproj",
    "Out": "./Artifacts/publish",
    "Configuration": "Release",
    "Framework": "net9.0",
    "DefineConstants": "TRACE;DOCS_BUILD",
    "BaseHref": "/",
    "ApplyBlazorFixes": true
  },
  "Optimize": {
    "SiteRoot": "./Artifacts/site",
    "CriticalCss": "./themes/nova/critical.css",
    "MinifyHtml": true,
    "MinifyCss": true,
    "MinifyJs": true
  }
}
```

### Publish vs pipeline

- **Pipeline** is granular (step-by-step) and better for complex flows.
- **Publish** is a shortcut for the common build + overlay + dotnet publish + optimize flow.

If you need API docs, multiple overlays, or per-project outputs, use pipeline.

## Common pitfalls (and how to avoid them)

- **Generated output in source**: keep API docs in `Artifacts/` and overlay into output.
- **Wrong base URL**: set `BaseUrl` in `site.json` and `baseUrl` in sitemap step.
- **Paths resolve wrong**: remember specs resolve relative to their own JSON file.
