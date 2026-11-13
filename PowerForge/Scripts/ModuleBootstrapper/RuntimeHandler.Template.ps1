# Ensure native runtime libraries are discoverable on Windows
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows) -and $BinaryModuleDirectories.Count -gt 0) {
{{ArchitectureResolverBlock}}

    # Prefer the folders of this host's module assemblies, then reuse a compatible native
    # runtime bundled under another framework folder. Native assets are selected
    # by process architecture and do not inherit the managed assembly TFM.
    $NativeLibraryDirectories = @(
        $BinaryModuleDirectories
        foreach ($NativeRuntimeRoot in {{NativeRuntimeRoots}}) {
            $NativeRuntimeRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($PowerForgeModuleRoot, $NativeRuntimeRoot))
            if ($NativeRuntimeRoot -notin $BinaryModuleDirectories) {
                $NativeRuntimeRoot
            }
        }
    )
    [array] $NativePaths = foreach ($NativeLibraryDirectory in $NativeLibraryDirectories) {
        $NativeCandidate = Join-Path -Path $NativeLibraryDirectory -ChildPath ("runtimes\{0}\native" -f $ArchFolder)
        if (Test-Path -LiteralPath $NativeCandidate) {
            $NativeCandidate
        }
    }
    $PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }
    if ($NativePaths.Count -gt 0) {
        [array] $RemainingPathEntries = foreach ($PathEntry in $PathEntries) {
            if ($NativePaths -notcontains $PathEntry) {
                $PathEntry
            }
        }
        # Rebuild the module-owned prefix on every import. This keeps the active
        # managed-framework folder first even when an earlier import already
        # inserted a fallback folder, while preserving unrelated PATH order.
        [array] $OrderedPathEntries = @($NativePaths) + @($RemainingPathEntries)
        $env:PATH = [string]::Join([IO.Path]::PathSeparator, $OrderedPathEntries)
    }
}
