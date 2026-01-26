# PowerForge.Web Sample

This folder is a minimal, self-contained sample content pack for the PowerForge.Web RFC.
It demonstrates site/project specs, front matter, snippets, collection layout, and themes.

Intended use:
- Validate schema shape and content layout decisions
- Provide a concrete example for documentation and early tooling
- Exercise template engines (simple tokens + Scriban)

## Theme engines

Theme engine is selected in `themes/<name>/theme.json`:
```json
{
  "name": "base",
  "engine": "simple"
}
```

### Simple engine
Token replacement + partials:
```html
<title>{{TITLE}}</title>
{{DESCRIPTION_META}}
{{CANONICAL}}
{{ASSET_CSS}}
{{> header}}
{{CONTENT}}
{{> footer}}
{{ASSET_JS}}
```

### Scriban engine
Scriban templates + includes:
```html
<title>{{ page.title }}</title>
{{ description_meta_html }}
{{ canonical_html }}
{{ assets.css_html }}
{{ include "header" }}
{{ content }}
{{ include "footer" }}
{{ assets.js_html }}
```

Scriban context keys:
- `site`, `page`, `content`
- `assets.css_html`, `assets.js_html`, `assets.preloads_html`, `assets.critical_css_html`
- `canonical_html`, `description_meta_html`, `site_name`, `base_url`

## Data files

JSON files under `data/` are loaded and exposed to Scriban as `data`.
Example:
```json
[
  { "title": "Docs at scale", "text": "Generate docs fast." }
]
```
Usage in template:
```html
{{ for feature in data.features }}
  <h3>{{ feature.title }}</h3>
{{ end }}
```

## Shortcodes

Shortcodes let markdown stay clean while rendering rich components:
```
{{< cards data="features" >}}
{{< metrics data="metrics" >}}
{{< showcase data="showcase" >}}
```

Shortcodes can be overridden by theme partials:
`themes/<name>/partials/shortcodes/<name>.html`.

## Navigation

Navigation is defined in `site.json` under `Navigation.Menus` and exposed to Scriban as `navigation`:
```
{{ for item in navigation.menus[0].items }}
  <a href="{{ item.url }}">{{ item.title }}</a>
{{ end }}
```

## Theme tokens

Theme tokens live in `themes/<name>/theme.json` and are injected via the `theme-tokens` partial:
```html
{{ include "theme-tokens" }}
```
They map to CSS variables (colors, fonts, radius, shadows) so themes can be swapped without editing CSS.

## CodeGlyphX sample flows

These configs live in `Samples/PowerForge.Web.CodeGlyphX.Sample` and show how the same site can be built via pipeline or publish specs.

### Pipeline (full, CodeMatrix-aware)
Runs build + apidocs + llms + sitemap + optimize.
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline.json
```

### Pipeline (static only, no CodeMatrix paths)
Builds and optimizes a static site into Artifacts without touching CodeMatrix.
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline-static.json
```

### Pipeline (with playground publish + overlay)
Publishes a Blazor app, overlays it under `/playground/`, then optimizes.
```
powerforge-web pipeline --config Samples/PowerForge.Web.CodeGlyphX.Sample/pipeline-playground.json
```

### Publish spec (build + overlay + dotnet publish + optimize)
Combines build/overlay/publish/optimize in one config. This references CodeMatrix paths by default.
```
powerforge-web publish --config Samples/PowerForge.Web.CodeGlyphX.Sample/publish.json
```

### Publish spec (Artifacts + local sample app)
Same flow without touching CodeMatrix. Publishes to Artifacts using a minimal Blazor host.
```
powerforge-web publish --config Samples/PowerForge.Web.CodeGlyphX.Sample/publish-artifacts.json
```
