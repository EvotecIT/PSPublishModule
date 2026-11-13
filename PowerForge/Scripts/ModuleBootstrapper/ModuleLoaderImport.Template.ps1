# Load the precompiled PowerForge module loader built for this PowerShell edition.
if (-not ('{{LoaderProbeType}}' -as [type])) {
    $PowerForgeLoaderEdition = if ($PSEdition -eq 'Core') { 'Core' } else { 'Desktop' }
    $PowerForgeLoaderPath = [IO.Path]::Combine({{LoaderRootParts}}, $PowerForgeLoaderEdition, '{{LoaderFileName}}')
    try {
        Add-Type -Path $PowerForgeLoaderPath -ErrorAction Stop
    } catch {
        # Another runspace can load the same loader between the probe and Add-Type.
        if (-not ('{{LoaderProbeType}}' -as [type])) {
            throw
        }
    }
}
