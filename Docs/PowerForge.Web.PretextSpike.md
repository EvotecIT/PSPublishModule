# PowerForge.Web Pretext Spike

Last updated: 2026-04-02

This note answers one question: should PowerForge.Web adopt [`chenglou/pretext`](https://github.com/chenglou/pretext), and if yes, where?

## Short answer

Not as a core engine dependency today.

`pretext` is a JavaScript/TypeScript text measurement and line-layout library, not a markdown parser, templating engine, or static-site pipeline. It is strongest when a browser runtime needs accurate multiline text measurement without triggering DOM reflow. That is valuable, but it does not line up with PowerForge.Web's current core responsibilities.

PowerForge.Web is currently a .NET-first static engine:

- markdown is rendered in-process via [`OfficeIMO.Markdown`](../PowerForge.Web/PowerForge.Web.csproj)
- theme templating is resolved via [`Scriban`](../PowerForge.Web/PowerForge.Web.csproj)
- content templating is routed through `meta.content_engine` and today resolves only to the existing simple/Scriban engines in [`ThemeEngineRegistry.cs`](../PowerForge.Web/Services/ThemeEngineRegistry.cs)
- extension points already exist at the pipeline/theme layer via `hook`, `html-transform`, `data-transform`, `model-transform`, theme assets, and per-page script injection

That makes `pretext` a better fit as an optional theme/runtime integration than as a first-class engine feature.

## What pretext actually provides

Based on the current upstream README:

- install as `@chenglou/pretext`
- primary use case 1: precompute text and cheaply calculate paragraph height for a given width/line height
- primary use case 2: return line data for manual layout (`layoutWithLines`, `walkLineRanges`, `layoutNextLine`)
- it is designed around browser text measurement and explicitly positions itself as "DOM-free" layout work using canvas measurement
- server-side support is described as future-facing rather than a stable present-day contract

Source:

- https://raw.githubusercontent.com/chenglou/pretext/main/README.md
- https://raw.githubusercontent.com/chenglou/pretext/main/DEVELOPMENT.md

## Why it does not belong in the engine core right now

### 1. It does not replace an existing PowerForge.Web core subsystem

`pretext` is not a substitute for:

- markdown rendering in [`MarkdownRenderer.cs`](../PowerForge.Web/Services/MarkdownRenderer.cs)
- shortcodes in [`ShortcodeProcessor.cs`](../PowerForge.Web/Services/ShortcodeProcessor.cs)
- theme rendering in [`ThemeEngineRegistry.cs`](../PowerForge.Web/Services/ThemeEngineRegistry.cs)
- content templating described in [`PowerForge.Web.ContentSpec.md`](./PowerForge.Web.ContentSpec.md)

If we added it to the engine package, it would be a new frontend/runtime concern rather than a better implementation of an existing core abstraction.

### 2. The repo does not currently carry a Node/Bun frontend toolchain

The current engine project is a .NET library targeting `net8.0;net10.0` with package references like `OfficeIMO.Markdown`, `Scriban`, and `HtmlTinkerX` in [`PowerForge.Web.csproj`](../PowerForge.Web/PowerForge.Web.csproj).

There is no repo-level `package.json`/bundler workflow in the current tree. Making `pretext` "official" at the engine level would either:

- introduce a new JS toolchain policy into the engine repo, or
- require shipping vendor JS in assets with a maintenance/update burden

Neither looks justified yet.

### 3. PowerForge.Web already has better-matched extension seams

The engine already supports:

- theme asset bundles and JS injection in [`PowerForge.Web.Theme.md`](./PowerForge.Web.Theme.md)
- per-page/per-site script injection via `{{ assets.js_html }}` and `{{ extra_scripts_html }}` in [`PowerForge.Web.Theme.md`](./PowerForge.Web.Theme.md)
- pipeline escape hatches via `hook`, `html-transform`, `data-transform`, and `model-transform` in [`PowerForge.Web.Pipeline.md`](./PowerForge.Web.Pipeline.md)

So if a site wants `pretext`, we do not need a new engine primitive first.

## Where pretext could actually help

These are the cases where `pretext` is plausibly useful for PowerForge-powered sites.

### Good fit: client-side editorial/search UI

Use `pretext` inside site/theme JS for cases like:

- masonry or mixed-height editorial cards where we want better prediction than CSS-only clamping
- virtualized search results or release catalog grids where row height must be predicted cheaply
- balanced/wrapped hero headlines, pills, or product labels where text length varies heavily by language
- anti-layout-shift behavior when async/localized content arrives after initial paint

This lines up with a known roadmap gap: search UX is still theme-owned rather than standardized in [`PowerForge.Web.Roadmap.md`](./PowerForge.Web.Roadmap.md).

### Maybe later: richer social card line wrapping

PowerForge.Web already generates social cards in-process, but the current generator wraps text by character-count heuristics in [`WebSocialCardGenerator.cs`](../PowerForge.Web/Services/WebSocialCardGenerator.cs), not font-accurate measurement.

That is the one place where `pretext`'s capabilities are conceptually attractive. The problem is deployment shape:

- current social card generation is server-side .NET + Magick
- upstream `pretext` still frames server-side support as future work

So today this is an architectural mismatch, not an implementation candidate.

### Weak fit: docs content rendering

I do not recommend using `pretext` for markdown/docs rendering itself. It does not help with:

- front matter
- markdown AST/rendering
- xref handling
- shortcode expansion
- site navigation/runtime surfaces
- theme contracts

Those are the current engine priorities.

## Recommended adoption model

### Recommendation

Treat `pretext` as an optional theme/runtime enhancement, not a PowerForge.Web engine dependency.

### Practical shape

If we want to explore it, the least risky path is:

1. Add a small website-level proof of concept in one site with a real typography problem.
2. Ship it as theme-local JS/assets, not as a `PowerForge.Web` package dependency.
3. Keep the integration behind a feature flag or body class so fallback behavior remains CSS-only.
4. Measure whether it materially improves CLS, overflow handling, or layout quality before promoting any pattern into starter docs.

### Best candidate spike targets

- a search/results page with mixed-height cards
- a release hub / package catalog tile grid
- a hero/banner component with multilingual headline balancing

I would avoid starting with docs/article body rendering because that gives the least leverage.

## Proposed next step

If we want to use `pretext`'s power without overcommitting, the best next spike is:

Create a tiny theme-level demo page that uses `pretext` only for card-height prediction or balanced hero text, then compare:

- pure CSS clamp/flex/grid baseline
- `pretext`-assisted layout
- complexity added to the site build/runtime

If that demo wins clearly, we can then document an endorsed "advanced typography enhancement" pattern in PowerForge.Web docs without baking the library into the engine.

## Bottom line

PowerForge.Web should not "add Pretext" as though it were a markdown or templating feature.

PowerForge.Web can absolutely utilize `pretext`'s power, but the right boundary is:

- site/theme asset
- optional progressive enhancement
- maybe a starter recipe later

not:

- engine dependency
- core renderer abstraction
- mandatory build/runtime requirement
