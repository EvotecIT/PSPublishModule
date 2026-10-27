param(
    [Parameter(Mandatory = $true)][string] $Root,
    [Parameter(Mandatory = $true)][string] $Preload,
    [switch] $Development,
    [switch] $Upgrade,
    [string] $OldLoaderType
)
$ErrorActionPreference = 'Stop'
if ($Development) { $env:PF_REVIEW_DEVELOPMENT = 'true' }
if ($Upgrade) {
    Import-Module (Join-Path $Root 'v1\Retained.psm1') -Force
    if ((Get-RetainedData) -ne 'old') { throw 'Old generated module did not import' }
    $oldTypeName = if ($OldLoaderType) { $OldLoaderType } elseif ($Development) {
        'Retained.DevelopmentModuleLoadContext.ModuleAssemblyLoadContext'
    } else { 'Retained.ModuleLoadContext.ModuleAssemblyLoadContext' }
    $oldType = $oldTypeName -as [type]
    if ($null -eq $oldType) { throw 'This reproduction requires the actual old generated helper' }
    Remove-Module Retained -Force
    if (($oldTypeName -as [type]) -ne $oldType) { throw 'Old helper was not retained' }
}
Import-Module (Join-Path $Root 'v2\Retained.psm1') -Force
$shared = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -in @('SharedCore', 'SharedDisplay') })
$expectedInitialNames = @(switch ($Preload) {
    'none' { @() }
    'core' { @('SharedCore') }
    'display' { @('SharedDisplay') }
})
if ($shared.Count -ne @($expectedInitialNames).Count) { throw 'Optional assemblies loaded before use' }
foreach ($name in $expectedInitialNames) {
    if (@($shared | Where-Object { $_.GetName().Name -eq $name }).Count -ne 1) { throw "Missing preload: $name" }
}

$newHost = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
    $_.GetName().Name -eq 'Retained.Host' -and $_.Location -like "*$([IO.Path]::DirectorySeparatorChar)v2$([IO.Path]::DirectorySeparatorChar)*"
})[0]
if ($null -eq $newHost) { throw 'New physical module host was not loaded' }
if ($PSEdition -eq 'Core') {
    $privateContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($newHost)
    $display = $privateContext.LoadFromAssemblyName([Reflection.AssemblyName]::new('SharedDisplay'))
} else {
    $display = [Reflection.Assembly]::LoadFrom((Join-Path $Root 'v2\Lib\Default\SharedDisplay.dll'))
}
# Display has been loaded but its method has not been JIT-compiled. Its Core
# dependency must still resolve after the product's scoped resolver is removed.
Remove-Module Retained -Force
$result = $display.GetType('SharedDisplay.Broker').GetMethod('Register').Invoke($null, @())
if ($result -ne 'display1:1') { throw "Lazy dependency did not resolve: $result" }
foreach ($name in 'SharedCore', 'SharedDisplay') {
    $loaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $name })
    if ($loaded.Count -ne 1) { throw "Expected one $name, found $($loaded.Count)" }
    if ($PSEdition -eq 'Core' -and [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($loaded[0]) -ne [System.Runtime.Loader.AssemblyLoadContext]::Default) {
        throw "$name was privately duplicated by the retained old helper"
    }
}
Import-Module (Join-Path $Root 'v2\Retained.psm1') -Force
if ((Get-RetainedData) -ne 'shared:display1:1') { throw 'New data cmdlet did not use the shared runtime' }
if ($PSEdition -eq 'Core' -and $privateContext -eq [System.Runtime.Loader.AssemblyLoadContext]::Default) { throw 'Product host escaped its private context' }
Remove-Module Retained -Force
"REVIEW_PROOF_OK:$PSEdition`:development=$Development`:upgrade=$Upgrade`:preload=$Preload"
