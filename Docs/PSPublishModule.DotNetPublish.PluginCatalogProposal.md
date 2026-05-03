# PowerForge Plugin Catalog Proposal

This proposal defines the boundary for moving plugin-oriented build mechanics into PowerForge without dragging product-specific policy into the engine.

## Goal

Move reusable mechanics into PowerForge:

- plugin catalog loading
- plugin group selection
- folder export / publish output shaping
- NuGet packing for plugin projects
- optional plugin manifest writing
- symbol stripping and common output policies

Keep product-specific policy in each repo:

- which projects are "public" vs "private"
- manifest branding and schema names
- README text, launcher text, environment-variable naming
- assumptions about sibling/private repos

## Current State

PowerForge already owns a meaningful part of bundle mechanics:

- bundle composition
- archive rules
- delete patterns
- bundle metadata emission
- bundle scripts
- installer-from-bundle flow

That means a product-specific portable bundle finalizer should not be copied into PowerForge as-is. Most of its remaining value is product-specific content and plugin export invocation, not missing generic bundle primitives.

The biggest duplication that still looks engine-worthy is the plugin catalog/export lane:

- the product repo keeps hardcoded project catalogs in separate scripts
- folder export and NuGet pack flows each rediscover the same groups
- private plugin handling currently knows about external private-repo roots

## Proposed Contract

Introduce a reusable PowerForge plugin catalog model with three layers:

1. Catalog

- A config-owned list of plugin projects
- Each entry has an `Id`, `ProjectPath`, optional `AssemblyName`, optional `PackageId`, optional `Manifest`
- Each entry can belong to one or more named groups like `public`, `private`, `all`, `desktop`, `ops`

2. Build lanes

- `ExportFolders`
- `PackNuGet`

Each lane should accept:

- selected groups
- configuration
- preferred framework
- output root
- include symbols
- extra MSBuild properties

3. Optional manifest writer

- Generic manifest writing should be opt-in
- PowerForge should write only a schema-neutral payload unless the repo config explicitly requests a format
- If a repo needs a branded manifest like `ix-plugin.json`, that should come from config, not hardcoded engine behavior

## Example Shape

This is a proposed config shape, not an implemented schema yet:

```json
{
  "Plugins": {
    "Catalog": [
      {
        "Id": "filesystem",
        "ProjectPath": "src/Tools/FileSystem/FileSystem.csproj",
        "Groups": ["public"],
        "Manifest": {
          "Format": "generic",
          "EntryTypeStrategy": "IToolPack"
        }
      },
      {
        "Id": "private-tool",
        "ProjectPath": "src/Tools/PrivateTool/PrivateTool.csproj",
        "Groups": ["private"],
        "MsBuildProperties": {
          "PrivateToolRoot": "{externalRoots.privateTool}"
        }
      }
    ]
  }
}
```

## What Belongs In PowerForge

- Catalog parsing and group filtering
- Project framework selection rules
- Common `dotnet publish` / `dotnet pack` execution
- Output directory lifecycle
- Optional symbol stripping
- Optional schema-neutral manifest writing
- Explicit external-root/property injection from config

## What Must Stay Repo-Specific

- product project membership in `public` / `private`
- `ix-plugin.json` naming unless generalized as a configurable format
- launcher scripts named `run-chat.ps1`
- README text that references a product UI or product name
- product-specific environment variable names
- repo-specific sibling dependency meaning such as a private tool root

## Bundle Guidance

Do not create a second generic bundle-finalizer system unless a real gap appears.

PowerForge bundle post-process already supports:

- archiving subdirectories
- deleting files by pattern
- writing metadata
- running scripts

That is already a reusable contract. Repos should prefer those features before adding more engine surface.

The remaining repo-side bundle logic should stay outside PowerForge unless two or more repos need the same behavior with the same contract.

## Product Migration Path

1. Add a PowerForge plugin catalog contract and execution service.
2. Replace `Export-PluginFolders.ps1` with a thin wrapper over that contract.
3. Replace `Publish-Plugins.ps1` with the same catalog plus `PackNuGet`.
4. Keep product-specific bundle finalizers only for product content generation until another repo proves the same launcher/readme contract is reusable.
5. Revisit launcher/readme generation only after at least one second consumer exists.

## Decision Rule

Before moving anything from a repo into PowerForge, ask:

1. Is this build machinery rather than product policy?
2. Would at least two repos likely want the same contract?
3. Can the behavior be expressed as config instead of hardcoded product names?
4. Would another repo recognize the option names as generic?

If any answer is "no", keep it in the repo for now.
