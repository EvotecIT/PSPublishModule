function {{CommandName}} {
    <#
    .SYNOPSIS
    Installs bundled module artefacts to a folder.

    .DESCRIPTION
    Copies files from the module's '{{InternalsPath}}' folder into a destination path.
    By default, existing files are preserved (OnExists=Merge) so local configuration is not overwritten.
    You can define default merge behavior for relative paths using PreservePaths/OverwritePaths.
    Optional bootstrap parameters can install/import a selected module version before extraction.

    .PARAMETER Path
    Destination folder for extracted artefacts.

    .PARAMETER OnExists
    What to do when the destination folder already exists:
    - Merge (default): keep existing files; copy only missing files (use -Force to overwrite files)
    - Overwrite: delete the destination folder and recreate it
    - Skip: do nothing
    - Stop: emit an error and stop processing

    .PARAMETER Force
    When OnExists=Merge, overwrites existing files.

    .PARAMETER ListOnly
    Shows planned copy actions without writing any changes.

    .PARAMETER Unblock
    On Windows, removes Zone.Identifier (best effort) from copied files.

    .PARAMETER PreservePaths
    Optional wildcard patterns (relative to Internals) to preserve during merge, for example: Config/**.

    .PARAMETER OverwritePaths
    Optional wildcard patterns (relative to Internals) to overwrite during merge, for example: Artefacts/**.

    .PARAMETER Bootstrap
    Forces bootstrap behavior: installs/imports this module before extraction.

    .PARAMETER Version
    Optional module version to bootstrap before extraction.

    .PARAMETER Repository
    Optional repository name used by Install-Module during bootstrap.

    .PARAMETER AllowPrerelease
    Allows prerelease versions during bootstrap install.

    .PARAMETER RepositoryCredentialUserName
    Optional repository credential username used during bootstrap install.

    .PARAMETER RepositoryCredentialSecret
    Optional repository credential secret used during bootstrap install.

    .PARAMETER RepositoryCredentialSecretFilePath
    Optional path to a file containing repository credential secret used during bootstrap install.

    .EXAMPLE
    {{CommandName}} -Path 'C:\Tools' -Verbose
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

        [string[]] $PreservePaths = {{PreservePathsArray}},
        [string[]] $OverwritePaths = {{OverwritePathsArray}},

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

    function Write-DeliveryError {
        param(
            [Parameter(Mandatory)]
            [string] $Message,

            [Parameter(Mandatory)]
            [string] $ErrorId,

            [System.Management.Automation.ErrorCategory] $Category = [System.Management.Automation.ErrorCategory]::InvalidOperation,
            [object] $TargetObject = $null
        )

        $exception = [System.InvalidOperationException]::new($Message)
        $record = [System.Management.Automation.ErrorRecord]::new($exception, $ErrorId, $Category, $TargetObject)
        $PSCmdlet.WriteError($record)
    }

    function Resolve-DeliverySecret {
        [CmdletBinding()]
        param(
            [string] $InlineValue,
            [string] $PathValue
        )

        if (-not [string]::IsNullOrWhiteSpace($PathValue)) {
            if (-not (Test-Path -LiteralPath $PathValue)) {
                Write-DeliveryError -Message "[{{ModuleName}}] RepositoryCredentialSecretFilePath was provided but file does not exist: $PathValue" -ErrorId 'Delivery.BootstrapSecretFileMissing' -Category ([System.Management.Automation.ErrorCategory]::ObjectNotFound) -TargetObject $PathValue
                return $null
            }

            try {
                return (Get-Content -LiteralPath $PathValue -Raw -ErrorAction Stop).Trim()
            } catch {
                $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
                Write-DeliveryError -Message "[{{ModuleName}}] Failed to read repository secret file '$PathValue'. $message" -ErrorId 'Delivery.BootstrapSecretFileReadFailed' -Category ([System.Management.Automation.ErrorCategory]::ReadError) -TargetObject $PathValue
                return $null
            }
        }

        return $InlineValue
    }

    function Test-DeliveryPathMatch {
        [CmdletBinding()]
        param(
            [string] $RelativePath,
            [string[]] $Patterns
        )

        if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $false }
        if (-not $Patterns -or $Patterns.Count -eq 0) { return $false }

        $normalizedPath = $RelativePath.Replace('\', '/')
        foreach ($raw in $Patterns) {
            if ([string]::IsNullOrWhiteSpace($raw)) { continue }

            $pattern = $raw.Trim().Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($pattern)) { continue }
            if ($pattern.EndsWith('/')) { $pattern = "$pattern*" }
            $pattern = $pattern.Replace('**', '*')

            if ($normalizedPath -like $pattern) { return $true }

            if ($pattern.IndexOf('*') -lt 0 -and $pattern.IndexOf('?') -lt 0) {
                if ($normalizedPath.StartsWith("$pattern/", [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
                if ([string]::Equals($normalizedPath, $pattern, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
            }
        }

        return $false
    }

    function Get-DeliveryAction {
        [CmdletBinding()]
        param(
            [bool] $TargetExists,
            [string] $RelativePath
        )

        if (-not $TargetExists) { return 'Copy' }
        if ($Force) { return 'Overwrite' }

        if ($OnExists -eq 'Merge') {
            if (Test-DeliveryPathMatch -RelativePath $RelativePath -Patterns $PreservePaths) { return 'Keep' }
            if (Test-DeliveryPathMatch -RelativePath $RelativePath -Patterns $OverwritePaths) { return 'Overwrite' }
            return 'Keep'
        }

        return 'Overwrite'
    }

    $bootstrapRequested = (-not $__DeliveryNoBootstrap) -and (
        $Bootstrap -or
        $PSBoundParameters.ContainsKey('Version') -or
        $PSBoundParameters.ContainsKey('Repository') -or
        $AllowPrerelease -or
        $PSBoundParameters.ContainsKey('RepositoryCredentialUserName') -or
        $PSBoundParameters.ContainsKey('RepositoryCredentialSecret') -or
        $PSBoundParameters.ContainsKey('RepositoryCredentialSecretFilePath')
    )

    if ($bootstrapRequested) {
        $resolvedSecret = Resolve-DeliverySecret -InlineValue $RepositoryCredentialSecret -PathValue $RepositoryCredentialSecretFilePath
        if ($PSBoundParameters.ContainsKey('RepositoryCredentialSecretFilePath') -and $null -eq $resolvedSecret) {
            return
        }

        if (-not [string]::IsNullOrWhiteSpace($resolvedSecret) -and [string]::IsNullOrWhiteSpace($RepositoryCredentialUserName)) {
            Write-DeliveryError -Message "[{{ModuleName}}] RepositoryCredentialUserName is required when repository secret is provided." -ErrorId 'Delivery.BootstrapCredentialUserNameMissing' -Category ([System.Management.Automation.ErrorCategory]::InvalidArgument)
            return
        }

        $installCommand = Get-Command -Name 'Install-Module' -ErrorAction SilentlyContinue
        if (-not $installCommand) {
            Write-DeliveryError -Message "[{{ModuleName}}] Install-Module is not available in this session." -ErrorId 'Delivery.BootstrapInstallCommandMissing' -Category ([System.Management.Automation.ErrorCategory]::ObjectNotFound) -TargetObject 'Install-Module'
            return
        }

        if (-not $PSCmdlet.ShouldProcess('{{ModuleName}}', "Bootstrap module package before running {{CommandName}}")) { return }

        Write-Host "[{{ModuleName}}] Bootstrapping module package before extraction" -ForegroundColor Cyan
        if ($PSBoundParameters.ContainsKey('Version')) {
            Write-Host "  Version     : $Version" -ForegroundColor DarkGray
        }
        if ($PSBoundParameters.ContainsKey('Repository')) {
            Write-Host "  Repository  : $Repository" -ForegroundColor DarkGray
        }

        $installParams = @{
            Name        = '{{ModuleName}}'
            Scope       = 'CurrentUser'
            Force       = $true
            ErrorAction = 'Stop'
        }

        if ($PSBoundParameters.ContainsKey('Repository')) { $installParams.Repository = $Repository }
        if ($PSBoundParameters.ContainsKey('Version')) { $installParams.RequiredVersion = $Version }
        if ($AllowPrerelease) { $installParams.AllowPrerelease = $true }

        if (-not [string]::IsNullOrWhiteSpace($RepositoryCredentialUserName) -and -not [string]::IsNullOrWhiteSpace($resolvedSecret)) {
            $secure = ConvertTo-SecureString -String $resolvedSecret -AsPlainText -Force
            $credential = [pscredential]::new($RepositoryCredentialUserName, $secure)
            $installParams.Credential = $credential
        }

        try {
            Install-Module @installParams | Out-Null
        } catch {
            $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
            Write-DeliveryError -Message "[{{ModuleName}}] Bootstrap install failed. $message" -ErrorId 'Delivery.BootstrapInstallFailed' -Category ([System.Management.Automation.ErrorCategory]::InvalidOperation) -TargetObject '{{ModuleName}}'
            return
        }

        $importParams = @{
            Name        = '{{ModuleName}}'
            Force       = $true
            ErrorAction = 'Stop'
        }
        if ($PSBoundParameters.ContainsKey('Version')) { $importParams.RequiredVersion = $Version }

        try {
            Import-Module @importParams | Out-Null
        } catch {
            $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
            Write-DeliveryError -Message "[{{ModuleName}}] Bootstrap import failed. $message" -ErrorId 'Delivery.BootstrapImportFailed' -Category ([System.Management.Automation.ErrorCategory]::InvalidOperation) -TargetObject '{{ModuleName}}'
            return
        }

        $targetCandidates = Get-Command -Name '{{CommandName}}' -CommandType Function -All -ErrorAction SilentlyContinue |
            Where-Object { $_.ModuleName -eq '{{ModuleName}}' }

        if ($PSBoundParameters.ContainsKey('Version')) {
            $targetCandidates = $targetCandidates | Where-Object {
                $_.Version -and ([string]::Equals($_.Version.ToString(), $Version, [System.StringComparison]::OrdinalIgnoreCase))
            }
        }

        $targetCommand = $targetCandidates | Sort-Object -Property Version -Descending | Select-Object -First 1
        if (-not $targetCommand -and $PSBoundParameters.ContainsKey('Version')) {
            Write-DeliveryError -Message "[{{ModuleName}}] Unable to resolve {{CommandName}} for version '$Version' after bootstrap." -ErrorId 'Delivery.BootstrapVersionNotResolved' -Category ([System.Management.Automation.ErrorCategory]::ObjectNotFound) -TargetObject $Version
            return
        }

        if ($targetCommand) {
            $currentPath = $null
            $targetPath = $null
            $currentVersion = $null

            try { $currentPath = $MyInvocation.MyCommand.ScriptBlock.File } catch { $currentPath = $null }
            try { $targetPath = $targetCommand.ScriptBlock.File } catch { $targetPath = $null }
            try { $currentVersion = $MyInvocation.MyCommand.Module.Version } catch { $currentVersion = $null }

            $differentPath = -not [string]::IsNullOrWhiteSpace($targetPath) -and
                -not [string]::IsNullOrWhiteSpace($currentPath) -and
                -not [string]::Equals($targetPath, $currentPath, [System.StringComparison]::OrdinalIgnoreCase)
            $newerVersion = $targetCommand.Version -and $currentVersion -and ($targetCommand.Version -gt $currentVersion)
            $requestedVersionDifferent = $PSBoundParameters.ContainsKey('Version') -and $currentVersion -and
                -not [string]::Equals($currentVersion.ToString(), $Version, [System.StringComparison]::OrdinalIgnoreCase)

            if ($differentPath -or $newerVersion -or $requestedVersionDifferent) {
                Write-Host "[{{ModuleName}}] Bootstrap resolved to the imported module version. Re-running delivery using the imported command." -ForegroundColor DarkGray

                $forward = @{}
                foreach ($entry in $PSBoundParameters.GetEnumerator()) {
                    if ($entry.Key -eq '__DeliveryNoBootstrap') { continue }
                    $forward[$entry.Key] = $entry.Value
                }
                $forward['__DeliveryNoBootstrap'] = $true

                return & $targetCommand @forward
            }
        }
    }

    $moduleBase = $null
    try { $moduleBase = $MyInvocation.MyCommand.Module.ModuleBase } catch { $moduleBase = $null }
    if ([string]::IsNullOrWhiteSpace($moduleBase)) {
        Write-DeliveryError -Message "[{{ModuleName}}] Unable to resolve module base path." -ErrorId 'Delivery.ModuleBaseNotResolved'
        return
    }

    $internalsRel = '{{InternalsPath}}'
    $internalsRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($moduleBase, $internalsRel))
    if (-not (Test-Path -LiteralPath $internalsRoot)) {
        Write-DeliveryError -Message "[{{ModuleName}}] Internals folder not found: $internalsRoot" -ErrorId 'Delivery.InternalsMissing' -Category ([System.Management.Automation.ErrorCategory]::ObjectNotFound) -TargetObject $internalsRoot
        return
    }

    $dest = $Path
    if (-not [System.IO.Path]::IsPathRooted($dest)) {
        $dest = [System.IO.Path]::Combine((Get-Location).Path, $dest)
    }
    $dest = [System.IO.Path]::GetFullPath($dest)

    if (Test-Path -LiteralPath $dest) {
        switch ($OnExists) {
            'Skip' {
                Write-Host "[{{ModuleName}}] Destination already exists. Skipping package install: $dest" -ForegroundColor Yellow
                return $dest
            }
            'Stop' {
                Write-DeliveryError -Message "[{{ModuleName}}] Destination already exists: $dest" -ErrorId 'Delivery.DestinationExists' -Category ([System.Management.Automation.ErrorCategory]::ResourceExists) -TargetObject $dest
                return
            }
            'Overwrite' {
                try {
                    Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction Stop
                } catch {
                    $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
                    Write-DeliveryError -Message "[{{ModuleName}}] Failed to remove existing destination '$dest'. $message" -ErrorId 'Delivery.RemoveDestinationFailed' -Category ([System.Management.Automation.ErrorCategory]::WriteError) -TargetObject $dest
                    return
                }
            }
            default { }
        }
    }

    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    $files = [System.IO.Directory]::GetFiles($internalsRoot, '*', [System.IO.SearchOption]::AllDirectories)
    if ($ListOnly) {
        foreach ($file in $files) {
            $rel = $file.Substring($internalsRoot.Length).TrimStart('\','/')
            $target = [System.IO.Path]::Combine($dest, $rel)
            $exists = Test-Path -LiteralPath $target
            $action = Get-DeliveryAction -TargetExists $exists -RelativePath $rel
            [pscustomobject]@{ Source = $file; Destination = $target; Exists = $exists; Action = $action }
        }
        return
    }

    if (-not $PSCmdlet.ShouldProcess($dest, "Install artefacts from '$internalsRel'")) { return }

    Write-Host "[{{ModuleName}}] Installing bundled package content" -ForegroundColor Cyan
    Write-Host "  Source      : $internalsRoot" -ForegroundColor DarkGray
    Write-Host "  Destination : $dest" -ForegroundColor DarkGray
    Write-Host "  Mode        : $OnExists" -ForegroundColor DarkGray
    Write-Host "  File count  : $($files.Count)" -ForegroundColor DarkGray
    if ($PreservePaths.Count -gt 0) {
        Write-Host "  Preserve    : $($PreservePaths -join ', ')" -ForegroundColor DarkGray
    }
    if ($OverwritePaths.Count -gt 0) {
        Write-Host "  Overwrite   : $($OverwritePaths -join ', ')" -ForegroundColor DarkGray
    }

    $copiedCount = 0
    $overwrittenCount = 0
    $keptCount = 0
    $extraCopiedCount = 0

    foreach ($file in $files) {
        $rel = $file.Substring($internalsRoot.Length).TrimStart('\','/')
        $target = [System.IO.Path]::Combine($dest, $rel)
        $targetDir = [System.IO.Path]::GetDirectoryName($target)
        if ($targetDir -and -not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }

        $exists = Test-Path -LiteralPath $target
        $action = Get-DeliveryAction -TargetExists $exists -RelativePath $rel
        if ($action -eq 'Keep') {
            $keptCount++
            Write-Host "  [keep] $rel -> $target" -ForegroundColor DarkYellow
            continue
        }

        try {
            Copy-Item -LiteralPath $file -Destination $target -Force -ErrorAction Stop
        } catch {
            $message = if ($_.Exception) { $_.Exception.Message } else { "$_" }
            Write-DeliveryError -Message "[{{ModuleName}}] Failed to copy '$file' to '$target'. $message" -ErrorId 'Delivery.CopyFailed' -Category ([System.Management.Automation.ErrorCategory]::WriteError) -TargetObject $target
            return
        }

        if ($exists) {
            $overwrittenCount++
            Write-Host "  [overwrite] $rel -> $target" -ForegroundColor Yellow
        } else {
            $copiedCount++
            Write-Host "  [copy] $rel -> $target" -ForegroundColor Green
        }

        if ($Unblock -and ($env:OS -eq 'Windows_NT')) {
            try { Unblock-File -LiteralPath $target -ErrorAction SilentlyContinue } catch { }
        }
    }

    $includeRootReadme = {{IncludeRootReadme}}
    $includeRootChangelog = {{IncludeRootChangelog}}
    $includeRootLicense = {{IncludeRootLicense}}

    if ($includeRootReadme) {
        try {
            Get-ChildItem -LiteralPath $moduleBase -Filter 'README*' -File -ErrorAction SilentlyContinue | ForEach-Object {
                $target = [System.IO.Path]::Combine($dest, $_.Name)
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) {
                    $keptCount++
                    Write-Host "  [keep] $($_.Name) -> $target" -ForegroundColor DarkYellow
                    return
                }
                $exists = Test-Path -LiteralPath $target
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
                $extraCopiedCount++
                $action = if ($exists) { 'overwrite' } else { 'copy' }
                $color = if ($exists) { 'Yellow' } else { 'Green' }
                Write-Host "  [$action] $($_.Name) -> $target" -ForegroundColor $color
            }
        } catch {
            Write-Warning "[{{ModuleName}}] Failed to copy README content. $($_.Exception.Message)"
        }
    }

    if ($includeRootChangelog) {
        try {
            Get-ChildItem -LiteralPath $moduleBase -Filter 'CHANGELOG*' -File -ErrorAction SilentlyContinue | ForEach-Object {
                $target = [System.IO.Path]::Combine($dest, $_.Name)
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) {
                    $keptCount++
                    Write-Host "  [keep] $($_.Name) -> $target" -ForegroundColor DarkYellow
                    return
                }
                $exists = Test-Path -LiteralPath $target
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
                $extraCopiedCount++
                $action = if ($exists) { 'overwrite' } else { 'copy' }
                $color = if ($exists) { 'Yellow' } else { 'Green' }
                Write-Host "  [$action] $($_.Name) -> $target" -ForegroundColor $color
            }
        } catch {
            Write-Warning "[{{ModuleName}}] Failed to copy CHANGELOG content. $($_.Exception.Message)"
        }
    }

    if ($includeRootLicense) {
        try {
            $lic = Get-ChildItem -LiteralPath $moduleBase -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($lic) {
                $target = [System.IO.Path]::Combine($dest, 'license.txt')
                if (-not ((Test-Path -LiteralPath $target) -and (-not $Force))) {
                    $exists = Test-Path -LiteralPath $target
                    Copy-Item -LiteralPath $lic.FullName -Destination $target -Force
                    $extraCopiedCount++
                    $action = if ($exists) { 'overwrite' } else { 'copy' }
                    $color = if ($exists) { 'Yellow' } else { 'Green' }
                    Write-Host "  [$action] $($lic.Name) -> $target" -ForegroundColor $color
                } else {
                    $keptCount++
                    Write-Host "  [keep] $($lic.Name) -> $target" -ForegroundColor DarkYellow
                }
            }
        } catch {
            Write-Warning "[{{ModuleName}}] Failed to copy LICENSE content. $($_.Exception.Message)"
        }
    }

    Write-Host "[{{ModuleName}}] Package install complete" -ForegroundColor Cyan
    Write-Host "  Copied      : $copiedCount" -ForegroundColor DarkGray
    Write-Host "  Overwritten : $overwrittenCount" -ForegroundColor DarkGray
    Write-Host "  Kept        : $keptCount" -ForegroundColor DarkGray
    Write-Host "  Extra files : $extraCopiedCount" -ForegroundColor DarkGray

    return $dest
}
