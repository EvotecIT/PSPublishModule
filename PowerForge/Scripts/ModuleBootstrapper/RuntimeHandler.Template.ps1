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
    $NativePath = $null
    foreach ($NativeLibraryFolder in $NativeLibraryFolders) {
        $NativeCandidate = Join-Path -Path $PSScriptRoot -ChildPath ("Lib\{0}\runtimes\{1}\native" -f $NativeLibraryFolder, $ArchFolder)
        if (Test-Path -LiteralPath $NativeCandidate) {
            $NativePath = $NativeCandidate
            break
        }
    }
    $PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }
    if ($NativePath -and ($PathEntries -notcontains $NativePath)) {
        # Prepend the module-native runtime path so the packaged payload wins over unrelated machine-wide copies.
        if ([string]::IsNullOrWhiteSpace($env:PATH)) {
            $env:PATH = $NativePath
        } else {
            $env:PATH = "$NativePath$([IO.Path]::PathSeparator)$env:PATH"
        }
    }
}
