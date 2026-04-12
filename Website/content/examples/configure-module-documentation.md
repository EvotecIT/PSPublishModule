---
title: "Configure module documentation generation"
description: "Configure generated PowerShell command help and about-topic output."
layout: docs
---

This pattern is useful when a binary PowerShell module should generate Markdown help and external help XML during its build.

It is adapted from `Docs/PSPublishModule.ModuleDocumentation.md`.

## Example

```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -Settings {
    New-ConfigurationDocumentation `
        -Enable `
        -StartClean `
        -UpdateWhenNew `
        -Path 'Docs' `
        -PathReadme 'Docs\Readme.md' `
        -AboutTopicsSourcePath 'Help\About' `
        -SyncExternalHelpToProjectRoot
}
```

## What this demonstrates

- treating generated docs as build output
- using `Help/About` for durable about-topic sources
- keeping command help generated from authored PowerShell/XML documentation

## Source

- [PSPublishModule.ModuleDocumentation.md](https://github.com/EvotecIT/PSPublishModule/blob/master/Docs/PSPublishModule.ModuleDocumentation.md)

