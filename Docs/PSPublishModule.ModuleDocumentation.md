# Module Documentation Workflow

This document defines the recommended documentation workflow for PowerForge/PSPublishModule module pipelines.

For DotNet publish usage and command conventions, see:
- `Docs/PSPublishModule.DotNetPublish.Quickstart.md`

## Generated vs Source Content

- `Module/Docs` is generated output.
- `Module/en-US/<ModuleName>-help.xml` is generated external help output.
- Treat both as build artifacts that can be overwritten by `Invoke-ModuleBuild` / `Build-Module.ps1`.

In this repo, module build uses:

```powershell
New-ConfigurationDocumentation -Enable -PathReadme 'Docs\Readme.md' -Path 'Docs' -AboutTopicsSourcePath 'Help\About'
```

Because documentation generation is enabled, PowerForge cleans stale generated docs and syncs new output back to `Module/Docs`. Manual edits in `Module/Docs` are not durable.

## Authoring Sources

- Cmdlet help pages come from PowerShell help metadata plus XML docs comments on cmdlets.
- About topics come from `about_*.help.txt` / `about_*.txt` / `about_*.md` / `about_*.markdown` source files.

For binary modules, the compiler-generated XML docs file is the canonical authored source for command help:

- keep `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in the `.csproj`
- remove `MatejKafka.XmlDoc2CmdletDoc` package/targets if you are migrating from that workflow
- build the project so `<ModuleName>.xml` is emitted beside `<ModuleName>.dll`
- let PowerForge read that XML and generate:
  - `Docs\*.md`
  - `Docs\Readme.md`
  - `Docs\About\*.md`
  - `<culture>\<ModuleName>-help.xml`

Useful XML authoring shapes that PowerForge now preserves for binary modules:

- cmdlet `<summary>` for synopsis
- cmdlet top-level `<para>` blocks or `<remarks>` for descriptions
- parameter/property `<summary>` for parameter descriptions
- `<list type="alertSet">` for notes
- `<example>` with `<summary>`, `<prefix>`, `<code>`, and `<para>`
- `<seealso>` links
- XML docs on CLR input/output types for type descriptions

This keeps the authored XML style familiar for teams coming from `XmlDoc2CmdletDoc`, while the generated outputs stay PowerShell-oriented and valid for `Get-Help`.

## Packaging And Installed Documentation Concepts

PowerForge keeps four documentation layers separate:

- Source docs are the authored files and XML comments that maintainers edit: cmdlet XML comments, `Help/About/about_*` source files, and durable `Docs/*.md` guidance.
- Generated docs are build artifacts created from source docs, including command markdown and external help XML.
- Packaged docs are the documentation files copied into the built module package.
- Installed docs are the operator-selected copy created by `Install-ModuleDocumentation`.

`Install-ModuleDocumentation` does not edit the source documentation. It copies the packaged documentation payload from an installed module to a target folder chosen by the operator.

The copied payload is:

- the delivery `InternalsPath` from the module manifest when configured
- otherwise the module's `Internals` folder when it exists
- root `README*` and `CHANGELOG*` files
- root `LICENSE*` normalized to `license.txt`
- optional delivery intro text from `IntroText` or `IntroFile`, unless `-NoIntro` is used

Delivery metadata is configured during packaging with `New-ConfigurationDelivery`. Typical fields used by documentation installers and viewers include `InternalsPath`, `DocumentationOrder`, `IntroText`, and `IntroFile`.

## Install-ModuleDocumentation Update Rules

The default install shape is conservative:

```powershell
Install-ModuleDocumentation -Name MyModule -Path C:\Docs
```

By default this uses:

- `-Layout ModuleAndVersion`
- `-OnExists Merge`

This means each module version lands in its own folder such as `C:\Docs\MyModule\1.2.3`, and re-running the command adds missing files without replacing existing files.

`-Layout` controls the destination path:

| Layout | Destination | Best fit |
| --- | --- | --- |
| `Direct` | `Path` | Disposable or caller-managed folders. |
| `Module` | `Path\ModuleName` | A single current documentation folder per module. |
| `ModuleAndVersion` | `Path\ModuleName\Version` | Side-by-side documentation for installed module versions. |

`-OnExists` controls what happens when the destination already exists:

| OnExists | Behavior |
| --- | --- |
| `Merge` | Add missing files and folders. Existing files are preserved unless `-Force` is used. Local files that are not part of the package remain in place. |
| `Overwrite` | Delete the destination folder first, then copy a fresh documentation set. Use `-Force` when read-only files may need their attributes cleared before delete. |
| `Skip` | Leave the destination unchanged and return the resolved destination path. |
| `Stop` | Throw when the destination already exists. |

`-Force` does not change the selected layout. With `-OnExists Merge`, it allows colliding files from the package to replace existing destination files while still leaving unrelated local files in place. With `-OnExists Overwrite`, it helps clear read-only attributes before the destination folder is deleted.

For validation, PowerForge can now enforce the old package's most-used missing-doc checks through `New-ConfigurationValidation`:

- `-MinSynopsisPercent 100` for missing cmdlet synopsis
- `-MinParameterDescriptionPercent 100` for missing parameter descriptions
- `-MinTypeDescriptionPercent 100` for missing input/output type descriptions

If a repo previously used `-ignoreOptional`, the closest native PowerForge profile is:

- keep `-MinSynopsisPercent 100`
- keep `-MinTypeDescriptionPercent 100`
- set `-MinParameterDescriptionPercent 0`
- optionally set `-MinDescriptionPercent 0` and `-MinExamplesPerCommand 0` if the repo wants `XmlDoc2CmdletDoc`-style validation rather than stricter markdown/help quality gates

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

Create a markdown about source template:

```powershell
New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About' -Format Markdown
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
2. For binary modules, ensure the project emits the compiler XML docs file and let PowerForge generate PowerShell help from it instead of using a separate XML-help package.
3. Add about-topic source files (prefer `Help/About`): `about_*.help.txt`, `about_*.txt`, `about_*.md`, or `about_*.markdown`.
4. Configure `New-ConfigurationDocumentation` with `-Enable`, `-Path`, `-PathReadme`, and optional `-AboutTopicsSourcePath`.
5. Run `Invoke-ModuleBuild` in normal mode to regenerate docs.
6. Review generated `Docs` + external help XML and commit intentional updates.
