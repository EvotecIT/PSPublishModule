Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force

# Example - Run project-specific lifecycle actions at stable module pipeline stages.

Build-Module -ModuleName 'MyGreatModule' -Path "C:\Support\GitHub" {
    New-ConfigurationManifest `
        -ModuleVersion '1.0.0' `
        -GUID '330e259e-799f-415d-8247-4843127620a1' `
        -Author 'Author' `
        -CompanyName 'CompanyName' `
        -Description 'Simple project MyGreatModule'

    New-ConfigurationBuild -Enable -MergeModuleOnBuild

    New-ConfigurationExecute `
        -Name 'Write build stamp' `
        -At AfterStaging `
        -InlineScript @'
$ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
$stampPath = Join-Path $ctx.ModuleRoot 'BuildStamp.txt'
"$($ctx.ModuleName) $($ctx.ResolvedVersion)" | Set-Content -LiteralPath $stampPath -Encoding UTF8
'@

    New-ConfigurationExecute `
        -Name 'Release notes guard' `
        -At BeforePublish `
        -InlineScript @'
$ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
$releaseNotes = Join-Path $ctx.ProjectRoot 'ReleaseNotes.md'
if (-not (Test-Path -LiteralPath $releaseNotes)) {
    throw "ReleaseNotes.md is required before publishing $($ctx.ModuleName) $($ctx.ResolvedVersion)."
}
'@

    New-ConfigurationExecute `
        -Name 'Advisory docs check' `
        -At AfterDocumentation `
        -FilePath '.\Build\Test-DocsShape.ps1' `
        -ContinueOnError
}
