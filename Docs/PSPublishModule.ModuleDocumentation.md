# Module Documentation Workflow

This document defines the recommended documentation workflow for PowerForge/PSPublishModule module pipelines.

## Generated vs Source Content

- `Module/Docs` is generated output.
- `Module/en-US/<ModuleName>-help.xml` is generated external help output.
- Treat both as build artifacts that can be overwritten by `Invoke-ModuleBuild` / `Build-Module.ps1`.

In this repo, module build uses:

```powershell
New-ConfigurationDocumentation -Enable -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs' -AboutTopicsSourcePath 'Help\About'
```

Because `-StartClean` and `-UpdateWhenNew` are enabled, manual edits in `Module/Docs` are not durable.

## Authoring Sources

- Cmdlet help pages come from PowerShell help metadata plus XML docs comments on cmdlets.
- About topics come from `about_*.help.txt` / `about_*.txt` / `about_*.md` / `about_*.markdown` source files.

Recommended source layout in module repos:

- `Help/About/about_<Topic>.help.txt`
- New scaffolds created via `Build-Module -ModuleName ...` now seed:
  - `Help/About/about_<ModuleName>_Overview.help.txt`

Use build configuration to include extra about-topic roots:

```powershell
New-ConfigurationDocumentation `
  -Enable `
  -Path 'Docs' `
  -PathReadme 'Docs\Readme.md' `
  -AboutTopicsSourcePath 'Help\About'
```

## About Topic Scaffolding

Use the helper cmdlet to create a canonical template:

```powershell
New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About'
```

This creates:

- `Help/About/about_Troubleshooting.help.txt`

You can overwrite an existing template with:

```powershell
New-ModuleAboutTopic -TopicName 'about_Troubleshooting' -OutputPath '.\Help\About' -Force
```

## Build Outputs

During docs generation:

- command markdown is generated under `Docs\*.md`
- about topic markdown is generated under `Docs\About\*.md`
- about index is generated under `Docs\About\README.md`
- module docs readme is generated at `Docs\Readme.md`
- external help is generated at `<culture>\<ModuleName>-help.xml` (default culture: `en-US`)

## Process For Other Modules

1. Add XML docs/comments for cmdlets in the module source.
2. Add `about_*.help.txt` source files (prefer `Help/About`).
3. Configure `New-ConfigurationDocumentation` with `-Enable`, `-Path`, `-PathReadme`, and optional `-AboutTopicsSourcePath`.
4. Run `Invoke-ModuleBuild` in normal mode to regenerate docs.
5. Review generated `Docs` + external help XML and commit intentional updates.
