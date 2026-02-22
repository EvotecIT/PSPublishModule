
# PowerShell Module

This module was scaffolded with `PSPublishModule`.

## Build

Run:

```powershell
.\Build\Build-Module.ps1
```

## Documentation Workflow

- Treat `Docs\` and `en-US\*-help.xml` as generated output.
- Author command help via comment/XML docs in source.
- Author about topics under `Help\About\about_*.help.txt`.

Starter about topic template:

```powershell
New-ModuleAboutTopic -TopicName 'Overview' -OutputPath '.\Help\About'
```

The build pipeline can convert about topics into `Docs\About\*.md`.
