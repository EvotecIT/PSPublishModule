function {{CommandName}} {
    <#
    .SYNOPSIS
    Installs bundled module artefacts to a folder.

    .DESCRIPTION
    Copies files from the module's '{{InternalsPath}}' folder into a destination path.
    By default, existing files are preserved (OnExists=Merge) so local configuration is not overwritten.

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
        [switch] $Unblock
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
            $action = if ($exists) { if ($Force) { 'Overwrite' } else { 'Keep' } } else { 'Copy' }
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
        if ($exists -and (-not $Force)) {
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
