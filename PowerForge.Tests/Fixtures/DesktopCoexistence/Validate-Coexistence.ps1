param([Parameter(Mandatory = $true)][string] $Root)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'This regression requires Windows PowerShell 5.1.'
}
$dependencyPath = Join-Path $Root 'Module/Lib/Dependencies/DesktopCoexistence.Dependency.dll'
$expected = [Reflection.AssemblyName]::GetAssemblyName($dependencyPath)
$legacyPath = Join-Path $Root 'Legacy/DesktopCoexistence.LegacyHost.dll'
$earlierModule = New-Module -Name EarlierDesktopModule -ArgumentList $legacyPath, (Join-Path $Root 'Legacy/DesktopCoexistence.Dependency.dll') -ScriptBlock {
    param($Assembly, $Dependency)
    Add-Type -Path $Assembly -ErrorAction Stop
    [DesktopCoexistence.LegacyResolver]::Register($Dependency)
    Export-ModuleMember
}
$earlierModule | Import-Module
try {
    $old = [DesktopCoexistence.LegacyResolver]::Dependency
    if ($expected.Version -ne [Version]'2.0.0.0' -or $old.GetName().Version -ne [Version]'1.0.0.0' -or
        $expected.GetPublicKeyToken().Length -eq 0 -or
        [BitConverter]::ToString($expected.GetPublicKeyToken()) -ne [BitConverter]::ToString($old.GetName().GetPublicKeyToken())) {
        throw 'The fixture did not create two strong-named versions with the same key.'
    }
    if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object FullName -EQ $expected.FullName).Count -ne 0) {
        throw 'The new dependency was loaded before the generated Libraries.ps1 import.'
    }
    $blind = [DesktopCoexistence.LegacyResolver]::Handler.Invoke($null, [ResolveEventArgs]::new($expected.FullName))
    if ($blind.FullName -ne $old.FullName) {
        throw 'The earlier handler does not reproduce the version-blind binding defect.'
    }

    # Do not manually preload either current assembly: Libraries.ps1 must Add-Type the module
    # and discover DependencyDerived, whose base exists only in the new dependency version.
    try {
        Import-Module (Join-Path $Root 'Module/PowerForgeDesktopCoexistence.psm1') -Force -ErrorAction Stop
    } catch [Reflection.ReflectionTypeLoadException] {
        throw ('Generated module type discovery failed: ' + ($_.Exception.LoaderExceptions.Message -join '; '))
    }
    if ((Get-DesktopCoexistence) -ne 'current-dependency') {
        throw 'The imported command did not execute against the new dependency.'
    }
    $owner = [PowerForgeDesktopCoexistence.DependencyDerived].Assembly
    $actual = [PowerForgeDesktopCoexistence.DependencyDerived].BaseType.Assembly
    if ($actual.FullName -ne $expected.FullName -or $actual.Location -ne $dependencyPath) {
        throw "The module bound to the wrong dependency: $($actual.FullName) at $($actual.Location)."
    }
    'DESKTOP_COEXISTENCE_IMPORT_OK'

    $module = Get-Module PowerForgeDesktopCoexistence
    $state = & $module { $PowerForgeDesktopAssemblyResolver }
    if ($null -eq $state -or -not $state.Registered -or $state.BootstrapActive) {
        throw 'The imported resolver did not enter scoped runtime mode.'
    }
    & (Join-Path $PSScriptRoot 'Assert-ResolverScope.ps1') -ResolverState $state -Owner $owner -Foreign ([DesktopCoexistence.LegacyResolver].Assembly) -Expected $expected

    [DesktopCoexistence.LegacyResolver]::RegisterLater()
    foreach ($remove in @($false, $true)) {
        if ($remove) {
            Remove-Module PowerForgeDesktopCoexistence -ErrorAction Stop
            if ($state.Registered -or (Get-Module PowerForgeDesktopCoexistence)) {
                throw 'Removal left the module or its resolver registered.'
            }
            if ($null -ne $state.Handler.Invoke($null, [ResolveEventArgs]::new($expected.FullName, $owner))) {
                throw 'The removed resolver continued serving requests.'
            }
        }
        $earlierCount = [DesktopCoexistence.LegacyResolver]::EarlierProbes
        $laterCount = [DesktopCoexistence.LegacyResolver]::LaterProbes
        [DesktopCoexistence.LegacyResolver]::ProbeOtherHandlers()
        if ([DesktopCoexistence.LegacyResolver]::EarlierProbes -le $earlierCount -or
            [DesktopCoexistence.LegacyResolver]::LaterProbes -le $laterCount) {
            throw "Another handler was detached (after removal=$remove)."
        }
    }
    'DESKTOP_COEXISTENCE_REMOVAL_OK'
} finally {
    Remove-Module PowerForgeDesktopCoexistence -ErrorAction SilentlyContinue
    [DesktopCoexistence.LegacyResolver]::Unregister()
    Remove-Module EarlierDesktopModule -ErrorAction SilentlyContinue
}
