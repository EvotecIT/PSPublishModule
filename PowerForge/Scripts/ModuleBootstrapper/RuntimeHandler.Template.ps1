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
    [array] $MissingNativePaths = foreach ($NativePath in $NativePaths) {
        if ($PathEntries -notcontains $NativePath) {
            $NativePath
        }
    }
    if ($MissingNativePaths.Count -gt 0) {
        # Prepend every module-native runtime path so split dependency sets remain complete.
        # The active managed-framework folder stays first and wins on duplicate file names.
        $NativePrefix = [string]::Join([IO.Path]::PathSeparator, $MissingNativePaths)
        if ([string]::IsNullOrWhiteSpace($env:PATH)) {
            $env:PATH = $NativePrefix
        } else {
            $env:PATH = "$NativePrefix$([IO.Path]::PathSeparator)$env:PATH"
        }
    }
}
