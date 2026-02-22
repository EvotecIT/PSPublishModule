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
    - Stop: throw an error

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

    foreach ($file in $files) {
        $rel = $file.Substring($internalsRoot.Length).TrimStart('\','/')
        $target = [System.IO.Path]::Combine($dest, $rel)
        $targetDir = [System.IO.Path]::GetDirectoryName($target)
        if ($targetDir -and -not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }

        if ((Test-Path -LiteralPath $target) -and (-not $Force)) {
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
