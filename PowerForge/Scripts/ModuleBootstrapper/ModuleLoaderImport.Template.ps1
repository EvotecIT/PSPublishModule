# Load the precompiled PowerForge module loader built for this PowerShell edition.
if (-not ('{{LoaderProbeType}}' -as [type])) {
    $PowerForgeLoaderEdition = if ($PSEdition -eq 'Core') { 'Core' } else { 'Desktop' }
    $PowerForgeLoaderPath = [IO.Path]::Combine({{LoaderRootParts}}, $PowerForgeLoaderEdition, '{{LoaderFileName}}')
    try {
        # Import-Module loads the assembly without importing Microsoft.PowerShell.Utility, as Add-Type would.
        # The loader declares no commands; removing its module object keeps only the loaded types.
        $PowerForgeLoaderModule = Microsoft.PowerShell.Core\Import-Module -Name $PowerForgeLoaderPath -PassThru -ErrorAction Stop
        Microsoft.PowerShell.Core\Remove-Module -ModuleInfo $PowerForgeLoaderModule -Force
    } catch {
        # Another runspace can load the same loader between the probe and the import.
        if (-not ('{{LoaderProbeType}}' -as [type])) {
            throw
        }
    }
}
