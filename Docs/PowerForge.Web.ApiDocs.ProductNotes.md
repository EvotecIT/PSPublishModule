# PowerForge.Web API Docs Product Notes

Last updated: 2026-04-02

This note captures the product-level gaps that show up when API reference pages are technically correct but still leave a reader asking:

- "Where do I start?"
- "What uses this type?"
- "Which walkthrough teaches this?"
- "How do I browse multiple related APIs without jumping between disconnected roots?"

## What PowerForge already does well

- Strong raw symbol extraction for C# and PowerShell APIs.
- Theme-aware API pages that can reuse site chrome and navigation.
- Source links, freshness, coverage, xref map generation, and PowerShell example provenance.
- Project-scoped API publishing via `project-apidocs`.
- Inferred reverse usage relationships:
  - each type page can now answer where the type is returned/exposed/accepted in the generated public surface.

## Main gap categories

### 1. Reference pages still need conceptual help

API reference is great at:

- exact signatures
- parameters and return values
- inheritance and related symbols
- source provenance

API reference is weak at:

- intent ("when should I pick this type?")
- sequence ("what order do I call these methods in?")
- recipes ("show me a real chart/email/report setup")
- heuristics ("use this option for X, avoid it for Y")

Reverse usage helps, but it is not a substitute for curated guides and examples.

### 2. Example correlation now exists, but editorial coverage is still too manual

Today we mostly rely on:

- XML `<example>` blocks
- PowerShell help examples
- imported PowerShell example scripts
- docs pages that happen to mention a symbol

This branch adds a first-class manifest contract saying:

- this guide teaches these types/members
- this snippet belongs to these APIs
- this project-level example should appear on these detail pages

What is still missing is the workflow around it:

- starter guidance for when sites should create those manifests
- CI rules for key APIs that should have curated walkthroughs
- broader attachment models for conceptual docs that are not yet in the manifest

## Comparison notes from other ecosystems

These are not "copy them exactly" recommendations, but they do show the missing concepts clearly.

### DocFX

- DocFX explicitly combines conceptual Markdown with API reference and supports API enrichments through overwrites.
- It also builds around stable API identifiers and cross-reference resolution.
- Product takeaway for PowerForge:
  - we already cover much of the symbol/xref side
  - we still need a cleaner conceptual-to-API attachment model

References:

