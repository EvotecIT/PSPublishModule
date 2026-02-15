# PowerForge.Web Theme System (Contract v2)

This document defines the theme system as a reusable, product-grade layer for PowerForge.Web.
Goals are consistency, performance, and ease of reuse across many projects.
See also: `Docs/PowerForge.Web.Assets.md` for asset policy, hashing, and cache headers.

Contract status:
- `theme.manifest.json` is the canonical theme contract (`theme.json` is still supported as legacy fallback).
- Relative paths in the theme manifest are required for portability.
- Theme assets should be declared in theme-local form and normalized by the engine.
- `schemaVersion: 2` enables stricter, reusable theme checks (recommended for new themes).

## Goals
- Themes are portable across projects and repos.
- One theme can drive multiple site types (product, docs, blog, api).
- Consistent asset policy (CSS/JS/preloads/critical CSS).
- Theme inherits from a base theme for common layouts + components.
- Works with simple tokens and Scriban without code changes.

## Theme package layout
```
themes/
  base/
    theme.json
    layouts/
      base.html
      page.html
      docs.html
      post.html
      api.html
    partials/
      header.html
      footer.html
      theme-tokens.html
    assets/
      app.css
      site.js
    critical.css

  codeglyphx/
    theme.json
    layouts/
      home.html
      showcase.html
    partials/
      header.html
      footer.html
    assets/
      app.css
      site.js
    critical.css
```

## Theme manifest (`theme.manifest.json`)
Theme manifest defines identity, engine, inheritance, and assets.
Schema: `Schemas/powerforge.web.themespec.schema.json`.
```json
{
  "name": "codeglyphx",
  "schemaVersion": 2,
  "version": "1.0.0",
  "author": "Evotec",
  "engine": "scriban",
  "features": ["docs", "apiDocs", "blog", "search"],
  "extends": "base",
  "defaultLayout": "page",
  "layouts": {
    "home": "layouts/home.html",
    "docs": "layouts/docs.html",
    "post": "layouts/post.html"
  },
  "partials": {
    "header": "partials/header.html",
    "footer": "partials/footer.html",
    "theme-tokens": "partials/theme-tokens.html"
  },
  "slots": {
    "hero": "partials/slots/hero.html",
    "cta": "partials/slots/cta.html",
    "footer-extra": "partials/slots/footer-extra.html"
  },
  "assets": {
    "bundles": [
      { "name": "global", "css": ["assets/app.css"], "js": ["assets/site.js"] }
    ],
    "routeBundles": [
      { "match": "/**", "bundles": ["global"] }
    ],
    "preloads": [
      { "href": "/themes/codeglyphx/assets/app.css", "as": "style" }
    ],
    "criticalCss": [
      { "name": "base", "path": "critical.css" }
    ]
  },
  "tokens": {
    "fontBody": "Manrope, system-ui, sans-serif",
    "fontDisplay": "Space Grotesk, system-ui, sans-serif",
    "colorBg": "#0b0f1a",
    "colorText": "#e2e8f0",
    "radius": "16px"
  }
}
```

## Feature Contracts (catch regressions across sites)
If you want "DocFX/Hugo-tier" predictability across multiple sites, add `featureContracts` to your theme manifest.
This allows the engine to verify that when a site enables a feature (e.g. `apiDocs`), the theme actually provides
the expected layouts/partials/slots and that your CSS contains critical selectors.

