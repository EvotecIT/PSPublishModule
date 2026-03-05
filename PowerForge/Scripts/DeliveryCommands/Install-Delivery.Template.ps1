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
    - Stop: throw an error

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

    function Resolve-DeliverySecret {
        [CmdletBinding()]
        param(
            [string] $InlineValue,
            [string] $PathValue
        )

        if (-not [string]::IsNullOrWhiteSpace($PathValue)) {
            if (-not (Test-Path -LiteralPath $PathValue)) {
                throw "[{{ModuleName}}] RepositoryCredentialSecretFilePath was provided but file does not exist: $PathValue"
            }

            return (Get-Content -LiteralPath $PathValue -Raw -ErrorAction Stop).Trim()
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
        if (-not [string]::IsNullOrWhiteSpace($resolvedSecret) -and [string]::IsNullOrWhiteSpace($RepositoryCredentialUserName)) {
            throw "[{{ModuleName}}] RepositoryCredentialUserName is required when repository secret is provided."
        }

        $installCommand = Get-Command -Name 'Install-Module' -ErrorAction SilentlyContinue
        if (-not $installCommand) {
            throw "[{{ModuleName}}] Install-Module is not available in this session."
        }

        if (-not $PSCmdlet.ShouldProcess('{{ModuleName}}', "Bootstrap module package before running {{CommandName}}")) { return }

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

        Install-Module @installParams | Out-Null

        $importParams = @{
            Name        = '{{ModuleName}}'
            Force       = $true
            ErrorAction = 'Stop'
        }
        if ($PSBoundParameters.ContainsKey('Version')) { $importParams.RequiredVersion = $Version }
        Import-Module @importParams | Out-Null

        $targetCandidates = Get-Command -Name '{{CommandName}}' -CommandType Function -All -ErrorAction SilentlyContinue |
            Where-Object { $_.ModuleName -eq '{{ModuleName}}' }
        if ($PSBoundParameters.ContainsKey('Version')) {
            $targetCandidates = $targetCandidates | Where-Object {
                $_.Version -and ([string]::Equals($_.Version.ToString(), $Version, [System.StringComparison]::OrdinalIgnoreCase))
            }
        }

        $targetCommand = $targetCandidates | Sort-Object -Property Version -Descending | Select-Object -First 1
        if (-not $targetCommand -and $PSBoundParameters.ContainsKey('Version')) {
            throw "[{{ModuleName}}] Unable to resolve {{CommandName}} for version '$Version' after bootstrap."
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
        throw "[{{ModuleName}}] Unable to resolve module base path."
    }

    $internalsRel = '{{InternalsPath}}'
    $internalsRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($moduleBase, $internalsRel))
    if (-not (Test-Path -LiteralPath $internalsRoot)) {
        throw "[{{ModuleName}}] Internals folder not found: $internalsRoot"
    }

    $dest = $Path
    if (-not [System.IO.Path]::IsPathRooted($dest)) {
        $dest = [System.IO.Path]::Combine((Get-Location).Path, $dest)
    }
    $dest = [System.IO.Path]::GetFullPath($dest)

    if (Test-Path -LiteralPath $dest) {
        switch ($OnExists) {
            'Skip' { return $dest }
            'Stop' { throw "[{{ModuleName}}] Destination already exists: $dest" }
            'Overwrite' { Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction Stop }
            default { }
        }
    }

    New-Item -ItemType Directory -Force -Path $dest | Out-Null

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
            continue
        }

        Copy-Item -LiteralPath $file -Destination $target -Force

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
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) { return }
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
        } catch { }
    }

    if ($includeRootChangelog) {
        try {
            Get-ChildItem -LiteralPath $moduleBase -Filter 'CHANGELOG*' -File -ErrorAction SilentlyContinue | ForEach-Object {
                $target = [System.IO.Path]::Combine($dest, $_.Name)
                if ((Test-Path -LiteralPath $target) -and (-not $Force)) { return }
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
        } catch { }
    }

    if ($includeRootLicense) {
        try {
            $lic = Get-ChildItem -LiteralPath $moduleBase -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($lic) {
                $target = [System.IO.Path]::Combine($dest, 'license.txt')
                if (-not ((Test-Path -LiteralPath $target) -and (-not $Force))) {
                    Copy-Item -LiteralPath $lic.FullName -Destination $target -Force
                }
            }
        } catch { }
    }

    return $dest
}
