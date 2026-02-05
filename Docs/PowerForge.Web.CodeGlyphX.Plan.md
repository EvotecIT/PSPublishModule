# PowerForge.Web CodeGlyphX Migration Plan

## Scope
- Preserve 1:1 live CodeGlyphX routes while replacing the build chain with PowerForge.Web.
- Keep the Blazor app only for `/playground/`.
- Generate static pages for `/docs/` and generate API docs under both `/api` (simple) and `/docs/api` (docs-style).

## Current status
- Parity pipeline runs end-to-end locally.
- Smoke tests pass against `Artifacts/PowerForge.Web.CodeGlyphX.Sample/site`.
- Static content + Blazor overlays now coexist without route collisions.

## Phase 1 — Parity lock (CodeGlyphX)
1) Verify 1:1 content for `/`, `/benchmarks/`, `/faq/`, `/showcase/`, `/pricing/`.
2) Validate static `/docs/` and mirrored `/docs/api` (navigation + deep links).
3) Confirm API docs `/api/` and JSON artifacts (`index.json`, `types/*.json`).
4) Confirm aux files `/robots.txt`, `/sitemap.xml`, `/llms*.txt/json`.
5) Run smoke tests as a required gate before any publish.

## Parity gaps to resolve (checked 2026-01-30)
- `/api/index.json` type count now matches live (375) via explicit type exclusions in the parity pipeline.
- `/docs/api` now uses docs-style static HTML; confirm parity on live links.
- `/docs/` now static; validate SEO + content parity.
- Footer navigation link parity (Pricing/Discord placement) needs explicit confirmation.
- `/sitemap.xml` and `/robots.txt` parity still needs a manual spot check (tool fetch failed).

## Phase 2 — Reusable system
1) Normalize data model (JSON + Markdown dual input) for FAQ/Showcase/Benchmarks/Pricing.
2) Extract shared layouts/components for reuse across projects.
3) Add theme variants (CodeGlyphX first, then 1–2 new themes).
4) Add publish profiles for multi-site output (per-project sites).

## Phase 3 — Evotec.xyz migration
1) Build a temporary PowerForge site for evotec.xyz (parallel to WordPress).
2) Migrate blog content + pages with improved copy/grammar.
3) Validate redirects + SEO metadata parity.
4) Perform one-time cutover once parity is verified.

## Nice-to-haves
- Add a configurable smoke-test step in the pipeline (optional, local-only).
- Add a minimal visual regression check for key pages.
