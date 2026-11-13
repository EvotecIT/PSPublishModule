if ($PSEdition -ne 'Core' -and $BinaryModuleDirectories.Count -gt 0) {
    # Windows PowerShell holds one copy of an assembly without a strong name per session. Reuse a compatible
    # copy another module already loaded and say so; stop here with one clear message when it cannot work.
    $script:PowerForgeDependencyCheck = [PowerForge.ModuleLoader.V1.DependencyConflictCheck]::Evaluate(
        $PowerForgeModuleName,
        $PowerForgeModuleRoot,
        $BinaryModuleDirectories,
        [string[]]{{IgnoredLibraryFileNames}})
    foreach ($DependencyWarning in $PowerForgeDependencyCheck.Warnings) {
        Write-Warning -Message $DependencyWarning
    }
    if ($null -ne $PowerForgeDependencyCheck.Error) {
        throw $PowerForgeDependencyCheck.Error
    }
    # AssemblyResolve also runs on worker threads that have no runspace, so the handler is compiled
    # code from the PowerForge loader and is limited to this module's own binary folders.
    $script:PowerForgeDesktopAssemblyResolver = [PowerForge.ModuleLoader.V1.DesktopAssemblyResolver]::new($BinaryModuleDirectories, [string[]]{{SharedAssemblyNames}})
    $PowerForgeDesktopAssemblyResolver.Register()
    $DesktopAssemblyResolver = $PowerForgeDesktopAssemblyResolver
    $PreviousOnRemove = $ExecutionContext.SessionState.Module.OnRemove
    $ExecutionContext.SessionState.Module.OnRemove = {
        try {
            $DesktopAssemblyResolver.Unregister()
        } finally {
            if ($null -ne $PreviousOnRemove) {
                & $PreviousOnRemove @args
            }
        }
    }.GetNewClosure()
}
