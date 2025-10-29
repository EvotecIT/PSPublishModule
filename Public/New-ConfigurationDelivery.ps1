function New-ConfigurationDelivery {
    <#
    .SYNOPSIS
    Configures delivery metadata for bundling and installing internal docs/examples.

    .DESCRIPTION
    Adds Delivery options to the PSPublishModule configuration so the build embeds
    discovery metadata in the manifest (PrivateData.PSData.PSPublishModuleDelivery)
    and so the Internals folder is easy to find post-install by helper cmdlets
    such as Install-ModuleDocumentation.

    Typical usage is to call this in your Build\Manage-Module.ps1 alongside
    New-ConfigurationInformation -IncludeAll 'Internals\' so that the Internals
    directory is physically included in the shipped module and discoverable later.

    .PARAMETER Enable
    Enables delivery metadata emission. If not specified, nothing is emitted.

    .PARAMETER InternalsPath
    Relative path inside the module that contains internal deliverables
    (e.g. 'Internals'). Defaults to 'Internals'.

    .PARAMETER IncludeRootReadme
    Include module root README.* during installation (if present).

    .PARAMETER IncludeRootChangelog
    Include module root CHANGELOG.* during installation (if present).

    .PARAMETER ReadmeDestination
    Where to bundle README.* within the built module. One of: Internals, Root, Both, None. Default: Internals.

    .PARAMETER ChangelogDestination
    Where to bundle CHANGELOG.* within the built module. One of: Internals, Root, Both, None. Default: Internals.

    .EXAMPLE
    PS> New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IncludeRootReadme -IncludeRootChangelog
    Emits Options.Delivery and causes PrivateData.PSData.PSPublishModuleDelivery to be written in the manifest.

    .EXAMPLE
    PS> New-ConfigurationInformation -IncludeAll 'Internals\'
    PS> New-ConfigurationDelivery -Enable
    Minimal configuration that bundles Internals and exposes it to the installer.

    .NOTES
    This emits a Type 'Options' object under Options.Delivery so it works with the
    existing New-PrepareStructure logic without further changes.
    #>
    [CmdletBinding()]
    param(
        [switch] $Enable,
        [string] $InternalsPath = 'Internals',
        [switch] $IncludeRootReadme,
        [switch] $IncludeRootChangelog,
        [ValidateSet('Internals','Root','Both','None')]
        [string] $ReadmeDestination = 'Internals',
        [ValidateSet('Internals','Root','Both','None')]
        [string] $ChangelogDestination = 'Internals'
    )

    if (-not $Enable) { return }

    $delivery = [ordered] @{
        Enable               = $true
        InternalsPath        = $InternalsPath
        IncludeRootReadme    = $IncludeRootReadme.IsPresent
        IncludeRootChangelog = $IncludeRootChangelog.IsPresent
        ReadmeDestination    = $ReadmeDestination
        ChangelogDestination = $ChangelogDestination
        Schema               = '1.1'
    }

    [ordered] @{
        Type    = 'Options'
        Options = [ordered] @{ Delivery = $delivery }
    }
}
