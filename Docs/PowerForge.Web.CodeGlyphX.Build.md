# PowerForge.Web CodeGlyphX Build Mapping (Draft)

This guide shows how the existing CodeGlyphX site build can be expressed as PowerForge.Web specs.
It does not modify CodeMatrix; it only demonstrates the equivalent PowerForge.Web steps.

## Goals
- Replace ad-hoc build scripts with a single JSON-driven pipeline.
- Keep the CodeGlyphX look and performance practices intact.
- Support both "Artifacts-only" and "full CodeMatrix publish" flows.
- Maintain 1:1 URL coverage during migration (no live URL changes).
- Allow dual content inputs (JSON or Markdown) with identical output.

## Parity contract (must-not-change)
These routes must remain valid and stable while CodeGlyphX is live:
- `/` (home)
- `/docs/` (hand-authored docs, currently a Blazor WASM app)
- `/docs/api` and `/docs/api/{TypeSlug}` (API reference UI)
- `/playground/` (Blazor WASM playground)
- `/benchmarks/` (static benchmarks)
- `/faq/` (static FAQ)
- `/showcase/` (static showcase)
- `/pricing/` (support/pricing)
- `/api/` (generated API HTML + JSON)
- `/llms.txt`, `/llms.json`, `/llms-full.txt`, `/sitemap.xml`, `/robots.txt`

If new routes are added later, they must be appended; existing ones must not be removed or renamed.

## Current CodeMatrix sources (as of now)
Use these as the authoritative input model when mapping to PowerForge.Web:
- Static pages: `CodeMatrix/Assets/Templates/pages/*.html` + `CodeMatrix/Build/Generate-StaticPages.ps1`
- Data: `CodeMatrix/Assets/Data/*.json` (FAQ, Showcase, Benchmarks)
 - Navigation: `CodeMatrix/CodeGlyphX.Website/wwwroot/data/site-nav.json` (legacy) or `site.json` (PowerForge.Web)
- API docs: PowerForge CLI output to `CodeMatrix/CodeGlyphX.Website/wwwroot/api`
- Blazor host: `CodeMatrix/CodeGlyphX.Website` for `/playground/` (and currently `/docs/` until migration completes)
- Smoke tests: `CodeMatrix/Build/website-smoke-tests.js`

## Simplification rules (non-negotiable)
- Never generate into tracked folders directly (no output into `wwwroot`).
- All build output goes to `Artifacts/` then overlays to final publish target.
- No scripts should mutate tracked files (no `git checkout` or overwrite patterns).
- The pipeline must be idempotent and fully reproducible from `site.json` + sources.
- `/playground/` is Blazor-only. PowerForge should generate static `/docs/` HTML and generate a docs-style `/docs/api` output.

## Dual input model (JSON + Markdown)
For FAQ, Showcase, Benchmarks, and Pricing we should accept:
- JSON data (existing CodeMatrix flow)
- Markdown files (new, author-friendly)

Recommended behavior:
- If JSON exists, render it with a shortcode via front matter:
  - `meta.data_shortcode: faq`
  - `meta.data_path: faq`
  - `meta.data_mode: override | append | prepend`
- If JSON does not exist, fall back to Markdown collection.
- Optional: allow both (Markdown section + JSON data blocks) via shortcodes.

## Recommended flow (Artifacts-only)
Use this when you want a safe local demo with no CodeMatrix changes.

Command:
```
powerforge-web publish --config Samples/PowerForge.Web.CodeGlyphX.Sample/publish-artifacts.json
```

What it does:
1) Build static site (Markdown + theme) into `Artifacts/PowerForge.Web.CodeGlyphX.Sample/site`
2) Overlay to `Artifacts/PowerForge.Web.CodeGlyphX.Sample/publish/wwwroot`
3) `dotnet publish` a minimal Blazor host app into `Artifacts/PowerForge.Web.CodeGlyphX.Sample/publish`
4) Optimize HTML/CSS/JS and apply critical CSS

## Full publish flow (CodeMatrix-aware)
Use this once you want to replace the current CodeGlyphX build chain.

Command:
```
powerforge-web publish --config Samples/PowerForge.Web.CodeGlyphX.Sample/publish.json
```

What it does:
1) Build static site into `Artifacts/PowerForge.Web.CodeGlyphX.Sample/site`
2) Overlay into `CodeMatrix/CodeGlyphX.Website/wwwroot`
3) `dotnet publish` the real CodeGlyphX website project
4) Optimize HTML/CSS/JS and apply critical CSS

Note: `publish.json` is a simplified flow and does not split DOCS/PLAYGROUND builds.
For full parity with the live site, use `pipeline-parity.json` below.

## Pipeline form (granular steps)
Use pipeline form when you want more fine‑grained tasks.

Command:
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline.json
```

Pipeline steps (mapping):
- `build` → static site generation from `site.json`
- `apidocs` → generate API reference from XML + assembly
- `llms` → create `llms.txt/llms.json` artifacts
- `sitemap` → combine site + api routes
- `optimize` → minify and apply critical CSS
- `audit` → validate static output and (optionally) run rendered checks

## Full parity pipeline (recommended for CodeGlyphX)
This mirrors the target CodeMatrix build (static root + static docs + playground overlay).

Command:
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline-parity.json
```

What it does:
1) Build static site into `Artifacts/PowerForge.Web.CodeGlyphX.Sample/site`
2) Generate `/api` HTML + JSON
3) Generate `/api` (simple static list) and `/docs/api` (docs-style static)
4) Build and overlay `/playground/` Blazor app (PLAYGROUND_BUILD) (overlay clean)
5) Generate sitemap + optimize output

