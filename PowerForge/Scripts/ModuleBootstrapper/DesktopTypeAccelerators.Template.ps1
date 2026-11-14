if ($PSEdition -ne 'Core') {
    # Desktop loads module dependencies into the default AppDomain, so expose the same configured scripting types as Core.
    try {
        $PowerForgeTypeAcceleratorRegistration = [PowerForge.ModuleLoader.V1.TypeAcceleratorRegistration]::RegisterFromDirectory(
            ({{LibraryDirectoryExpression}}),
            '{{Mode}}',
            [string[]]{{RequestedTypes}},
            [string[]]{{RequestedAssemblies}},
            [string[]]{{IgnoredLibraryFileNames}},
            $PowerForgeModuleName,
            $PSScriptRoot)
{{RegistrationTrackingBlock}}
    } catch {
        Write-Warning -Message "Desktop type accelerator registration failed: $($_.Exception.Message)"
    }
}