Example:
```json
{
  "name": "mytheme",
  "schemaVersion": 2,
  "engine": "scriban",
  "features": ["docs", "apiDocs"],
  "featureContracts": {
    "apiDocs": {
      "requiredPartials": ["api-header", "api-footer"],
      "requiredCssSelectors": [".api-layout", ".api-sidebar", ".api-content"]
    },
    "docs": {
      "requiredLayouts": ["docs"]
    }
  }
}
```
Notes:
- `requiredPartials` and `requiredLayouts` are validated using theme resolution (including `extends`).
- `requiredSlots` ensures `slots.<name>` exists and resolves to a real partial file.
- `requiredSurfaces` requires the site to define `Navigation.Surfaces` explicitly; verify emits a theme-contract warning when surfaces are required but missing.
- `requiredCssSelectors` is validated by scanning local CSS files:
  - if `cssHrefs` is provided, those hrefs are scanned
  - otherwise, the engine infers CSS from route bundles for representative routes (`/docs/`, `/api/`, `/blog/`)
  - remote CSS (`http/https`) is skipped (best-effort)

### Manifest rules
- `schemaVersion` defaults to `1`; use `2` for strict portable contract validation.
- Legacy `contractVersion` is still read for compatibility, but new themes should use `schemaVersion`.
- `extends` is optional. If set, theme inherits layouts/partials/assets/tokens from base.
- Child theme overrides anything it redefines.
- `assets` is merged: bundles with same name are replaced by child.
- `tokens` are merged with child values winning.
- `defaultLayout` applies when content has no layout.
- `engine` should always be explicit (`simple` or `scriban`) to avoid ambiguous rendering.
- `layoutsPath`, `partialsPath`, `assetsPath`, mapped `layouts`/`partials`, and theme `assets` bundle paths should be relative (not rooted paths, no `..`).
- `slots` map named hook points to partial files. Layouts can render these hooks consistently across themes.
- For `schemaVersion: 2`, set `defaultLayout`, `scriptsPath`, and explicit `slots` to maximize theme portability.

## Theme Dependency Model (Base Theme + Product Theme)

PowerForge.Web theme inheritance is **filesystem-only** and intentionally simple:

- A theme can optionally `extends` another theme.
- The base theme is resolved relative to the current theme folder (sibling under the same `themes/` root by default).
- There is no package manager or remote resolution: if you want shared behavior, you typically **vendor** the base theme into each website repo.

Pragmatic best practice for multi-site setups:

- Keep a small number of base themes (ideally 1).
- Keep product themes thin: branding, a few layouts/partials overrides, and token overrides.
- Use verify/audit contracts to stop silent drift across sites.

### Tokens and CSS Variable Naming (Avoid Theme-Name Coupling)

The engine exposes design tokens at `data.theme.tokens` (merged across `extends`).
Themes then map those tokens into CSS variables, typically via a `theme-tokens` partial.

To reduce surprises when you rename or swap base themes:

- Prefer a **stable** CSS variable prefix like `--pf-*` for your design system contract.
- Avoid using the base theme name as the prefix (for example `--nova-*`) as the long-term contract, because it couples CSS to the theme folder name.

You can keep backwards compatibility by emitting aliases in `theme-tokens`:

```css
/* recommended: stable contract */
--pf-accent: {{ data.theme.tokens.color.accent }};

/* optional: legacy alias for older CSS */
--nova-accent: var(--pf-accent);
```

## Asset copying + output paths
PowerForge.Web copies theme assets during `build`:
- Source: `<themeRoot>/<assetsPath>/...` (defaults to `assets/`)
- Output: `/<themesFolder>/<themeName>/<assetsPath>/...`
  (default `themes/` folder unless `site.json` sets `ThemesRoot`)

Example mapping (default settings):
```
themes/intelligencex/assets/site.css
=> /themes/intelligencex/assets/site.css
```

If a theme extends another theme, **both** themes’ assets are copied:
```
/themes/base/assets/...
/themes/intelligencex/assets/...
```

## How asset URLs are resolved
Prefer the asset registry helpers instead of hard-coded `<link>` tags:
- `{{ assets.css_html }}` injects the correct CSS for the current route.
- `{{ assets.js_html }}` injects the correct JS for the current route.

When you must hard-code an asset in a layout/partial, use absolute paths:
```
<link rel="stylesheet" href="/themes/intelligencex/assets/site.css" />
```
Avoid relative paths like `assets/site.css` because they resolve relative to the
current page URL and will break on nested routes.

