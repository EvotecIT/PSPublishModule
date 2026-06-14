---
topic: about_ModuleLifecycleActions
schema: 1.0.0
---
# about_ModuleLifecycleActions

## Short Description

Explains module build lifecycle actions, their stage ordering, and the JSON context passed to action scripts.

## Long Description

Module lifecycle actions run project-specific PowerShell at stable points in the module build pipeline.
They are declared with New-ConfigurationExecute inside an Invoke-ModuleBuild or Build-Module settings block.

The order of statements in the settings DSL does not decide when an action runs. The At value decides the lifecycle
point. This makes actions predictable even when a settings block is reorganized.

COMMON USES

Use lifecycle actions for:

- project-local generated-file updates
- staged-module inspection
- release guards before publish
- diagnostics after artifact or publish steps
- advisory checks with ContinueOnError

Prefer a reusable PowerForge feature instead when the behavior should be shared across repositories or owns
dependency, packaging, signing, versioning, or documentation rules.

CONTEXT

Before each action runs, PowerForge writes a JSON context file and sets POWERFORGE_CONTEXT to the file path.
Action scripts should read that context instead of guessing paths from the current directory.

The context includes:

- SchemaVersion
- Stage
- ActionName
- ModuleName
- ProjectRoot
- ExpectedVersion
- ResolvedVersion
- PreRelease
- StagingPath
- ManifestPath
- ModuleRoot
- DocumentationPath
- DocumentationReadmePath
- ArtefactPaths
- PublishDestinations
- ContextPath

PowerForge also exposes:

- POWERFORGE_CONTEXT
- POWERFORGE_ACTION_STAGE
- POWERFORGE_ACTION_NAME
- POWERFORGE_MODULE_NAME
- POWERFORGE_PROJECT_ROOT
- POWERFORGE_STAGING_PATH
- POWERFORGE_MANIFEST_PATH
- POWERFORGE_RESOLVED_VERSION

STAGES

Supported stages:

- BeforeDependencies, AfterDependencies
- BeforeVersioning, AfterVersioning
- BeforeStaging, AfterStaging
- BeforeBuild, AfterBuild
- BeforeManifest, AfterManifest
- BeforeDocumentation, AfterDocumentation
- BeforeFormatting, AfterFormatting
- BeforeValidation, AfterValidation
- BeforeTests, AfterTests
- BeforeSigning, AfterSigning
- BeforeArtefacts, AfterArtefacts
- BeforePublish, AfterPublish
- BeforeInstall, AfterInstall

Choose the earliest stage that has the context the script needs. AfterStaging has StagingPath and ModuleRoot.
AfterManifest has ManifestPath. AfterArtefacts has ArtefactPaths. AfterPublish has PublishDestinations.

FAILURE BEHAVIOR

By default, a failed action fails the build. Use ContinueOnError for advisory checks that should be recorded but
should not stop the pipeline.

## Examples


```powershell
PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
>>     New-ConfigurationBuild -Enable -MergeModuleOnBuild
>>     New-ConfigurationExecute -Name 'Inspect staged module' -At AfterStaging -InlineScript @'
>> $ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
>> Get-ChildItem -LiteralPath $ctx.ModuleRoot -Recurse | Select-Object -First 10 FullName
>> '@
>> }
```

Runs an inline action after staging and reads the stable JSON context file.

```powershell
PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
>>     New-ConfigurationBuild -Enable -MergeModuleOnBuild
>>     New-ConfigurationExecute -Name 'Release guard' -At BeforePublish -FilePath '.\Build\Test-ReleaseReady.ps1' -TimeoutSeconds 120
>> }
```

Runs a repository script before publish steps execute.

```powershell
PS> New-ConfigurationExecute -Name 'Advisory docs check' -At AfterDocumentation -FilePath '.\Build\Test-DocsShape.ps1' -ContinueOnError
```

Records a failed action in ActionResults but allows the pipeline to continue.

## Notes

See Docs\PSPublishModule.ModuleLifecycleActions.md for a longer how-to.
This file is source content for generated module documentation.
