# PowerForge.Web Private Gallery Engine Plan

Last updated: 2026-05-23

This document defines the engine-side plan for building a reusable private
PowerShell Gallery experience on top of PowerForge.Web. The goal is not a
one-off feed viewer. The goal is a static, searchable internal gallery site
where maintainers publish modules to a private feed and PowerForge turns that
feed plus packaged module documentation into a browsable delivery surface.

## Goal

Private gallery sites should answer the same questions users expect from a
PowerShell gallery:

- what modules are available,
- which versions are available,
- how to register the repository,
- how to install and update modules,
- what commands, examples, and docs ship with each module,
- which dependencies and compatibility constraints apply,
- which versions are stable, prerelease, deprecated, or hidden,
- and what package statistics are available from the backing feed.

Maintainers should keep the normal publishing workflow. The website should be
generated from the published packages and their bundled metadata.

## Architecture

Keep the feature split by responsibility.

### PowerForge

Core gallery indexing belongs in `PowerForge` because it is host-neutral and
useful outside the website CLI.

Proposed folders:

- `PowerForge/Models/PrivateGallery/`
- `PowerForge/Services/PrivateGallery/`

Core models:

- `PrivateGalleryFeed`
- `PrivateGalleryPackage`
- `PrivateGalleryPackageVersion`
- `PrivateGalleryModuleMetadata`
- `PrivateGalleryCommandMetadata`
- `PrivateGalleryDocumentAsset`
- `PrivateGalleryPackageMetrics`
- `PrivateGalleryIndexResult`

Core services:

- `AzureArtifactsFeedClient`
- `NuGetV3FeedClient`
- `PrivateGalleryIndexer`
- `NuGetPackageDownloader`
- `SafePackageExtractor`
- `PowerShellModulePackageInspector`
- `PrivateGalleryMetricsCollector`

This layer owns:

- Azure Artifacts feed/package/version inventory,
- NuGet V3 package download metadata where useful,
- safe `.nupkg` extraction,
- `.psd1` parsing,
- external help XML parsing,
- README/changelog/license/docs/examples discovery,
- exported command metadata,
- dependency metadata,
- normalized gallery JSON models.

This layer must not render website pages and must not import or execute uploaded
modules.

### PowerForge.Web

Website shaping belongs in `PowerForge.Web`.

Proposed files:

- `PowerForge.Web/Models/WebPrivateGallery.cs`
- `PowerForge.Web/Services/WebPrivateGalleryGenerator.cs`
- `PowerForge.Web/Services/WebPrivateGalleryPageGenerator.cs`
- `PowerForge.Web/Services/WebPrivateGallerySearchBuilder.cs`

This layer owns:

- mapping normalized gallery data to website data files,
- generating gallery content pages,
- generating search records and facets,
- connecting module packages to existing PowerShell API docs output,
- producing theme-friendly page models.

Expected outputs:

- `data/private-gallery/feed.json`
- `data/private-gallery/modules/<module>.json`
- `data/private-gallery/modules/<module>/<version>.json`
- `data/private-gallery/search.json`
- optional merge into `data/projects/catalog.json` using the standard
  `project-catalog` contract
- `content/modules/<module>/index.md`
- `content/modules/<module>/versions/<version>.md`
- optional command pages under `content/modules/<module>/commands/`

Important boundary: the private gallery index is a provider/adapter layer, not
the long-term page model. Azure Artifacts contributes package, version, metric,
documentation, command, dependency, and install facts. Reusable PowerForge.Web
surfaces such as `project-catalog`, `project-docs-sync`, `project-apidocs`,
`release-hub`, downloads, search, and theme layouts should render those facts.
Private-gallery-only pages are acceptable as starter/demo output, but shared
portal sites should prefer mapping packages into the generic project/module
catalog.

### PowerForge.Web.Cli

The CLI should stay a thin adapter.

Proposed files:

- `PowerForge.Web.Cli/WebPipelineRunner.Tasks.PrivateGallery.cs`
- `PowerForge.Web.Cli/WebCliCommandHandlers.PrivateGalleryCommands.cs`

The CLI owns:

- pipeline JSON binding,
- environment token lookup,
- mode-aware warning policy,
- summary output,
- and calling `PowerForge.Web`.

It should not contain Azure Artifacts, package extraction, or docs parsing
business logic.

### PSPublishModule

PowerShell cmdlets are optional follow-up UX, not the first implementation
layer.

Potential later wrappers:

- `New-PrivateGalleryWebsite`
- `Update-PrivateGalleryIndex`
- `Test-PrivateGalleryFeed`

Those cmdlets should call the shared PowerForge/PowerForge.Web services rather
than carrying their own indexing logic.

### PowerForge.PowerShell

Avoid this layer for the first implementation. The gallery indexer should parse
files, not import modules.

Use `PowerForge.PowerShell` only if a later slice needs PowerShell-host
behavior that cannot remain host-neutral, such as reusing repository profile
bootstrap logic or PowerShell-specific credential-provider checks.

## Proposed Pipeline Task

Future task shape:

```json
{
  "task": "private-gallery-index",
  "provider": "azure-artifacts",
  "organization": "evotecpl",
  "project": "PowerShellGallery",
  "feed": "PowerShellGalleryFeed",
  "repositoryName": "EvotecPowerShellGallery",
  "includeAllVersions": true,
  "includePackageContent": true,
  "includeMetrics": true,
  "tokenEnv": "AZURE_DEVOPS_TOKEN",
  "out": "./data/private-gallery",
  "contentOut": "./content/modules"
}
```

