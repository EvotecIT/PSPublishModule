# Ensure native runtime libraries are discoverable on Windows
$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
# Skip probing when the current host cannot resolve a Windows-facing Lib folder (for example Desktop + Core-only payloads).
if ($IsWindowsPlatform -and $LibFolder) {
{{ArchitectureResolverBlock}}

    # Prefer the active managed-framework folder, then reuse a compatible native
    # runtime bundled under another framework folder. Native assets are selected
    # by process architecture and do not inherit the managed assembly TFM.
    $LibraryRoot = Join-Path -Path $PSScriptRoot -ChildPath 'Lib'
    $NativeLibraryFolders = @(
        $LibFolder
        if (Test-Path -LiteralPath $LibraryRoot) {
            foreach ($LibraryDirectory in @(Get-ChildItem -LiteralPath $LibraryRoot -Directory -ErrorAction SilentlyContinue)) {
                if ($LibraryDirectory.Name -ne $LibFolder) {
                    $LibraryDirectory.Name
                }
            }
        }
    )
    [array] $NativePaths = foreach ($NativeLibraryFolder in $NativeLibraryFolders) {
        $NativeCandidate = Join-Path -Path $PSScriptRoot -ChildPath ("Lib\{0}\runtimes\{1}\native" -f $NativeLibraryFolder, $ArchFolder)
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
