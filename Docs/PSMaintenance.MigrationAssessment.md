# PSMaintenance Migration Assessment

Last updated: 2026-05-20

## Recommendation

Archive/deprecate `PSMaintenance` as a standalone module, but do not copy it into `PSPublishModule` wholesale.

The useful direction is to absorb the durable engine pieces into `PowerForge` / `PowerForge.PowerShell`, then expose a small PSPublishModule command surface as thin wrappers. This fits the current PowerForge architecture better than keeping a separate maintenance module: PowerForge already owns module build metadata, delivery metadata, generated documentation, repository registration/publishing, private module install/update, and validation. PSMaintenance is mostly a runtime documentation consumption layer over those same module/package contracts.

The main caution: PSMaintenance has a large HtmlForgeX/OfficeIMO.Markdown renderer. That should not become a core PowerForge dependency. Rendering should sit behind an adapter so the engine can plan, fetch, normalize, and install documentation without requiring the HTML stack.

This migration has started on branch `codex/psmaintenance-ingestion-assessment`:

- reusable module-documentation discovery/planning/repository/install helpers moved under `PowerForge.PowerShell\ModuleDocumentation`
- rich console/HTML rendering helpers moved under `PSPublishModule\Services\ModuleDocumentation`
- PSPublishModule now exposes the PSMaintenance cmdlets and compatibility aliases
- focused planner/link-normalizer/renderer tests were ported into `PowerForge.Tests`
- the large migrated planner/exporter files were split into partial files instead of left as single copied blobs
- PSPublishModule import now registers a CoreCLR dependency resolver so the migrated HtmlForgeX/OfficeIMO.Markdown/Spectre dependency surface resolves from the module folder on PowerShell 7+

## What PSMaintenance Has

Local repo inspected: `C:\Support\GitHub\PSMaintanance` (`master`, with local edits in `Module/PSMaintenance.psd1` and `Module/PSMaintenance.psm1`).

Exported cmdlets:

- `Get-ModuleDocumentation`
- `Show-ModuleDocumentation`
- `Install-ModuleDocumentation`
- `Install-ModuleScript`
- `Set-ModuleDocumentation`

Aliases:

- `Show-Documentation`
- `Install-Documentation`
- `Install-ModuleScripts`
- `Install-Scripts`
- `Set-Documentation`

Core behaviors:

- Resolve installed modules by name/version or module object.
- Read `PrivateData.PSData.Delivery` and `PrivateData.PSData.Repository` metadata.
- Find local docs from module root and `Internals`.
- Fetch remote docs from GitHub/Azure DevOps using `ProjectUri`, repository branch, repository paths, and tokens.
- Normalize relative repo links/assets to raw/blob URLs.
- Render local/remote docs, command help, dependency information, commands, and releases to interactive HTML.
- Copy bundled module docs to a target folder with layout/conflict policies.
- Copy scripts from `Internals\Scripts` to a target folder with include/exclude/conflict/unblock behavior.
- Store GitHub/Azure DevOps tokens under a user profile token store.

Notable implementation pressure:

- `HtmlExporter.cs` is about 95 KB and combines rendering, tab selection, release cards, dependency/command display, and markdown handling.
- `DocumentationPlanner.cs` is about 57 KB and contains several distinct concerns: local discovery, remote fetching, release parsing, doc classification, link normalization, and selection policy.
- PSMaintenance targets `net8.0;net472`; PSPublishModule/PowerForge currently target `net472;net8.0;net10.0`.
- PSMaintenance depends on `HtmlForgeX`, `HtmlForgeX.Markdown`, `OfficeIMO.Markdown`, `Spectre.Console`, and `Spectre.Console.Json`.

## What PowerForge Already Has

PowerForge/PSPublishModule already covers several adjacent capabilities:

