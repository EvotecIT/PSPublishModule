# Theme Contracts Checklist (What Prevents "Half Cooked")

This is a practical checklist to apply in `theme.manifest.json` `featureContracts`.

## Common Features

### docs

- required layouts:
  - `docs`
- required slots/partials:
  - `header`, `footer`
  - `sidebar` or a documented alternative
- required hooks in layouts:
  - `{{ include "theme-tokens" }}`
  - `{{ extra_css_html }}`, `{{ extra_scripts_html }}`
  - `{{ assets.css_html }}`, `{{ assets.js_html }}`

### apiDocs

- required partials:
  - `api-header`, `api-footer`
- css contract:
  - scan combined CSS that API pages load (usually global + api CSS)
  - require selectors that represent the API layout you rely on

Rule of thumb required selectors (adapt per theme):
- `.pf-api-layout`
- `.pf-api-sidebar`
- `.pf-api-content`
- `.pf-api-search` (or equivalent)

### search

- required routes:
  - `/search/` (if the theme provides a UI)
- required assets:
  - index at `/search/index.json` (engine)
  - theme UI JS and layout

### blog

- required layouts:
  - list layout (section page)
  - single post layout
- outputs:
  - RSS is supported by engine; add Atom/JSON feed later if desired

### 404

- required routes:
  - `/404.html` output or `404` page

## Base Theme Inheritance (extends)

If you use `extends`:

- vendor the base theme into the repo (for example `themes/nova`)
- do not assume it exists globally on a machine
- keep tokens as the design system, not the theme name

