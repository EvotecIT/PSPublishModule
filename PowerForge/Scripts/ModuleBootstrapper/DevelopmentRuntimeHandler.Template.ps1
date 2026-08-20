# Ensure native runtime libraries are discoverable for the selected development binary.
$PowerForgeDevelopmentIsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if ($PowerForgeDevelopmentIsWindowsPlatform) {
{{ArchitectureResolverBlock}}
    $PowerForgeDevelopmentLibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)
    if ($PowerForgeDevelopmentLibFolder) {
        $PowerForgeDevelopmentNativePath = Join-Path -Path $PowerForgeDevelopmentLibFolder -ChildPath ("runtimes\{0}\native" -f $PowerForgeDevelopmentArchFolder)
        $PowerForgeDevelopmentPathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }
        if (Test-Path -LiteralPath $PowerForgeDevelopmentNativePath) {
            [array] $PowerForgeDevelopmentRemainingPathEntries = foreach ($PowerForgeDevelopmentPathEntry in $PowerForgeDevelopmentPathEntries) {
                if ($PowerForgeDevelopmentPathEntry -ne $PowerForgeDevelopmentNativePath) {
                    $PowerForgeDevelopmentPathEntry
                }
            }
            [array] $PowerForgeDevelopmentOrderedPathEntries = @($PowerForgeDevelopmentNativePath) + @($PowerForgeDevelopmentRemainingPathEntries)
            $env:PATH = [string]::Join([IO.Path]::PathSeparator, $PowerForgeDevelopmentOrderedPathEntries)
        }
    }
}
