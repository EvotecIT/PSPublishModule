# PowerForge CLI JSON Schema

PowerForge CLI commands accept JSON configuration files (`--config <file.json>`). This repository ships a **baseline JSON Schema** set intended for editor validation (VSCode) and for building future tooling (e.g., a VSCode extension).

## Input config schemas (v1)

- Build: `schemas/powerforge.buildspec.schema.json` (maps to `PowerForge.ModuleBuildSpec`)
- Install: `schemas/powerforge.installspec.schema.json` (maps to `PowerForge.ModuleInstallSpec`)
- Test: `schemas/powerforge.testsuitespec.schema.json` (maps to `PowerForge.ModuleTestSuiteSpec`)
- Pipeline / Plan: `schemas/powerforge.pipelinespec.schema.json` (maps to `PowerForge.ModulePipelineSpec`)
  - Segments: `schemas/powerforge.segments.schema.json` (maps to `PowerForge.IConfigurationSegment` + concrete segment types)
- Shared enums: `schemas/powerforge.common.schema.json`

## Using with VSCode

Add a `$schema` property to your JSON file (PowerForge ignores unknown properties during deserialization):

```json
{
  "$schema": "./schemas/powerforge.pipelinespec.schema.json",
  "SchemaVersion": 1,
  "Build": {
    "Name": "MyModule",
    "SourcePath": ".",
    "Version": "1.0.0",
    "Frameworks": ["net472", "net8.0", "net10.0"]
  },
  "Install": {
    "Enabled": true,
    "Strategy": "AutoRevision",
    "KeepVersions": 3
  },
  "Segments": [
    { "Type": "Manifest", "Configuration": { "Author": "Me" } },
    { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "Artefacts" } }
  ]
}
```

## Segment type discriminators

Segments use a required `Type` discriminator (case-insensitive). Supported `Type` values include:

- Fixed: `Manifest`, `Build`, `BuildLibraries`, `Information`, `Options`, `Formatting`, `Documentation`, `BuildDocumentation`, `ImportModules`, `ModuleSkip`, `Command`, `PlaceHolder`, `PlaceHolderOption`, `Compatibility`, `FileConsistency`, `TestsAfterMerge`
- Publish: `GalleryNuget`, `GitHubNuget`
- Artefacts: `Unpacked`, `Packed`, `Script`, `ScriptPacked`
- Module dependencies: `RequiredModule`, `ExternalModule`, `ApprovedModule`

## Notes

- Property names are treated case-insensitively by PowerForge (`System.Text.Json` options); the schemas use canonical PascalCase names.
- The CLI config parser allows JSON comments and trailing commas; many JSON Schema validators do not.
- `Version` supports the special value `"auto"` for build/pipeline/install configs (reads `ModuleVersion` from `<Name>.psd1`).

## CLI JSON output

When you run with `--output json`, stdout contains a single JSON object with a stable envelope:

- `schemaVersion` (int)
- `command` (string)
- `success` (bool)
- `exitCode` (int)
- `error` (string, when `success=false`)
- plus command-specific payload (`spec`, `result`, `logs`, etc.)
