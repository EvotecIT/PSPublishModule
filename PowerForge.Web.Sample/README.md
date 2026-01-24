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

## Theme tokens

Theme tokens live in `themes/<name>/theme.json` and are injected via the `theme-tokens` partial:
```html
{{ include "theme-tokens" }}
```
They map to CSS variables (colors, fonts, radius, shadows) so themes can be swapped without editing CSS.
