# PowerForge.Web Asset Policy & Performance

This document defines how PowerForge.Web manages CSS/JS delivery for fast,
cache‑friendly sites with minimal payload per route.

## Goals
- Only load assets needed for the current route.
- Prefer local assets when required (offline/locked environments).
- Support CDN for global caching when allowed.
- Enable cache‑friendly filenames (hashing).
- Emit cache headers for hosts that support `_headers`.

## Asset policy (site.json)
```json
{
  "AssetPolicy": {
    "Mode": "local",
    "Hashing": {
      "Enabled": true,
      "Extensions": [".css", ".js"],
      "Exclude": ["**/nohash/**"],
      "ManifestPath": "asset-manifest.json"
    },
    "CacheHeaders": {
      "Enabled": true,
      "OutputPath": "_headers"
    },
    "Rewrites": [
      {
        "Match": "https://cdn.example.com/prism/",
        "Replace": "/assets/prism/",
        "MatchType": "prefix",
        "Source": "./themes/intelligencex/assets/prism-core.js",
        "Destination": "/assets/prism/prism-core.js"
      }
    ]
  }
}
```

### AssetPolicy.Mode
- `local` — prefer local assets (no CDN).
- `cdn` — prefer CDN assets (best global cache).
- `hybrid` — prefer local when files exist, otherwise CDN.

This is used by built‑in features like Prism auto‑injection.

## Route‑level asset loading
Use the asset registry to keep CSS/JS minimal per route.
Example in `theme.json`:
```json
{
  "assets": {
    "bundles": [
      { "name": "global", "css": ["assets/site.css"], "js": ["assets/site.js"] },
      { "name": "docs", "css": ["assets/docs.css"] }
    ],
    "routeBundles": [
      { "match": "/**", "bundles": ["global"] },
      { "match": "/docs/**", "bundles": ["docs"] }
    ]
  }
}
```

## Prism syntax highlighting
Prism can be injected automatically when code blocks are detected.
Configuration lives in `site.json`:
```json
{
  "Prism": {
    "Mode": "auto",
    "Source": "local",
    "Local": {
      "ThemeLight": "/assets/prism/prism.css",
      "ThemeDark": "/assets/prism/prism-okaidia.css",
      "Core": "/assets/prism/prism-core.js",
      "Autoloader": "/assets/prism/prism-autoloader.js",
      "LanguagesPath": "/assets/prism/components/"
    }
  }
}
```

Front matter overrides (optional):
```yaml
prism: false
prism_mode: always
prism_source: cdn
prism_cdn: https://cdn.jsdelivr.net/npm/prismjs@1.29.0
```

## Asset hashing
Hashing renames assets and rewrites HTML/CSS references, enabling immutable
cache headers without stale files.

Enable via `AssetPolicy.Hashing` or the `optimize` step:
```json
{ "task": "optimize", "siteRoot": "./_site", "hashAssets": true }
```

Output:
- `main.css` → `main.ab12cd34.css`
- `asset-manifest.json` for traceability

## Cache headers
When enabled, the optimizer writes a `_headers` file:
```
/*
  Cache-Control: public, max-age=0, must-revalidate

/assets/*
  Cache-Control: public, max-age=31536000, immutable
```

This works on Netlify and Cloudflare Pages.

## External asset rewrites
Use `AssetPolicy.Rewrites` to replace external URLs with local equivalents.
This is useful for turning CDN links into local files.

Supported `MatchType`: `contains` (default), `prefix`, `exact`, `regex`.

## Best practices for 100/100/100
- Keep CSS/JS route‑specific (use `routeBundles`).
- Hash assets and serve them with immutable cache headers.
- Avoid injecting libraries on pages that don’t need them.
- Preload only critical CSS (avoid unnecessary preloads).
- Minify HTML/CSS/JS during optimize.