### Asset registry rules (theme.json)
Asset paths in theme bundles are **relative to the theme root**:
```
"bundles": [
  { "name": "global", "css": ["assets/site.css"], "js": ["assets/site.js"] }
]
```
During build, these resolve to:
```
/themes/<themeName>/assets/site.css
```

Do not hard-code `/themes/<theme>/...` inside `theme.json` bundle paths. Keep those paths relative and let the engine normalize output URLs.

Critical CSS paths are also relative to the theme root:
```
"criticalCss": [{ "name": "base", "path": "critical.css" }]
```

## Common pitfalls (and how to avoid them)
- **CSS not loading**: you used a relative URL in a layout.
  Fix: use `{{ assets.css_html }}` or absolute `/themes/<theme>/...` links.
- **Wrong theme path**: `site.json` has `ThemesRoot` and you hard-coded `/themes/...`.
  Fix: use the asset registry (recommended) so the engine resolves paths.

## Required layout hooks
To keep features reusable across projects, layouts should include:
- `{{ extra_css_html }}` in `<head>` so the engine can inject per‑page CSS (syntax highlighting, experiments, analytics styles).
- `{{ extra_scripts_html }}` before `</body>` so the engine can inject per‑page scripts (syntax highlighting, analytics, widgets).

For the **simple** engine, the equivalent placeholders are `{{EXTRA_CSS}}` and
`{{EXTRA_SCRIPTS}}`. `powerforge-web verify` warns when these hooks are missing,
because features like Prism injection rely on them.

Recommended base ordering:
```
{{ assets.critical_css_html }}
{{ include "theme-tokens" }}
{{ extra_css_html }}
{{ assets.css_html }}
```
```
{{ assets.js_html }}
{{ extra_scripts_html }}
```

## Verification (catch broken asset paths)
`powerforge-web verify` warns when asset files referenced in:
- Theme asset registry (`theme.json` → `assets`)
- Site asset registry (`site.json` → `AssetRegistry`)
are missing on disk.

## Theme engines
Two engines are supported:
- **simple**: token replacement + partials (`{{TITLE}}`, `{{CONTENT}}`, `{{> header}}`).
- **scriban**: full templates with includes and data.

Engine selection order:
1) Site `site.json` `ThemeEngine` override (if set)
2) Theme `theme.json` `engine`
3) Default fallback: `simple`

## Layout resolution
When rendering a page:
1) Use front matter `layout` if set.
2) Otherwise use collection `DefaultLayout`.
3) Otherwise use theme `defaultLayout`.
4) If layout is missing, fallback to base theme `page`.

## Partials + components
Partials are shared building blocks (header, footer, nav, cards, tabs).
They should be pure HTML with placeholders or Scriban logic.
Use `partials/` and name them explicitly in `theme.json` or by convention.

Recommended conventions:
- Render navigation from `navigation.menus` instead of hard‑coding links.
- Render favicons/preconnects from `head_html` (or `site.head` structured links).
- Keep product‑specific URLs in `site.json` so themes stay reusable.

## Asset registry
Theme assets should map to site asset registry with a clear override order:
1) Site `AssetRegistry` (authoritative, can override any theme bundle)
2) Theme `assets`
3) Base theme `assets`

This keeps performance decisions centralized and consistent.

## Design tokens
Tokens map to CSS variables and are exposed via the `theme-tokens` partial.
The partial should emit variables, not layout.
Example:
```html
<style>
:root {
  --font-body: {{ theme.tokens.fontBody }};
  --color-bg: {{ theme.tokens.colorBg }};
}
</style>
```

