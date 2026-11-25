{{ModuleLoaderImportBlock}}
# Export assemblies for this host, resolved from the Lib layout when the module was built.
$BinaryModules = {{BinaryModules}}
$BinaryModulePaths = [string[]]@(foreach ($BinaryModule in $BinaryModules) {
    if ($BinaryModule['Path']) {
        $BinaryModule.Path = [IO.Path]::GetFullPath([IO.Path]::Combine($PowerForgeModuleRoot, $BinaryModule.Path))
        if ([IO.File]::Exists($BinaryModule.Path)) {
            $BinaryModule.Path
        } else {
            # A file removed after the build fails on its own instead of breaking the group of every export.
            $BinaryModule.Error = "Configured binary module '$($BinaryModule.Name)' was not found at '$($BinaryModule.Path)'."
        }
    }
})
# Import avoids cmdlets from Microsoft.PowerShell.Utility and .Management; loading either module costs more than the loader.
$BinaryModuleDirectorySet = [System.Collections.Generic.HashSet[string]]::new()
$BinaryModuleDirectories = [string[]]@(foreach ($BinaryModulePath in $BinaryModulePaths) {
    $BinaryModuleDirectory = [IO.Path]::GetDirectoryName($BinaryModulePath)
    if ($BinaryModuleDirectorySet.Add($BinaryModuleDirectory)) {
        $BinaryModuleDirectory
    }
})
{{SharedAssembliesBlock}}{{DesktopAssemblyResolverBlock}}{{RuntimeHandlerBlock}}$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, '{{ModuleName}}.Libraries.ps1')
if ($PSEdition -ne 'Core' -and [IO.File]::Exists($LibrariesScript)) {
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
