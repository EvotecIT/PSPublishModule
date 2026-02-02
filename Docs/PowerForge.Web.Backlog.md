# PowerForge.Web Backlog

Short, high-signal list to keep parity and stability work visible.

## Engine parity & quality
- CodeGlyphX 1:1 coverage checklist (home, docs, benchmarks, pricing, showcase, playground, API docs).
- Docs API parity: namespace/type filters, search UX, member grouping (methods/properties/fields/events).
- Fix markdown parser precedence: `## Heading: Text` should be heading, not definition list.
- Syntax highlighting: verify Prism injection for all markdown code blocks and API pages.
- Link consistency: nav parity across all pages + edit links resolve to correct sources.
- Optional: API docs edit links per symbol (XML/CS source mapping) when available.

## Assets & performance
- Asset policy examples for local/CDN/hybrid with rewrites and hashing.
- Cache headers best‑practice defaults per hosting provider (Netlify/Cloudflare Pages).
- Optional: exclude patterns for assets that must stay unhashed (e.g., Prism components).

## Automation & verification
- Built‑in website verification: broken links, missing assets, missing nav parity, CSS/JS load errors.
- Optional Playwright smoke checks (config‑driven, no custom tests required).

## Documentation
- “Theme anatomy” doc: where assets live, how `extra_css_html`/`extra_scripts_html` are used.
- Content spec examples for multi‑site reuse and CodeGlyphX migration steps.
