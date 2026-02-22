function {{UpdateCommandName}} {
    <#
    .SYNOPSIS
    Updates bundled module artefacts in a folder.

    .DESCRIPTION
    Delegates to {{InstallCommandName}}. Re-running the install command is typically enough to update new files while keeping existing configuration.

    .EXAMPLE
    {{UpdateCommandName}} -Path 'C:\Tools' -Verbose
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path,

        [ValidateSet('Merge', 'Overwrite', 'Skip', 'Stop')]
        [string] $OnExists = 'Merge',

        [switch] $Force,
        [switch] $ListOnly,
        [switch] $Unblock
    )

    if (-not (Get-Command -Name '{{InstallCommandName}}' -ErrorAction SilentlyContinue)) {
        throw "[{{ModuleName}}] Expected install command not found: {{InstallCommandName}}"
    }

    & '{{InstallCommandName}}' @PSBoundParameters
}
