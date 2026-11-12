# Type accelerators resolve from the module AssemblyLoadContext through the precompiled loader.
try {
    $PowerForgeTypeAcceleratorRegistration = [PowerForge.ModuleLoader.V1.TypeAcceleratorRegistration]::RegisterFromLoadContext(
        $ModuleAssembly,
        {{LibraryDirectoryExpression}},
        '{{Mode}}',
        [string[]]{{RequestedTypes}},
        [string[]]{{RequestedAssemblies}})
{{RegistrationTrackingBlock}}
} catch {
    Write-Warning -Message "ALC type accelerator registration failed: $($_.Exception.Message)"
}