- [DocFX Basic Concepts](https://dotnet.github.io/docfx/docs/basic-concepts.html)

### TypeDoc

- TypeDoc supports additional project documents (`projectDocuments`) so tutorials and narrative docs live beside API reference.
- Its `packages` and `merge` entry point strategies support multi-package sites that still feel like one API surface.
- Product takeaway for PowerForge:
  - we need a first-class API suite mode, not only multiple separate `apidocs` outputs

References:

- [TypeDoc Input Options](https://typedoc.org/documents/Options.Input.html)

### Doxygen

- Doxygen supports explicit topics/groups and member groups, which lets maintainers organize symbols by meaning rather than only by syntax.
- Product takeaway for PowerForge:
  - we should support curated API groups for "builders", "entry points", "formatters", "transport", "reporting", etc.

References:

- [Doxygen Grouping](https://www.doxygen.nl/manual/grouping.html)

## Recommended PowerForge direction

### 1. Keep strengthening inferred relationships

Low-cost, engine-owned signals we can infer automatically:

- where a type is accepted by parameters
- where a type is returned or exposed
- who derives from a base type
- who implements an interface
- extension methods targeting a type

This is safe to automate because it is structural, not editorial.

### 2. Expand the curated example-correlation model

Current shipped contract:

- site/project can provide an example manifest
- entries can target:
  - type full names
  - member ids / xref ids
  - command names / parameter ids
- rendered API pages can then show:
  - "Learn by example"
  - "Walkthroughs using this type"
  - "Recipes mentioning this command"

Recommended next extension:

- support merge-friendly suite manifests across multiple projects
- support command/parameter-specific attachment rules for PowerShell beyond basic type/member targeting
- add optional quality-gate inputs so important symbols can require curated attachments

Potential artifact shape:

- generated or curated JSON under `data/apidocs/examples/*.json`
- merge-friendly so `project-docs-sync` can stage external docs/examples into one site

### 3. Add first-class API suite support

Current state:

- one site can publish several API roots
- `project-apidocs` can publish per-project API under `/projects/<slug>/api/`

Still missing:

- one unified API home covering many projects
- one search/filter surface with project/module selectors
- a project switcher inside the API UI itself
- suite-level coverage/xref/search defaults

Recommended future model:

- `apiSuite` or batch-aware `apidocs` config that declares:
  - projects/packages/modules
  - labels/icons/order
  - shared output root
  - shared search index
  - merged xref map
  - optional per-project overview/learn links

### 4. Add curated API groups

A page organized only by symbol kind answers "what members exist?"
It does not answer "what clusters belong together?"

Recommended future capability:

- optional curated groups per type or project:
  - Entry points
  - Fluent setup
  - Data sources
  - Output/rendering
  - Advanced diagnostics

These should complement, not replace, the raw Methods/Properties/Fields sections.

### 5. Add quality gates for relevance, not only completeness

Coverage today focuses on presence:

- summary
- remarks
- examples
- source links

Future API-doc quality gates should also check for relevance signals:

- missing curated examples for configured entry-point types
- builder/configuration types with no remarks and no usage links
- suite roots missing project labels or project switch affordances
- project APIs without overview/tutorial attachments

## Practical next steps

1. Ship inferred usage everywhere in docs-template output and JSON. (done in this branch)
2. Ship curated related-content manifests so guides/snippets can target types and members. (done in this branch)
3. Add quality gates around relevance, not just presence. (partially done in this branch)
   - quick-start types can now be gated for curated related content
   - unresolved related-content targets already surface warnings
4. Extend those quality gates beyond quick-start types:
   - configuration/builder-heavy APIs without walkthroughs
   - suite-level portal guidance for important cross-project entry points
5. Ship an API suite contract for multi-project websites. (done in this branch)
   - docs-template HTML now renders project/module switchers
   - per-type and index JSON now emit `suite`
   - batch `apidocs` and `project-apidocs` can infer/populate suite entries automatically
   - `project-apidocs` now emits `api-suite.json`
6. Add suite-wide search/xref/coverage defaults so the suite model becomes a true portal, not just coordinated per-project pages. (done in this branch for `project-apidocs`)
   - `project-apidocs` now emits merged suite search/xref/coverage artifacts
   - suite manifests can point at those aggregate artifacts
   - docs-template pages can now use `suiteSearchUrl` to render built-in suite search
7. Add suite-level relevance gates and project-specific relevance hints in `project-apidocs`. (done in this branch)
   - project catalog entries can now declare `apiDocs.quickStartTypes` and `apiDocs.relatedContentManifest(s)`
   - `project-apidocs` can now evaluate merged `api-suite-coverage.json` through `suiteCoverage` + `suiteFailOnCoverage`
8. Add a standalone suite landing/search route in the site/runtime itself. (done in this branch)
   - `project-apidocs` now generates `api-suite/index.html` and `api-suite/index.json`
   - the suite portal reuses the built-in suite cards/search UI, supports project/module filtering, and can become the default suite home route
   - the suite portal also reads `api-suite-coverage.json` to surface summary guidance signals instead of forcing consumers to inspect raw JSON
   - `project-apidocs` now also emits `api-suite-related-content.json`, and the suite portal renders that as a built-in `Guides & Samples` section for cross-project onboarding/walkthrough discovery
9. Add first-class suite narrative guidance, not just suite search/assets. (done in this branch)
   - `project-apidocs` can now normalize `suiteNarrativeManifest` / `suiteNarrativeManifests` into `api-suite-narrative.json`
   - the suite portal renders that artifact as a built-in `Start Here` section with ordered workflow/onboarding links, optional audience/time metadata, and optional project targeting
10. Add suite narrative quality gates so important ecosystems must explain themselves. (done in this branch)
   - `project-apidocs` now supports `suiteNarrative` + `suiteFailOnNarrative`
   - thresholds can require section/item counts, summary presence, and coverage of suite entries inside the onboarding flow
11. Add starter guidance so new sites do not split APIs in ad-hoc ways when one suite would be clearer.
