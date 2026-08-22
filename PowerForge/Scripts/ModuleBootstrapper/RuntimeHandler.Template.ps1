# Ensure native runtime libraries are discoverable on Windows
$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if ($IsWindowsPlatform) {
    [array] $ResolvedLibraryDirectories = @(
        foreach ($LibraryFileName in $LibraryFileNames) {
            $ResolvedLibrary = & $ResolvePowerForgeModuleAssembly -LibraryFileName $LibraryFileName
            if ($null -ne $ResolvedLibrary -and -not [string]::IsNullOrWhiteSpace($ResolvedLibrary.Directory)) {
                $ResolvedLibrary.Directory
            }
        }
    ) | Select-Object -Unique
    if ($ResolvedLibraryDirectories.Count -gt 0) {
{{ArchitectureResolverBlock}}

    # Prefer the active managed-framework folder, then reuse a compatible native
    # runtime bundled under another framework folder. Native assets are selected
    # by process architecture and do not inherit the managed assembly TFM.
    $LibraryRoot = Join-Path -Path $PSScriptRoot -ChildPath 'Lib'
    $NativeLibraryDirectories = @(
        $ResolvedLibraryDirectories
        if (Test-Path -LiteralPath $LibraryRoot) {
            foreach ($LibraryDirectory in @(Get-ChildItem -LiteralPath $LibraryRoot -Directory -ErrorAction SilentlyContinue)) {
                if ($LibraryDirectory.FullName -notin $ResolvedLibraryDirectories) {
                    $LibraryDirectory.FullName
                }
            }
            if ($LibraryRoot -notin $ResolvedLibraryDirectories) {
                $LibraryRoot
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
}
