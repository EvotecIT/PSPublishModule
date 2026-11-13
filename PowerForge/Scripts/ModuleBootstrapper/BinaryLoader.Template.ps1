{{ModuleLoaderImportBlock}}
# Export assemblies for this host, resolved from the Lib layout when the module was built.
$BinaryModules = {{BinaryModules}}
$BinaryModulePaths = [string[]]@(foreach ($BinaryModule in $BinaryModules) {
    if ($BinaryModule['Path']) {
        $BinaryModule.Path = [IO.Path]::GetFullPath([IO.Path]::Combine($PowerForgeModuleRoot, $BinaryModule.Path))
        $BinaryModule.Path
    }
})
$BinaryModuleDirectories = [string[]]@($BinaryModulePaths | ForEach-Object { [IO.Path]::GetDirectoryName($_) } | Select-Object -Unique)
{{SharedAssembliesBlock}}{{DesktopAssemblyResolverBlock}}{{RuntimeHandlerBlock}}$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, '{{ModuleName}}.Libraries.ps1')
if ($PSEdition -ne 'Core' -and (Test-Path -LiteralPath $LibrariesScript)) {
    # Windows PowerShell loads the dependencies before the module assemblies that need them.
    . $LibrariesScript
}
$ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
$LoadedDirectories = [Collections.Generic.List[string]]::new()
foreach ($BinaryModule in $BinaryModules) {
    try {
        if ($BinaryModule['Error']) {
            throw $BinaryModule['Error']
        }
        $ModuleAssemblyPath = $BinaryModule.Path
        $LibraryDirectory = [IO.Path]::GetDirectoryName($ModuleAssemblyPath)
{{ImportBlock}}
        if (-not $LoadedDirectories.Contains($LibraryDirectory)) {
            $LoadedDirectories.Add($LibraryDirectory)
        }
    } catch {
        # A caller that does not stop on errors keeps the other export assemblies of the module.
        if ($ErrorActionPreference -eq 'Stop') {
            throw
        }
        Write-Warning -Message "Importing module $($BinaryModule.Name) failed. Fix errors before continuing. Error: $($_.Exception.Message)"
    }
}
{{LateLibrariesBlock}}{{DesktopTypeAcceleratorBlock}}if ($null -ne $PowerForgeDesktopAssemblyResolver) {
    $PowerForgeDesktopAssemblyResolver.BootstrapActive = $false
}