- Build-time documentation generation via `PowerForge.PowerShell\Services\DocumentationEngine.cs`.
- XML-doc/comment-help enrichment and markdown/external-help writers under `PowerForge\Services\Documentation\`.
- `New-ConfigurationDocumentation` and `New-ConfigurationDelivery` wrappers.
- Delivery metadata for `InternalsPath`, root README/CHANGELOG/LICENSE bundling, intro/upgrade text/files, repository paths/branch, documentation order, generated install/update commands, preserve/overwrite paths.
- Generated delivery install/update commands via `PowerForge\Services\DeliveryCommandGenerator.cs`.
- Module manifest/metadata readers and delivery metadata mutation helpers.
- Private gallery/repository registration and install/update flows for Azure Artifacts/PS repositories.
- GitHub publishing, housekeeping, release assets, and HTTP helpers, but not yet a general GitHub/Azure DevOps content-fetch abstraction.

This means the migration should reuse PowerForge's existing delivery/config/documentation contracts instead of recreating PSMaintenance-specific schema.

## Fit Analysis

| PSMaintenance behavior | PowerForge overlap | Migration target |
| --- | --- | --- |
| Installed module resolution by name/version/object | Existing module locator scripts and module metadata readers | `PowerForge.PowerShell` resolver service |
| Delivery metadata consumption | Existing `DeliveryOptionsConfiguration`, `New-ConfigurationDelivery`, manifest mutation | Reuse existing delivery config model, add read-side adapter only if needed |
| Local README/CHANGELOG/LICENSE/Internals docs discovery | Build pipeline already bundles these, but no runtime doc viewer planner | New core documentation inventory/planner service |
| Remote GitHub/Azure DevOps doc fetch | Publishing/release code exists, content browsing does not | New repository content client abstraction in `PowerForge` |
| Link normalization for remote docs | Not currently general-purpose | Move neutral normalizer to `PowerForge` |
| Changelog/release parsing | GitHub release publishing exists, but not doc-facing changelog projection | Move release/changelog parser to `PowerForge` with tests |
| HTML interactive viewer | No equivalent in PowerForge core | Renderer adapter, not core; preferably PSPublishModule-hosted or optional PowerForge.PowerShell service |
| Console markdown viewer | Partial Spectre usage exists in PSPublishModule | Thin wrapper or later phase, after planner is stable |
| Copy documentation to folder | Delivery command generator overlaps but is generated module-local | Core install planner/copy service, PSPublishModule wrapper |
| Copy `Internals\Scripts` | Delivery install already copies Internals broadly, but script-only extraction is distinct | Small core planner/copy service, PSPublishModule wrapper |
| Token store | No general doc token store | New explicit credential provider/token-store abstraction, avoid silent global coupling |

## Proposed Architecture

Keep the boundaries:

- `PowerForge`
  - Plain documentation inventory models.
  - Local document discovery.
  - Repository URL parsing and content normalization.
  - Changelog/release projection.
  - Documentation/script install planning and filesystem copy rules.
  - Renderer-neutral output models.

- `PowerForge.PowerShell`
  - Installed module resolution using `Get-Module` / `Test-ModuleManifest`.
  - `Get-Help` extraction and command/dependency enrichment.
  - PowerShell-hosted repository/token providers where PowerShell profile/user semantics matter.
  - Any behavior requiring `PSObject`, `PSModuleInfo`, `ScriptBlock`, or runspaces.

- `PSPublishModule`
  - Cmdlet parameter binding.
  - `ShouldProcess`, `WhatIf`, `Verbose`, `Open`, `ListOnly`, output shaping.
  - Compatibility aliases.
  - Thin calls into PowerForge/PowerForge.PowerShell services.

Do not put the HtmlForgeX renderer directly in `PowerForge`. Either:

1. put an HTML renderer adapter in `PSPublishModule` because only the command surface needs it, or
2. create an optional renderer-oriented assembly later if multiple hosts need the same HTML output.

The first option is the current implementation and avoids expanding core dependencies.

Dependency isolation should be handled at two separate layers:

- Keep `NETAssemblyLoadContext` / `UseAssemblyLoadContext` for modules produced by PowerForge. That feature generates a module-scoped loader for downstream binary modules and should not be treated as PSPublishModule self-isolation.
- Keep PSPublishModule's own import resolver active. On .NET Framework this remains the `AssemblyResolve` fallback; on PowerShell 7+ PSPublishModule now registers an `AssemblyLoadContext.Default.Resolving` hook backed by `AssemblyDependencyResolver` so its copied dependencies can be found beside `PSPublishModule.dll`.

This CoreCLR resolver is useful and should ship with the migration, but it is not full side-by-side isolation if another module has already loaded a conflicting assembly identity into the default load context. True PSPublishModule self-isolation would require a bootstrapper module that loads `PSPublishModule.dll` into a dedicated `AssemblyLoadContext` before cmdlet types are registered.

## Migration Slices

### Slice 1: Inventory and contracts

- Status: started.
- Added this migration tracking document.
- Ported existing PSMaintenance planner/result models first to preserve behavior before introducing renamed public contracts.
- Added tests that describe PSMaintenance-compatible selection behavior.

### Slice 2: Neutral planners

- Status: partially complete.
- `DocumentationFinder`, `DocumentationPlanner`, `RepositoryContentNormalizer`, changelog/release parsing, `DocumentationInstaller`, and GitHub/Azure DevOps repository clients have moved under `PowerForge.PowerShell\ModuleDocumentation`.
- Follow-up: rename the compatibility-shaped PSMaintenance models to cleaner PowerForge contracts and split the repository client interface into a more generic public-facing abstraction.

### Slice 3: PowerShell-hosted module/doc enrichment

- Add `PowerShellModuleDocumentationResolver` in `PowerForge.PowerShell`.
- Use existing module manifest readers where possible.
- Keep `Get-Help` parsing/extraction next to `DocumentationEngine`, because it is PowerShell-runtime behavior.
- Decide whether the old `GetHelpParser` still has unique viewer value after comparing it with the current `DocumentationExtractionPayload`/markdown help writer.

### Slice 4: Thin PSPublishModule wrappers

- Status: complete for the compatibility surface.
- Added wrappers for:
  - `Get-ModuleDocumentation`
  - `Install-ModuleDocumentation`
  - `Install-ModuleScript`
  - `Show-ModuleDocumentation`
  - `Set-ModuleDocumentation`
- Preserved aliases initially for migration friendliness.
- `Show-ModuleDocumentation` depends on PSPublishModule renderer services while planner/runtime helpers live in PowerForge.PowerShell.

### Slice 5: Renderer migration

- Status: started.
- Split the current HTML renderer into partial files:
  - main tab/page composition
  - command/help rendering
  - release/dependency rendering
  - markdown/console rendering helpers
- Keep HtmlForgeX/OfficeIMO.Markdown references out of core.
- Added HTML behavior tests for alternate fences, release cards, version-aware release ordering, issue links, and format/type text rendering.

### Slice 6: Deprecation/archive

- Release PSPublishModule with compatibility aliases and a migration note.
- Update PSMaintenance README to point to PSPublishModule and explain command replacements.
- Ship a final PSMaintenance release that warns on import or on command invocation, with a clear migration date.
- Archive the GitHub repository after users have had at least one normal release cycle with compatibility available in PSPublishModule.

## What Not To Duplicate

- Do not duplicate `New-ConfigurationDelivery` concepts. Read the same `PrivateData.PSData.Delivery` metadata PowerForge already writes.
- Do not create a parallel repository registration/private module install flow. PSPublishModule already has `Register-ModuleRepository`, `Connect-ModuleRepository`, `Install-PrivateModule`, `Update-PrivateModule`, and related services.
- Do not copy the existing 95 KB `HtmlExporter` as-is. Split it first, or it will become the next large file that blocks maintenance.
- Do not put renderer package dependencies into `PowerForge` core.
- Do not preserve PSMaintenance token storage as an implicit singleton without an abstraction. The new service should make token source precedence explicit: parameter, environment, stored profile token.

## Open Questions

- Should `Show-ModuleDocumentation` be considered a PSPublishModule operator convenience only, or should `PowerForge.Cli` eventually get a non-PowerShell equivalent?
- Should remote doc fetching support only GitHub/Azure DevOps initially, or should the content client contract also account for local Git remotes and generic raw URLs?
- Should `Install-ModuleScript` stay as a generic script-extraction command, or should it become a narrower delivery command that only uses `Delivery.InternalsPath` and documented script paths?
- Should `Set-ModuleDocumentation` be renamed long-term to something credential-specific, with old aliases left as compatibility shims?

## Bottom Line

The standalone module no longer earns its separate existence. Its strongest parts are useful, but they are PowerForge module-delivery/documentation features now.

The right migration is staged extraction, not a repo transplant: core planner/model logic into `PowerForge`, PowerShell runtime glue into `PowerForge.PowerShell`, and PSPublishModule cmdlets as thin wrappers. Keep the renderer optional and adapter-based, then archive PSMaintenance once PSPublishModule has shipped compatibility aliases and migration docs.
