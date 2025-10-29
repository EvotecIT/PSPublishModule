function Install-ModuleDocumentation {
    <#
    .SYNOPSIS
    Installs bundled module documentation/examples (Internals) to a chosen path.

    .DESCRIPTION
    Copies the contents of a module's Internals folder (or the path defined in
    PrivateData.PSData.PSPublishModuleDelivery) to a destination outside of
    $env:PSModulePath, optionally including README/CHANGELOG from module root.

    .PARAMETER Name
    Module name to install documentation for. Accepts pipeline by value.

    .PARAMETER RequiredVersion
    Specific version of the module to target. If omitted, selects the highest available.

    .PARAMETER Module
    A PSModuleInfo object (e.g., from Get-Module -ListAvailable) to operate on directly.

    .PARAMETER Path
    Destination directory where the Internals content will be copied.

    .PARAMETER Layout
    How to lay out the destination path:
    - Direct: copy into <Path>
    - Module: copy into <Path>\\<Name>
    - ModuleAndVersion (default): copy into <Path>\\<Name>\\<Version>

    .PARAMETER OnExists
    What to do if the destination folder already exists:
    - Merge (default): merge files/folders; overwrite files only when -Force is used
    - Overwrite: remove the existing destination, then copy fresh
    - Skip: do nothing and return the existing destination path
    - Stop: throw an error

    .PARAMETER CreateVersionSubfolder
    When set (default), content is placed under '<Path>\\<Name>\\<Version>'.
    If disabled, content is copied directly into '<Path>'.

    .PARAMETER Force
    Overwrite existing files.

    .PARAMETER ListOnly
    Show what would be copied and where, without copying any files. Returns the
    computed destination path(s). Use -Verbose for details.

    .PARAMETER Open
    After a successful copy, open the README in the destination (if present).

    .PARAMETER NoIntro
    Suppress introductory notes and important links printed after installation.

    .EXAMPLE
    Install-ModuleDocumentation -Name AdminManager -Path 'C:\Docs'

    .EXAMPLE
    Get-Module -ListAvailable AdminManager | Install-ModuleDocumentation -Path 'D:\AM'
    #>
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='ByName')]
    param(
        [Parameter(ParameterSetName='ByName', Position = 0, ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true)]
        [Alias('ModuleName')]
        [string] $Name,
        [Parameter(ParameterSetName='ByModule', ValueFromPipeline = $true)]
        [Alias('InputObject','ModuleInfo')]
        [System.Management.Automation.PSModuleInfo] $Module,
        [version] $RequiredVersion,
        [Parameter(Mandatory)]
        [string] $Path,
        [ValidateSet('Direct','Module','ModuleAndVersion')]
        [string] $Layout = 'ModuleAndVersion',
        [ValidateSet('Merge','Overwrite','Skip','Stop')]
        [string] $OnExists = 'Merge',
        [switch] $CreateVersionSubfolder, # legacy toggle: if bound and Layout not specified, maps to Direct/ModuleAndVersion
        [switch] $Force,
        [switch] $ListOnly,
        [switch] $Open,
        [switch] $NoIntro
    )

    begin {
        # Use a generic list for performance/compat across PS5/PS7
        $resolvedTargets = [System.Collections.Generic.List[string]]::new()
    }
    process {
        if ($PSCmdlet.ParameterSetName -eq 'ByName') {
            if (-not $Name) { throw "Specify -Name or provide a module via the pipeline." }
            $candidates = Get-Module -ListAvailable -Name $Name
            if (-not $candidates) { throw "Module '$Name' not found." }
            if ($RequiredVersion) {
                $Module = $candidates | Where-Object { $_.Version -eq $RequiredVersion } | Sort-Object Version -Descending | Select-Object -First 1
                if (-not $Module) { throw "Module '$Name' with version $RequiredVersion not found." }
            } else {
                $Module = $candidates | Sort-Object Version -Descending | Select-Object -First 1
            }
        } elseif ($PSCmdlet.ParameterSetName -eq 'ByModule') {
            if (-not $Module) { throw "Pipeline didn't pass a PSModuleInfo object. Use -Name or pipe Get-Module output." }
            if ($RequiredVersion -and $Module.Version -ne $RequiredVersion) {
                # Try to resolve the exact version if requested differs from piped object
                $resolved = Get-Module -ListAvailable -Name $Module.Name | Where-Object { $_.Version -eq $RequiredVersion } | Sort-Object Version -Descending | Select-Object -First 1
                if ($resolved) { $Module = $resolved } else { throw "Module '$($Module.Name)' with version $RequiredVersion not found." }
            }
        }

        $manifestPath = Join-Path $Module.ModuleBase ("{0}.psd1" -f $Module.Name)
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            throw "Manifest not found for module '$($Module.Name)' at '$manifestPath'."
        }

        $manifest = Test-ModuleManifest -Path $manifestPath
        $delivery = $manifest.PrivateData.PSData.PSPublishModuleDelivery

        $internalsRel = if ($delivery -and $delivery.InternalsPath) { [string]$delivery.InternalsPath } else { 'Internals' }
        $includeReadme = if ($null -ne $delivery.IncludeRootReadme) { [bool]$delivery.IncludeRootReadme } else { $true }
        $includeChlog = if ($null -ne $delivery.IncludeRootChangelog) { [bool]$delivery.IncludeRootChangelog } else { $true }
        $includeLicense = if ($null -ne $delivery.IncludeRootLicense) { [bool]$delivery.IncludeRootLicense } else { $true }

        $internalsPath = Join-Path $Module.ModuleBase $internalsRel
        if (-not (Test-Path -LiteralPath $internalsPath)) {
            throw "Internals path '$internalsRel' not found under module base '$($Module.ModuleBase)'."
        }

        # Back-compat: if legacy CreateVersionSubfolder was provided and Layout not changed, honor it
        if ($PSBoundParameters.ContainsKey('CreateVersionSubfolder') -and -not $PSBoundParameters.ContainsKey('Layout')) {
            $Layout = if ($CreateVersionSubfolder) { 'ModuleAndVersion' } else { 'Direct' }
        }

        switch ($Layout) {
            'Direct'            { $dest = $Path }
            'Module'            { $dest = Join-Path $Path $Module.Name }
            'ModuleAndVersion'  { $dest = Join-Path (Join-Path $Path $Module.Name) $Module.Version.ToString() }
        }

        # If listing only, do not copy — just output the planned destination
        if ($ListOnly) {
            Write-Verbose "Would copy Internals from '$internalsPath' to '$dest' using Layout=$Layout, OnExists=$OnExists."
            $resolvedTargets.Add($dest)
            return
        }

        if ($PSCmdlet.ShouldProcess("$internalsPath", "Copy to '$dest'")) {
            $exists = Test-Path -LiteralPath $dest
            if ($exists) {
                switch ($OnExists) {
                    'Skip' { $resolvedTargets.Add($dest); return }
                    'Stop' { throw "Destination '$dest' already exists." }
                    'Overwrite' {
                        Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue
                        New-Item -ItemType Directory -Path $dest -Force | Out-Null
                        Copy-PSPDirectoryTree -Source $internalsPath -Destination $dest -Overwrite:$true
                    }
                    'Merge' {
                        Copy-PSPDirectoryTree -Source $internalsPath -Destination $dest -Overwrite:$Force.IsPresent
                    }
                }
            } else {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
                Copy-PSPDirectoryTree -Source $internalsPath -Destination $dest -Overwrite:$Force.IsPresent
            }

            # Copy selected root files from module base in a PS5/PS7-safe, readable way
            $rootFiles = @(Get-ChildItem -Path $Module.ModuleBase -File -ErrorAction SilentlyContinue)

            if ($includeReadme -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'README*') {
                        try { Copy-Item -LiteralPath $file.FullName -Destination $dest -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
            }
            if ($includeChlog -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'CHANGELOG*') {
                        try { Copy-Item -LiteralPath $file.FullName -Destination $dest -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
            }
            if ($includeLicense -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'LICENSE*') {
                        try { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dest 'license.txt') -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
            }

            # Copy Intro/Upgrade files if explicitly provided in delivery metadata
            if ($delivery) {
                if ($delivery.IntroFile) {
                    $introSrc = Join-Path $Module.ModuleBase ([string]$delivery.IntroFile)
                    if (Test-Path -LiteralPath $introSrc) {
                        try { Copy-Item -LiteralPath $introSrc -Destination $dest -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
                if ($delivery.UpgradeFile) {
                    $upgradeSrc = Join-Path $Module.ModuleBase ([string]$delivery.UpgradeFile)
                    if (Test-Path -LiteralPath $upgradeSrc) {
                        try { Copy-Item -LiteralPath $upgradeSrc -Destination $dest -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
            }
            if ($includeLicense -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'LICENSE*') {
                        try { Copy-Item -LiteralPath $file.FullName -Destination $dest -Force:$Force.IsPresent -ErrorAction Stop } catch { }
                    }
                }
            }

            $resolvedTargets.Add($dest)

            # Intro and links (unless suppressed)
            if (-not $NoIntro) {
                $hasIntro = $false
                if ($delivery) {
                    if ($delivery.IntroText) {
                        $hasIntro = $true
                        Write-Host ''
                        Write-Host 'Introduction:' -ForegroundColor Cyan
                        foreach ($line in [string[]]$delivery.IntroText) { Write-Host "  $line" }
                    }
                    if ($delivery.IntroFile) {
                        $introDest = Join-Path $dest ([IO.Path]::GetFileName([string]$delivery.IntroFile))
                        if (Test-Path -LiteralPath $introDest) {
                            $hasIntro = $true
                            Write-Host ''
                            Write-Host "Introduction (from $([IO.Path]::GetFileName($introDest))):" -ForegroundColor Cyan
                            try { Write-Host (Get-Content -LiteralPath $introDest -Raw -ErrorAction Stop) } catch {}
                        }
                    }
                    if ($delivery.ImportantLinks) {
                        Write-Host ''
                        Write-Host 'Links:' -ForegroundColor Cyan
                        foreach ($l in $delivery.ImportantLinks) {
                            try {
                                $title = if ($l.Title) { $l.Title } elseif ($l.Name) { $l.Name } else { '' }
                                $url = $l.Url
                                if ($title -and $url) {
                                    Write-Host "  - $title"
                                    Write-Host "    $url"
                                } elseif ($url) {
                                    Write-Host "  - $url"
                                } elseif ($title) {
                                    Write-Host "  - $title"
                                }
                            } catch {}
                        }
                    }
                }
            }

            # Optionally open README in destination
            if ($Open) {
                try {
                    $readme = Get-ChildItem -LiteralPath $dest -Filter 'README*' -File -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($readme) {
                        if ($IsWindows) { Start-Process -FilePath $readme.FullName | Out-Null }
                        else { Start-Process -FilePath $readme.FullName | Out-Null }
                    } else {
                        # If README not found, open the destination folder
                        if ($IsWindows) { Start-Process -FilePath $dest | Out-Null }
                    }
                } catch {
                    Write-Verbose "Open failed: $($_.Exception.Message)"
                }
            }
        }
    }
    end {
        if ($resolvedTargets.Count -gt 0) { $resolvedTargets | Select-Object -Unique }
    }
}
