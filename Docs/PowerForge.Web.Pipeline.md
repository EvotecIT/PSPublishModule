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
- `nav`: path to `site.json` or `site-nav.json` to inject navigation tokens into header/footer
- `includeNamespace` / `excludeNamespace` are comma-separated namespace prefixes (pipeline only)
- `includeType` / `excludeType` accept comma-separated full type names (supports `*` suffix for prefix match)

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
  "version": "1.2.3"
}
```

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

#### optimize
Applies critical CSS + minifies HTML/CSS/JS.
```json
{
  "task": "optimize",
  "siteRoot": "./Artifacts/site",
  "criticalCss": "./themes/codeglyphx/critical.css",
  "cssPattern": "(app|api-docs)\\.css",
  "minifyHtml": true,
  "minifyCss": true,
  "minifyJs": true
}
```

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
