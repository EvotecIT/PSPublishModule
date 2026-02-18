# PowerForge.Web SEO Parity Plan (Yoast-Informed)

Last updated: 2026-02-18

This document maps common Yoast SEO capabilities to PowerForge.Web and defines what we should build next.

Goal: keep PowerForge strong on technical SEO while adding editorial SEO guidance and better discovery tooling for product/docs/blog-heavy sites.

## Scope

- Compared against Yoast feature areas (free + premium style workflows), not WordPress UI specifics.
- Focused on engine-level and pipeline-level capabilities we can support across all PowerForge websites.

## Capability Matrix

Legend:
- Have: implemented and usable now.
- Partial: possible today, but incomplete or theme-dependent.
- Missing: not implemented in engine/pipeline.

| Capability area | Yoast-style expectation | PowerForge status | Notes |
|---|---|---|---|
| Canonical + robots basics | Canonical tags, noindex handling | Have | Canonical/meta already emitted; sitemap excludes noindex by default. |
| XML sitemap generation | Auto sitemap with core URLs | Have | `sitemap` task supports XML + optional HTML/JSON output and alternates. |
| Social cards/meta | OG/Twitter metadata + defaults | Have | Social metadata + generated social cards already available. |
| Schema baseline | WebSite/Organization/Article/Breadcrumb | Have | Structured data baseline exists. |
| Redirect management | Easy redirects + migration safety | Partial | Strong redirect emitters exist; no smart suggestion workflow yet. |
| Search appearance templates | SEO title/meta templates with variables | Have | `Seo.Templates` supports tokenized title/description with collection overrides and `_powerforge/seo-preview.json`. |
| Content SEO analysis | Focus keyphrase, snippet checks, content hygiene | Missing | No built-in author-facing SEO scoring/checklist. |
| Internal linking suggestions | Link recommendations + orphan detection | Missing | We lint nav/docs structure but no content link-graph advisor. |
| Schema breadth | FAQ/HowTo/Product/Software/News schemas | Missing | Baseline schema only; no richer schema profiles yet. |
| Vertical sitemaps | News/Image/Video sitemap modes | Partial | `sitemap` supports news sitemap + sitemap index; image/video variants are still pending. |
| Fast indexing ping | IndexNow support | Have | `indexnow` pipeline step supports batching/retry/dry-run/reporting and changed-file scoping. |
| Crawl controls UX | Fine-grained crawl/index directives | Have | `Seo.CrawlPolicy` supports route-scoped robots directives, bot directives, and `_powerforge/crawl-policy.json` diagnostics. |

## What We Should Build Next

### M1: SEO Doctor (Content + Technical Guidance)

Deliverables:
- New pipeline step: `seo-doctor`
- Report output: JSON + Markdown summary with severity and fix hints
- Checks:
  - title length + description length heuristics
  - missing/multiple H1
  - weak internal linking / orphan candidates
  - missing image alt (configurable)
  - duplicate/near-duplicate slug/title warnings
  - optional focus keyphrase checks from front matter

Why first:
- Highest impact for editorial quality and organic discoverability.
- Enables CI gating similar to current `verify`/`audit`.

### M2: Search Appearance Templates + Preview Model (Completed)

Deliverables:
- Site-level and collection-level SEO template fields in `site.json`:
  - `Seo.Templates.Title`
  - `Seo.Templates.Description`
  - token support: `{title}`, `{site}`, `{collection}`, `{date}`, `{project}`, `{lang}`
- Resolved preview payload per page in `_powerforge/seo-preview.json`
- Optional HTML preview artifact for maintainers.

Why:
- Aligns with Yoast snippet workflow while staying static-site friendly.

### M3: Structured Data Kit (Completed - Baseline)

Deliverables:
- Expand `StructuredDataSpec` with opt-in profiles:
  - `FaqPage`, `HowTo`, `SoftwareApplication`, `Product`, `NewsArticle`
- Per-page override metadata for profile payload inputs
- Verification rules:
  - baseline required-field checks per schema profile
  - schema/data consistency warnings

Why:
- Rich results coverage for docs/products/news pages.

### M4: Extended Sitemap Family (Partial)

Deliverables:
- Implemented:
  - `sitemap` step can emit `newsOut` (Google News sitemap) and `sitemapIndex`.
  - route-filtering for news entries via `newsPaths`.
  - optional publication metadata via `newsMetadata`.
- Remaining:
  - image/video sitemap variants.
  - richer collection-aware defaults for media-heavy routes.

Why:
- Better crawl targeting for large multi-surface sites.

### M5: Discovery + Crawl Controls

Deliverables:
- `indexnow` step (implemented):
  - submit changed canonical URLs after deploy
  - retry + batching + dry-run + report/summary output
- Crawl policy block in `site.json`:
  - named bot directives
  - route-scoped index/follow defaults
  - explicit policy export for diagnostics

Why:
- Completes technical SEO loop beyond passive sitemap discovery.

### M6: Redirect Assistant

Deliverables:
- Delta-aware redirect suggestion tool:
  - compare old/new route manifests
  - suggest 301 map candidates
  - emit reviewable patch JSON before apply
- Integrate with existing redirect artifact emitters.

Why:
- Safer migrations (especially WordPress/legacy blog imports).

## Proposed Spec Additions

Site spec (new block):
- `Seo`
  - `Enabled`
  - `Templates`
  - `FocusKeyphraseField` (default front matter key)
  - `Doctor`
  - `CrawlPolicy`

Pipeline spec (new steps):
- `seo-doctor`
- `indexnow`
- optional specialized sitemap steps (or `sitemap` sub-modes)

## Quality Gates

- `seo-doctor` supports baselines + fail-on-new pattern (same contract style as `verify`/`audit`).
- CI defaults:
  - fail on new high-severity findings
  - warn on medium/low unless threshold exceeded.

## Non-Goals

- Recreating WordPress plugin UI inside engine.
- Hardcoding a single scoring model as universal truth.
- Coupling SEO logic to one specific theme.

## Suggested Execution Order

1. Ship `seo-doctor` with deterministic editorial + technical checks (done; continue expanding fix hints/scoring).
2. Add template/token resolution and preview artifact.
3. Expand structured data profiles with validation checks. (completed; continue tightening rules over time)
4. Add specialized sitemap outputs + index. (news + index completed; image/video pending)
5. Add crawl policy model. (completed)
6. Add redirect assistant for migration-heavy sites.

## References

- Yoast plugin feature baseline: https://wordpress.org/plugins/wordpress-seo/
- Yoast product features: https://yoast.com/wordpress/plugins/seo/
- Snippet variables: https://yoast.com/help/snippet-variables-yoast-seo/
- Readability analysis: https://yoast.com/features/readability-analysis/
- SEO analysis/keyphrase: https://yoast.com/features/seo-analysis/
- XML sitemaps: https://yoast.com/help/xml-sitemaps-in-the-wordpress-seo-plugin/
- Schema graph: https://yoast.com/help/how-does-yoast-seo-generate-its-schema-graph/
- Crawl settings: https://yoast.com/help/crawl-settings/
