# Each PowerShell 7 import loads this module into its own AssemblyLoadContext, and PowerShell cannot import
# the same cmdlet names from two assemblies into one session. Say so before loading anything. Windows
# PowerShell reuses the loaded assemblies instead; its dependency check decides whether that is compatible.
if ($PSEdition -eq 'Core') {
    $PowerForgeOtherModuleVersion = @(Get-Module -Name $PowerForgeModuleName | Where-Object {
        $_.ExportedCmdlets.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($_.Path) -and (Test-Path -LiteralPath $_.Path) -and
        -not [PowerForge.ModuleLoader.V1.ModulePath]::IsSameFile($_.Path, $PSCommandPath)
    })[0]
    if ($null -ne $PowerForgeOtherModuleVersion) {
        throw "$PowerForgeModuleName $($PowerForgeOtherModuleVersion.Version) is already imported from '$($PowerForgeOtherModuleVersion.ModuleBase)'. PowerShell cannot import the same cmdlets from two versions of a binary module into one session. Run 'Remove-Module $PowerForgeModuleName' and import it again to switch versions."
    }
}
