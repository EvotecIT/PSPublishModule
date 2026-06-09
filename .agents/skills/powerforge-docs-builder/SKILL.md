---
name: powerforge-docs-builder
description: Build and maintain PSPublishModule/PowerForge documentation pipelines, including generated cmdlet docs, external help XML, and about-topic source workflows.
---

# PowerForge Docs Builder

Use this skill when work is primarily documentation-pipeline related (not website theming).

## Golden Path (Do This In Order)

1. Confirm docs scope first.
   - Module command docs/help (`Module/Docs`, `Module/en-US/*-help.xml`) vs authored source docs (`Docs/*.md`, code XML comments, `Help/About/about_*`).
2. Validate pipeline configuration.
   - Check `New-ConfigurationDocumentation` in build settings (`Path`, `PathReadme`, `AboutTopicsSourcePath`, `StartClean`, `UpdateWhenNew`).
3. Edit source, not generated output.
   - Cmdlet docs: C# XML comments/examples.
   - About docs: `Help/About/about_*.help.txt|.txt|.md|.markdown`.
   - Narrative docs: `Docs/*.md`.
4. Regenerate docs through normal build path.
   - Prefer `.\Module\Build\Build-Module.ps1`.
5. Validate generated outputs.
   - `Module/Docs` pages present and updated.
   - `Module/en-US/PSPublishModule-help.xml` updated.
6. Keep naming and discoverability consistent.
   - Prefer explicit entrypoint docs for `New-*` (scaffold), `New-Configuration*` (DSL object), `Invoke-*` (execute).
7. Commit generated docs only when source changes require them.

## High-Value Commands

```powershell
# Regenerate docs/help via normal module pipeline
.\Module\Build\Build-Module.ps1

# Scaffold about-topic source
New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About'
```

## Decision Rules

- Treat `Module/Docs` as generated output that can be overwritten.
- Keep durable guidance under `Docs/*.md`.
- When adding cmdlets/parameters, update XML docs comments first, then regenerate help.
- Add cross-links between docs so quickstart and deep reference docs stay connected.

## Reference Files (Read As Needed)

- `references/checklist.md` for preflight and validation sequence.
- `Docs/PSPublishModule.ModuleDocumentation.md` for authored-vs-generated rules.
- `Docs/PSPublishModule.DotNetPublish.Quickstart.md` for dotnet publish command usage patterns.
- `Module/Docs/New-ConfigurationDocumentation.md` for documentation segment parameters.
