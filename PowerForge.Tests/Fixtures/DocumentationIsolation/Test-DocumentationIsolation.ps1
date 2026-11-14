<#
.SYNOPSIS
Proves rendered documentation commands coexist with a newer incompatible unsigned OfficeIMO assembly.
.PARAMETER ModulePath
Path to the candidate Desktop PSPublishModule manifest or bootstrapper, packaged with its dependencies and loader.
.PARAMETER FixturePath
Output path in the calling test's disposable directory for the newer assembly, removed after this process exits.
.EXAMPLE
& .\Test-DocumentationIsolation.ps1 -ModulePath C:\candidate\PSPublishModule.psd1 -FixturePath C:\test\OfficeIMO.Markdown.dll
.OUTPUTS
Writes DOCUMENTATION_ISOLATION_OK on success and throws on a contract failure.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ModulePath,
    [Parameter(Mandatory = $true)]
    [string] $FixturePath
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'This regression requires Windows PowerShell 5.1.'
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('pf-documentation-isolation-' + [Guid]::NewGuid().ToString('N'))
$originalModulePath = $env:PSModulePath
$null = New-Item -ItemType Directory -Path $root
try {
    Import-Module -Name $ModulePath -ErrorAction Stop
    $null = [PSPublishModule.GetModuleDocumentationCommand].Assembly.GetTypes()
    $officeBefore = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
        $_.GetName().Name -in @('OfficeIMO.Core', 'OfficeIMO.Markdown', 'HtmlForgeX.Markdown', 'HtmlForgeX')
    })
    if ($officeBefore.Count -ne 0) {
        throw 'Import or type discovery eagerly loaded the legacy rendering assemblies.'
    }

    # A real, unsigned assembly identity with only the newer API: old rendering cannot bind to it.
    $fixturePath = $FixturePath
    Add-Type -TypeDefinition @'
using System.Reflection;
[assembly: AssemblyVersion("99.0.0.0")]
namespace OfficeIMO.Markdown {
    public static class MarkdownReader {
        public static string NewApiOnly() { return "new-officeimo-fixture"; }
    }
}
'@ -OutputAssembly $fixturePath
    # Match real module loading: the first unsigned LoadFrom identity is shared.
    # The C# test owns this file and removes it after this process releases the lock.
    $null = [Reflection.Assembly]::LoadFrom($fixturePath)
    if ([OfficeIMO.Markdown.MarkdownReader]::NewApiOnly() -ne 'new-officeimo-fixture') {
        throw 'The incompatible OfficeIMO fixture was not loaded.'
    }
    $fixtureAssembly = [OfficeIMO.Markdown.MarkdownReader].Assembly
    if ($fixtureAssembly.GetName().GetPublicKeyToken().Length -ne 0) {
        throw 'The OfficeIMO fixture must be unsigned.'
    }

    $docs = Join-Path $root 'DocumentationIsolation'
    $null = New-Item -ItemType Directory -Path $docs
    [IO.File]::WriteAllText((Join-Path $docs 'README.md'), "# Isolation heading`n`nRendered documentation survives.`n")
    [IO.File]::WriteAllText((Join-Path $docs 'DocumentationIsolation.psd1'), @'
@{
    RootModule = 'DocumentationIsolation.psm1'
    ModuleVersion = '1.0.0'
    GUID = '9bf646e1-b198-46ee-8028-5bb8074cc1f9'
    Author = 'Documentation regression fixture'
    FunctionsToExport = @('Get-IsolatedDocumentationCommand')
    PrivateData = @{
        PSData = @{
            ExternalModuleDependencies = @('DocumentationIsolation.Dependency')
        }
    }
}
'@)
    [IO.File]::WriteAllText((Join-Path $docs 'DocumentationIsolation.psm1'), @'
if ([OfficeIMO.Markdown.MarkdownReader]::NewApiOnly() -ne 'new-officeimo-fixture') {
    throw 'The target module requires the newer OfficeIMO API in its calling host.'
}
function Get-IsolatedDocumentationCommand {
    <#
    .SYNOPSIS
    Command help from the newer OfficeIMO module survives isolation.
    #>
    [CmdletBinding()]
    param()
    'documentation-command'
}
Export-ModuleMember -Function Get-IsolatedDocumentationCommand
'@)
    $env:PSModulePath = $root + [IO.Path]::PathSeparator + $originalModulePath
    $originalOut = [Console]::Out
    $capturedOut = [IO.StringWriter]::new()
    try {
        [Console]::SetOut($capturedOut)
        Get-ModuleDocumentation -DocsPath $docs -Readme
        $rendered = $capturedOut.ToString()
        if ($rendered -notmatch 'Isolation heading' -or $rendered -notmatch 'Rendered documentation survives\.' -or
            $rendered -match '# Isolation heading|Failed to render|Failed to read') {
            throw "The console command did not render the documentation: $rendered"
        }
        $capturedOut.GetStringBuilder().Clear() | Out-Null
        Get-ModuleDocumentation -DocsPath $docs -Readme -Raw
        if ($capturedOut.ToString() -notmatch '# Isolation heading') {
            throw 'Raw documentation no longer preserves Markdown.'
        }
    }
    finally {
        [Console]::SetOut($originalOut)
        $capturedOut.Dispose()
    }

    $htmlPath = Join-Path $root 'documentation.html'
    $records = @(Show-ModuleDocumentation -ModuleBase $docs `
        -OutputPath $htmlPath -DoNotShow -Verbose 4>&1)
    $returnedPaths = @($records | Where-Object { $_ -is [string] })
    if ($returnedPaths.Count -ne 1 -or $returnedPaths[0] -ne $htmlPath -or -not (Test-Path -LiteralPath $htmlPath)) {
        throw 'The HTML command did not return its real exported path.'
    }
    $html = [IO.File]::ReadAllText($htmlPath)
    if ($html -notmatch 'Isolation heading' -or $html -notmatch 'Rendered documentation survives\.' -or
        $html -notmatch 'DocumentationIsolation.Dependency' -or
        $html -notmatch 'Command help from the newer OfficeIMO module survives isolation\.') {
        throw 'The HTML command did not render the selected documentation, dependency, and Commands help.'
    }
    if (-not @($records | Where-Object { $_ -is [Management.Automation.VerboseRecord] -and
        $_.Message -eq 'Finalizing HTML...' }).Count) {
        throw 'Worker exporter logs were not forwarded to the parent verbose stream.'
    }
    $officeAfter = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
        $_.GetName().Name -in @('OfficeIMO.Core', 'OfficeIMO.Markdown', 'HtmlForgeX.Markdown', 'HtmlForgeX')
    })
    if ($officeAfter.Count -ne 1 -or $officeAfter[0] -ne $fixtureAssembly -or
        [OfficeIMO.Markdown.MarkdownReader]::NewApiOnly() -ne 'new-officeimo-fixture') {
        throw 'Documentation rendering polluted or replaced the shared OfficeIMO identity.'
    }
    'DOCUMENTATION_ISOLATION_OK'
}
finally {
    $env:PSModulePath = $originalModulePath
    # This test owns the GUID root and every file below it.
    Remove-Item -LiteralPath $root -Recurse
}