The task should run before `build` so templates and search indexing can consume
generated gallery data.

## Source Data

### Azure Artifacts

Azure Artifacts is the source of truth for published package inventory:

- feed identity,
- package ids,
- package versions,
- latest/listed/deleted state,
- publish date,
- feed views,
- package/version REST URLs,
- optional package and version metrics.

Use paging and cache-friendly behavior. Respect `Retry-After` and
`X-RateLimit-*` response headers when present.

### NuGet Package Content

Each published module package is the source of truth for bundled module content:

- `.psd1`,
- root/module README files,
- `Docs/`,
- external help XML,
- examples,
- changelog or release notes,
- license,
- icon or image assets,
- command exports,
- required modules.

The indexer should support a metadata-only mode for fast builds and a content
inspection mode for full gallery generation.

### Existing PowerForge Data

Private gallery indexing should compose with existing engine capabilities:

- `package-hub` for local `.csproj`/`.psd1` metadata patterns,
- `apidocs` for PowerShell command help pages,
- `xref-merge` for linking docs to commands,
- search index generation for module/docs/command search,
- `compat-matrix` for dependency and compatibility display.

## Security Rules

Treat uploaded packages as untrusted archives.

- Do not import modules.
- Do not execute package scripts.
- Defend against zip-slip paths.
- Cap extracted file count and total extracted bytes.
- Ignore unsupported binary files except for known static assets.
- Parse manifests and help files as data.
- Keep warnings for malformed package metadata.
- Make destructive cleanup paths explicit and constrained to the temp workspace.

## Generated Data Contract

The first stable document should be page-agnostic JSON:

```json
{
  "schemaVersion": 1,
  "format": "powerforge.private-gallery",
  "generatedAtUtc": "2026-05-23T00:00:00Z",
  "provider": "azure-artifacts",
  "feed": {
    "organization": "evotecpl",
    "project": "PowerShellGallery",
    "name": "PowerShellGalleryFeed",
    "repositoryName": "EvotecPowerShellGallery"
  },
  "summary": {
    "packageCount": 0,
    "versionCount": 0,
    "commandCount": 0,
    "documentCount": 0
  },
  "packages": []
}
```

Per-package JSON can carry heavier details:

- normalized metadata,
- version table,
- command list,
- docs list,
- examples list,
- dependencies,
- install/update command templates,
- metrics,
- warnings.

## User-Facing Site Shape

The engine should generate enough data for a frontend to render:

- module listing,
- module detail,
- install/update setup commands,
- version history,
- command docs,
- examples,
- dependencies,
- download/statistics badges,
- freshness and quality signals.

The frontend may be rich, but the system of record is the generated data.

## Implementation Slices

### Slice 1: Inventory JSON

- Add core models and `AzureArtifactsFeedClient`.
- Add `private-gallery-index` pipeline task in metadata-only mode.
- Emit `feed.json`.
- Test paging, missing feed, auth failure, and empty feed behavior.

Status: implemented as the first engine slice. The task emits `feed.json` and
per-package/per-version JSON under `modules/`, plus `search.json`.

### Slice 2: Safe Package Inspection

- Download selected/latest package versions.
- Add safe extraction.
- Parse `.psd1`, README, docs, external help XML, and examples.
- Emit per-module/per-version JSON.

Status: partially implemented. The first slice downloads `.nupkg` files through
NuGet V3 and inspects archive entries in place without module import or script
execution. It emits discovered module metadata on package/version JSON records,
including bounded text content for README/docs/changelog/license assets so
package-bundled documentation can be rendered by the portal docs pipeline.

### Slice 3: Web Page Generation

- Generate module list/detail/version pages.
- Generate install/update command snippets from repository profile settings.
- Add search entries for modules, commands, docs, and examples.

Status: initial module and related-document page generation is implemented via
`portal-module-pages`, composing `private-gallery-index` feed data with
`portal-docs-index` documentation data. `portal-docs-index` also supports a
reusable `kind: "module"` source declaration that expands to package-bundled
docs plus related repository docs when GitHub or Azure DevOps repository details
are present. `portal-module-pages` supports separate `indexLayout`,
`moduleLayout`, and `documentLayout` values, and emits `meta.pageKind` plus
surface-specific counts so reusable gallery themes can render native catalog,
module detail, and module documentation views without route-specific template
checks.

### Slice 4: Metrics and Quality Signals

- Add optional package and version metrics.
- Add quality fields such as has README, has help, has examples, has license,
  command count, dependency count, and docs freshness.
- Add CI warning policy and baselines.

Status: package and version download metrics are implemented for Azure Artifacts
as optional data. Quality-signal rollups and CI gates remain future work.

### Slice 5: Starter/Theme Contract

- Add a gallery starter profile or gallery feature contract.
- Add required selectors/partials for module cards, install command blocks,
  version tables, and command/doc search results.

Status: partially implemented. Generated private-gallery pages now expose
surface-specific layout names and metadata, which gives a starter/theme a stable
contract for catalog/detail/document templates. A full bundled starter profile
and selector contract remain future work.

## Non-Goals

- No live website proxy to Azure DevOps.
- No runtime package installation from the website.
- No module import during indexing.
- No maintainer upload UI in the first engine slices.
- No replacement for Azure Artifacts as the package system of record.
