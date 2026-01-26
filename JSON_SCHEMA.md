# PowerForge CLI JSON Schema

PowerForge CLI commands accept JSON configuration files (`--config <file.json>`). This repository ships a **baseline JSON Schema** set intended for editor validation (VSCode) and for building future tooling (e.g., a VSCode extension).

## Input config schemas (v1)

- Build: `Schemas/powerforge.buildspec.schema.json` (maps to `PowerForge.ModuleBuildSpec`)
- Install: `Schemas/powerforge.installspec.schema.json` (maps to `PowerForge.ModuleInstallSpec`)
- Test: `Schemas/powerforge.testsuitespec.schema.json` (maps to `PowerForge.ModuleTestSuiteSpec`)
- DotNet publish: `Schemas/powerforge.dotnetpublish.schema.json` (maps to `PowerForge.DotNetPublishSpec`)
- Pipeline / Plan: `Schemas/powerforge.pipelinespec.schema.json` (maps to `PowerForge.ModulePipelineSpec`)
  - Segments: `Schemas/powerforge.segments.schema.json` (maps to `PowerForge.IConfigurationSegment` + concrete segment types)
- Web site: `Schemas/powerforge.web.sitespec.schema.json` (maps to `PowerForge.Web.SiteSpec`, includes `Collections[].Include`/`Exclude`)
- Web project: `Schemas/powerforge.web.projectspec.schema.json` (maps to `PowerForge.Web.ProjectSpec`, includes `Content.Include`/`Content.Exclude`)
- Web front matter: `Schemas/powerforge.web.frontmatter.schema.json`
- Web theme: `Schemas/powerforge.web.themespec.schema.json` (maps to `theme.json`)
- Web pipeline: `Schemas/powerforge.web.pipelinespec.schema.json` (maps to `powerforge-web pipeline` JSON)
- Web publish: `Schemas/powerforge.web.publishspec.schema.json` (maps to `powerforge-web publish` JSON)
- Shared enums: `Schemas/powerforge.common.schema.json`

## Using with VSCode

Add a `$schema` property to your JSON file (PowerForge ignores unknown properties during deserialization):

```json
{
  "$schema": "./Schemas/powerforge.pipelinespec.schema.json",
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

## Web site highlights

`powerforge.web.sitespec.schema.json` supports a site-level `AssetRegistry` to centralize CSS/JS/performance policy:

```json
{
  "$schema": "./Schemas/powerforge.web.sitespec.schema.json",
  "SchemaVersion": 1,
  "Name": "ExampleSite",
  "BaseUrl": "https://example.com",
  "AssetRegistry": {
    "Bundles": [
      { "Name": "global", "Css": ["/themes/base/assets/site.css"], "Js": ["/themes/base/assets/site.js"] },
      { "Name": "docs", "Css": ["/themes/base/assets/docs.css"] }
    ],
    "RouteBundles": [
      { "Match": "/**", "Bundles": ["global"] },
      { "Match": "/docs/**", "Bundles": ["docs"] }
    ],
    "Preloads": [
      { "Href": "/themes/base/assets/site.css", "As": "style" }
    ],
    "CriticalCss": [
      { "Name": "base", "Path": "themes/base/critical.css" }
    ]
  }
}
```

Notes:
- `AssetRegistry` in `site.json` overrides theme asset choices.
- Route matching uses glob-style patterns (e.g., `/docs/**`).

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
