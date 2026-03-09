function {{UpdateCommandName}} {
    <#
    .SYNOPSIS
    Updates bundled module artefacts in a folder.

    .DESCRIPTION
    Delegates to {{InstallCommandName}}. Re-running the install command is typically enough to update new files while keeping existing configuration.

    .PARAMETER PreservePaths
    Optional wildcard patterns (relative to Internals) to preserve during merge, for example: Config/**.

    .PARAMETER OverwritePaths
    Optional wildcard patterns (relative to Internals) to overwrite during merge, for example: Artefacts/**.

    .PARAMETER Bootstrap
    Forces bootstrap behavior: installs/imports this module before extraction.

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
        [switch] $Unblock,

        [string[]] $PreservePaths,
        [string[]] $OverwritePaths,

        [switch] $Bootstrap,
        [string] $Version,
        [string] $Repository,
        [switch] $AllowPrerelease,
        [string] $RepositoryCredentialUserName,
        [string] $RepositoryCredentialSecret,
        [string] $RepositoryCredentialSecretFilePath,

        [Parameter(DontShow)]
        [switch] $__DeliveryNoBootstrap
    )

    if (-not (Get-Command -Name '{{InstallCommandName}}' -ErrorAction SilentlyContinue)) {
        $exception = [System.InvalidOperationException]::new("[{{ModuleName}}] Expected install command not found: {{InstallCommandName}}")
        $record = [System.Management.Automation.ErrorRecord]::new($exception, 'Delivery.InstallCommandMissing', [System.Management.Automation.ErrorCategory]::ObjectNotFound, '{{InstallCommandName}}')
        $PSCmdlet.WriteError($record)
        return
    }

    Write-Host "[{{ModuleName}}] Updating bundled package content via {{InstallCommandName}}" -ForegroundColor Cyan
    Write-Host "  Destination : $Path" -ForegroundColor DarkGray
    Write-Host "  Mode        : $OnExists" -ForegroundColor DarkGray

    & '{{InstallCommandName}}' @PSBoundParameters
}
