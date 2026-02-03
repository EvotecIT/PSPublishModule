# PowerForge.Web Backlog

Short, high-signal list to keep parity and stability work visible.

## Recently completed
- API docs: sidebar filters + counts + reset + URL state.
- API docs: sidebar position option + body class hook.
- Audit: navRequired / navIgnorePrefixes options (CLI + pipeline).
- Prism: auto-init highlight + local asset warnings.
- CodeGlyphX sample: apply Prism highlighting and unified code-block styling to docs pages.
- Auto-generate `data/site-nav.json` when missing (navigation export).
- Normalize Markdown code blocks to add `.code-block` on `<pre>` for consistent styling.
- Verify: warn when custom `site-nav.json` drifts from Navigation menus.

## Engine parity & quality
- CodeGlyphX 1:1 coverage checklist (home, docs, benchmarks, pricing, showcase, playground, API docs).
- Docs API parity: namespace/type filters, search UX, member grouping (methods/properties/fields/events).
- Fix markdown parser precedence: `## Heading: Text` should be heading, not definition list.
- Syntax highlighting: verify Prism injection for all markdown code blocks and API pages.
- Prism load policy: auto-include only when code blocks are present; keep pagespeed clean.
- Theme layout hook validation (`extra_css_html` / `extra_scripts_html`) so per-page assets are reliable.
- Prism local asset validation to prevent silent 404s.
- Prism theme overrides (light/dark) so sites can reuse custom palettes.
- Link consistency: nav parity across all pages + edit links resolve to correct sources.
- Optional: API docs edit links per symbol (XML/CS source mapping) when available.
- API docs URLs: choose one canonical scheme (index.html folders vs .html) and update dev server to avoid “download” behavior.
- Docs nav: auto-generate from `docs/` by default, allow manual ordering/overrides, warn on orphaned pages.
- Sitemap: auto-generate from site output + allow manual overrides/priority; warn on duplicates/invalid paths.
- Layout parity: ensure docs, home, and API share the same base background/spacing tokens by default.
- Sidebar options: allow left/right placement (theme-level switch), without forking templates. (done for API docs via `sidebar`)

## Assets & performance
- Asset policy examples for local/CDN/hybrid with rewrites and hashing.
- Cache headers best‑practice defaults per hosting provider (Netlify/Cloudflare Pages).
- Optional: exclude patterns for assets that must stay unhashed (e.g., Prism components).
- Local asset fallback for CDN resources (fonts/Prism) with deterministic paths.

## Automation & verification
- Built‑in website verification: broken links, missing assets, missing nav parity, CSS/JS load errors.
- Optional Playwright smoke checks (config‑driven, no custom tests required).
- Configured audit step in sample pipelines (docs + scaffolder defaults).
- Content diagnostics: warn on missing syntax highlighter assets, missing theme files, or invalid pipeline entries.

## Documentation
- “Theme anatomy” doc: where assets live, how `extra_css_html`/`extra_scripts_html` are used.
- Content spec examples for multi‑site reuse and CodeGlyphX migration steps.
- Prism local asset expectations + troubleshooting (docs/assets).
- Docs nav rules (auto + manual), sidebar placement options, and relative link resolution.
- Sitemap behavior (auto + manual entries) with examples.