## Data + shortcodes
Data files are loaded from `data/` (site + project).
Shortcodes should be theme-agnostic and render via partials.
Recommended shortcodes: `cards`, `metrics`, `showcase`, `cta`, `faq`.
Project data is available under:
- `data.projects.<slug>` (all project data)
- `data.project` (current project's data when rendering that project)

Media shortcodes (built-in):
- `{{< media ... >}}` generic provider wrapper (`youtube`, `video`, `iframe`, `x`, `screenshot`, `screenshots`)
- `{{< youtube id=\"...\" start=\"15\" size=\"lg\" >}}`
- `{{< x url=\"https://x.com/<user>/status/<id>\" size=\"md\" >}}` (alias: `tweet`)
- `{{< screenshot src=\"/img/feature.png\" caption=\"Dark mode\" size=\"md\" >}}`
- `{{< screenshots data=\"media.shots\" layout=\"grid|masonry|strip|stack\" columns=\"3\" >}}`

Screenshot sizing notes:
- `size`: `xs|sm|md|lg|xl|full` (defaults to `lg` for single screenshot, `xl` container for galleries)
- `align`: `left|center|right`
- Optional dimensions (`width`, `height`, `ratio`) improve aspect-ratio stability and reduce layout shift.
- Responsive image attributes are supported: `srcset`, `sizes`, `loading`, `decoding`, `fetchpriority`.

Media performance notes:
- `youtube` defaults to **lite mode** (`lite="true"`): thumbnail + play button hydrates iframe on interaction.
- Set `lite="false"` when immediate iframe render is required.
- `x`/`tweet` embeds inject a single per-page bootstrap script and lazy-load the X widget when embeds approach viewport.
- Media shortcodes inject a small shared CSS baseline once per page (`extra_css`), so base behavior stays consistent across themes.

Edit links:
- When `site.json` defines `EditLinks`, pages expose `page.edit_url`.
- Use `{{< edit-link >}}` in markdown to render a consistent "Edit on GitHub" block.

### Linking apps (Blazor or otherwise)
Themes can expose `app` (generic) and `blazor` shortcodes that link to a published app (typically hosted under `/playground/`).
Example markdown:
```
{{< app src="/playground/" label="Launch Playground" title="Playground" >}}
```

Recommended hosting flow:
1) Publish the Blazor app to a folder (e.g., `Artifacts/Playground`).
2) Overlay the publish output into the static site output (e.g., `/playground/`).
3) Use the shortcode above or a normal link to `/playground/`.

### Shortcode rendering
Shortcodes are resolved in this order:
1) Registered handler (code)
2) Theme partial `shortcodes/<name>.html` (if present)

When a shortcode is rendered via a partial, Scriban receives:
- `shortcode.name`
- `shortcode.attrs` (dictionary)
- `shortcode.data` (resolved list from `data="..."` if provided)

Example partial (`partials/shortcodes/cards.html`):
```html
<div class="pf-grid">
  {{ for card in shortcode.data }}
    <div class="pf-card">
      <h3>{{ card.title }}</h3>
      <p>{{ card.text }}</p>
    </div>
  {{ end }}
</div>
```

## Project overrides
Projects can provide:
- `themes/<project>/` to override layouts/partials/assets
- `project.json` tokens to override theme tokens
- `project.json` menus/links

## Navigation + breadcrumbs
Navigation and breadcrumbs are computed at render time:
- `navigation.menus` (active/ancestor flags included)
- `navigation.actions` (header buttons/icons)
- `navigation.regions` (named slots with resolved items)
- `navigation.footer` (columns + legal links)
- `navigation.active_profile` (selected profile name, when applicable)
- `breadcrumbs` (array of `{ title, url, is_current }`)

Example:
```html
<nav>
  {{ for item in navigation.menus[0].items }}
    <a href="{{ item.url }}" class="{{ if item.is_active }}is-active{{ end }}">{{ item.title }}</a>
  {{ end }}
</nav>
```

Header actions example:
```html
<div class="nav-actions">
  {{ if navigation.actions && navigation.actions.size > 0 }}
    {{ for action in navigation.actions }}
      {{ if action.kind == "button" }}
        <button type="button" class="{{ action.css_class }}">{{ action.title }}</button>
      {{ else }}
        <a href="{{ action.url }}" class="{{ action.css_class }}">{{ action.title }}</a>
      {{ end }}
    {{ end }}
  {{ end }}
</div>
```