Notes:
- The playground overlay is "clean" to remove stale static HTML under `/playground/`.
- `/docs/api` is generated with a docs-style template (sidebar + quick start).

## Clean API docs flow (generated artifacts)
To keep sources clean, API docs should be generated into `Artifacts/` and overlaid into the final site output.

Command:
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline-static-with-api.json
```

Expected inputs (generated by your library build):
- `Artifacts/PowerForge.Web.CodeGlyphX.Sample/generated/CodeGlyphX.xml`
- `Artifacts/PowerForge.Web.CodeGlyphX.Sample/generated/CodeGlyphX.dll`

Output:
- `Artifacts/PowerForge.Web.CodeGlyphX.Sample/site/api` (HTML + JSON)

The `Samples/PowerForge.Web.CodeGlyphX.Sample/static/` folder should only contain hand-authored assets (CSS/JS/images/fonts/vendor).

## Key inputs
- `Samples/PowerForge.Web.CodeGlyphX.Sample/site.json`
- `Samples/PowerForge.Web.CodeGlyphX.Sample/themes/codeglyphx/`
- `Samples/PowerForge.Web.CodeGlyphX.Sample/content/`
- `Samples/PowerForge.Web.CodeGlyphX.Sample/data/`
- `Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline-parity.json`

## Mapping notes (CodeGlyphX specifics)
- `/docs/` is static HTML generated from markdown.
- `/docs/api` is a static mirror of `/api` (SEO + URL parity).
- `/api/` is generated by PowerForge API docs for SEO and deep links.
- Navigation can be sourced from `site.json` (preferred) or `site-nav.json` (legacy).
- CodeGlyphX uses compile-time flags (`PLAYGROUND_BUILD`) to publish SPA output.
  Use `dotnet-publish` with `defineConstants` (include `TRACE;`) when building the playground.

## Parity checklist (routes + selectors)
Use this list as the baseline for 1:1 coverage. It mirrors the default smoke-test config.

Routes:
- `/` (expect `.hero`, text `Generate QR Codes`)
- `/docs/` (static, expect `.docs-layout`)
- `/docs/api` (static, expect `.api-layout`)
- `/playground/` (Blazor, expect `.playground`)
- `/benchmarks/` (expect `.benchmark-page`)
- `/faq/` (expect `.faq-page`)
- `/showcase/` (expect `.showcase-page`)
- `/pricing/` (expect `.pricing-page`)
- `/api/` (static API HTML + JSON)

Assets:
- `/llms.txt`, `/llms.json`, `/llms-full.txt`, `/sitemap.xml`, `/robots.txt`

## Replacement checklist
Use this when swapping the current CodeGlyphX build:
- [ ] Confirm `site.json` routes match the parity contract list.
- [ ] Confirm `/docs/api` renders docs-style layout and links to type pages.
- [ ] Confirm `/api/` HTML + JSON output is present and linked from `/docs/`.
- [ ] Confirm `sitemap.xml` entries include special pages (playground, showcase, benchmarks, pricing).
- [ ] Confirm `critical.css` matches above‑the‑fold layout.
- [ ] Confirm API docs output location and base URL.
- [ ] Run Lighthouse after `optimize`.

## Automated verification (recommended)
Use these checks before any cutover:
- PowerForge.Web `verify` output must be clean (no duplicate routes).
- PowerForge.Web `audit` should be clean (no broken links/assets or rendered console errors).
- Run `CodeMatrix/Build/website-smoke-tests.js` against the published output.
  - Make it configurable per site (expected routes + selectors).
  - Fail on missing nav links, core headings, or 4xx asset loads.
  - Optional config file: `CodeMatrix/Build/website-smoke-config.json` (set `WEBSITE_SMOKE_CONFIG` to override).

## Playground / Blazor link
If the playground is a Blazor WASM app, publish it separately and overlay into the site output:
```
dotnet publish <Playground.csproj> -c Release -o Artifacts/Playground
powerforge-web overlay --source Artifacts/Playground/wwwroot --destination Artifacts/PowerForge.Web.CodeGlyphX.Sample/site/playground --include "**/*"
```

Then link to it in markdown:
```
{{< app src="/playground/" label="Launch Playground" title="CodeGlyphX Playground" >}}
```

Pipeline form (single JSON flow):
```json
{
  "steps": [
    { "task": "build", "config": "./site.json", "out": "../../Artifacts/PowerForge.Web.CodeGlyphX.Sample/site" },
    {
      "task": "dotnet-publish",
      "project": "<Playground.csproj>",
      "out": "../../Artifacts/PowerForge.Web.CodeGlyphX.Sample/playground",
      "configuration": "Release",
      "defineConstants": "TRACE;PLAYGROUND_BUILD"
    },
    {
      "task": "overlay",
      "source": "../../Artifacts/PowerForge.Web.CodeGlyphX.Sample/playground/wwwroot",
      "destination": "../../Artifacts/PowerForge.Web.CodeGlyphX.Sample/site/playground",
      "include": ["**/*"]
    },
    { "task": "optimize", "siteRoot": "../../Artifacts/PowerForge.Web.CodeGlyphX.Sample/site" }
  ]
}
```

## Next steps
- Move old scripts into `powerforge-web pipeline` equivalents.
- Add CI job that runs `publish.json` for release builds.
- Extend the theme tokens to match CodeGlyphX visuals exactly.
