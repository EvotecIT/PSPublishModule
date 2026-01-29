# PowerForge.Web Theme System (Draft)

This document defines the theme system as a reusable, product-grade layer for PowerForge.Web.
Goals are consistency, performance, and ease of reuse across many projects.

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

## Theme manifest (theme.json)
Theme manifest defines identity, engine, inheritance, and assets.
Schema: `Schemas/powerforge.web.themespec.schema.json`.
```json
{
  "name": "codeglyphx",
  "version": "1.0.0",
  "author": "Evotec",
  "engine": "scriban",
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

### Manifest rules
- `extends` is optional. If set, theme inherits layouts/partials/assets/tokens from base.
- Child theme overrides anything it redefines.
- `assets` is merged: bundles with same name are replaced by child.
- `tokens` are merged with child values winning.
- `defaultLayout` applies when content has no layout.

## Theme engines
Two engines are supported:
- **simple**: token replacement + partials (`{{TITLE}}`, `{{CONTENT}}`, `{{> header}}`).
- **scriban**: full templates with includes and data.

Engine selection order:
1) Theme `theme.json` `engine`
2) Site `site.json` `ThemeEngine` override (optional)

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
- `breadcrumbs` (array of `{ title, url, is_current }`)

Example:
```html
<nav>
  {{ for item in navigation.menus[0].items }}
    <a href="{{ item.url }}" class="{{ if item.is_active }}is-active{{ end }}">{{ item.title }}</a>
  {{ end }}
</nav>
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