Regions example:
```html
{{ for region in navigation.regions }}
  {{ if region.name == "header.right" }}
    <div class="nav-right">
      {{ for item in region.items }}
        <a href="{{ item.url }}">{{ item.title }}</a>
      {{ end }}
    </div>
  {{ end }}
{{ end }}
```

Footer example:
```html
{{ if navigation.footer }}
  <footer>
    {{ for column in navigation.footer.columns }}
      <section>
        <h3>{{ column.title || column.name }}</h3>
        <ul>
          {{ for link in column.items }}
            <li><a href="{{ link.url }}">{{ link.title }}</a></li>
          {{ end }}
        </ul>
      </section>
    {{ end }}
  </footer>
{{ end }}
```

## Localization runtime
When `site.json` defines `Localization`, templates receive:
- `localization` (resolved runtime object)
- `languages` (shortcut to `localization.languages`)
- `current_language` (shortcut to `localization.current`)

Each language entry includes:
- `code`, `label`, `prefix`
- `is_default`, `is_current`
- `url` (resolved URL for the current page in that language)

Example:
```html
{{ if localization.enabled && languages.size > 1 }}
  <nav aria-label="Language selector">
    {{ for lang in languages }}
      <a href="{{ lang.url }}" {{ if lang.is_current }}aria-current="page"{{ end }}>
        {{ lang.label }}
      </a>
    {{ end }}
  </nav>
{{ end }}
```

## Versioning runtime
When `site.json` defines `Versioning`, templates also receive:
- `versioning` (resolved runtime model)
- `versions` (shortcut to `versioning.versions`)
- `current_version`
- `latest_version`
- `versioning.lts` (when configured)

Each entry includes `name`, `label`, `url`, `default`, `latest`, `lts`, `deprecated`, `is_current`.

Example:
```html
{{ if versioning.enabled && versions.size > 0 }}
  <select onchange="location.href=this.value">
    {{ for v in versions }}
      <option value="{{ v.url }}" {{ if v.is_current }}selected{{ end }}>
        {{ v.label }}{{ if v.latest }} (latest){{ end }}
      </option>
    {{ end }}
  </select>
{{ end }}
```

## Output runtime (feeds/json variants)
Templates receive output metadata for the current page:
- `outputs` (array of `{ name, url, media_type, rel, is_current }`)
- `feed_url` (resolved preferred feed URL when available: RSS, then Atom, then JSON Feed)

The engine also injects `<link rel=\"alternate\" ...>` tags for non-HTML outputs into `<head>`.

Example:
```html
{{ if feed_url }}
  <a href="{{ feed_url }}">RSS</a>
{{ end }}
```

## List pages + taxonomies
Section pages (`_index.md`) and taxonomy pages expose extra data in Scriban:
- `items`: list of child pages (for sections/taxonomies/terms)
- `taxonomy`: taxonomy spec (when rendering taxonomy/term pages)
- `term`: current term (string)

Example list template:
```html
<ul>
  {{ for page in items }}
    <li><a href="{{ page.output_path }}">{{ page.title }}</a></li>
  {{ end }}
</ul>
```

## Performance rules (theme responsibility)
- `critical.css` is required for above-the-fold content.
- All scripts are deferred (no blocking scripts).
- Fonts are preloaded (only critical weights).
- Images must include width/height defaults in layout components.

## Versioning and distribution
- Themes can live in repo `themes/` or in a NuGet package.
- If packaged, `theme.json` sits at package root.
- Engine should read from disk or package transparently.

## Minimal theme checklist
- `theme.json`
- `layouts/base.html` or `layouts/page.html`
- `partials/header.html` + `partials/footer.html`
- `assets/app.css`
- `critical.css`
