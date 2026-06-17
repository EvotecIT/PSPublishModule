function {{UpdateCommandName}} {
    <#
    .SYNOPSIS
    Updates bundled module artefacts in a folder.

    .DESCRIPTION
    Delegates to {{InstallCommandName}} in Refresh mode by default. Refresh overwrites package files while keeping destination files that are not part of the package.

    .PARAMETER OnExists
    What to do when the destination folder already exists:
    - Merge: keep existing files; copy only missing files
    - Refresh (default): overwrite package files, but keep destination files that are not part of the package
    - Overwrite: delete the destination folder and recreate it
    - Skip: do nothing
    - Stop: emit an error and stop processing

    .PARAMETER PreservePaths
    Optional wildcard patterns (relative to Internals) to preserve during merge or refresh, for example: Config/**.

    .PARAMETER IncludePaths
    Optional wildcard patterns (relative to Internals) that define which package files are managed, for example: Config/*.sample.json, Scripts/*.ps1.
    Defaults to the generated install helper configuration.

    .PARAMETER ExcludePaths
    Optional wildcard patterns (relative to Internals) to skip. Exclusions win over IncludePaths, for example: Config/local/**.

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

        [ValidateSet('Merge', 'Refresh', 'Overwrite', 'Skip', 'Stop')]
        [string] $OnExists = 'Refresh',

        [switch] $Force,
        [switch] $ListOnly,
        [switch] $Unblock,

        [string[]] $IncludePaths,
        [string[]] $ExcludePaths,
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

    $forward = @{}
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        $forward[$entry.Key] = $entry.Value
    }
    if (-not $forward.ContainsKey('OnExists')) {
        $forward['OnExists'] = $OnExists
    }

    {{InstallCommandName}} @forward
}
