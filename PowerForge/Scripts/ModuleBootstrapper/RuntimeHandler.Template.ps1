# Ensure native runtime libraries are discoverable on Windows
$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
# Skip probing when the current host cannot resolve a Windows-facing Lib folder (for example Desktop + Core-only payloads).
if ($IsWindowsPlatform -and $LibFolder) {
{{ArchitectureResolverBlock}}

    $NativePath = Join-Path -Path $PSScriptRoot -ChildPath ("Lib\{0}\runtimes\{1}\native" -f $LibFolder, $ArchFolder)
    $PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }
    if ((Test-Path -LiteralPath $NativePath) -and ($PathEntries -notcontains $NativePath)) {
        # Prepend the module-native runtime path so the packaged payload wins over unrelated machine-wide copies.
        if ([string]::IsNullOrWhiteSpace($env:PATH)) {
            $env:PATH = $NativePath
        } else {
            $env:PATH = "$NativePath$([IO.Path]::PathSeparator)$env:PATH"
        }
    }
}
