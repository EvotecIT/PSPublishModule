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

    .PARAMETER CreateVersionSubfolder
    When set (default), content is placed under '<Path>\\<Name>\\<Version>'.
    If disabled, content is copied directly into '<Path>'.

    .PARAMETER Force
    Overwrite existing files.

    .EXAMPLE
    Install-ModuleDocumentation -Name EFAdminManager -Path 'C:\Docs'

    .EXAMPLE
    Get-Module -ListAvailable EFAdminManager | Install-ModuleDocumentation -Path 'D:\EFAM'
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
        [switch] $CreateVersionSubfolder = $true,
        [switch] $Force
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

        $internalsPath = Join-Path $Module.ModuleBase $internalsRel
        if (-not (Test-Path -LiteralPath $internalsPath)) {
            throw "Internals path '$internalsRel' not found under module base '$($Module.ModuleBase)'."
        }

        $dest = $Path
        if ($CreateVersionSubfolder) {
            $dest = Join-Path $dest (Join-Path $Module.Name $Module.Version.ToString())
        }

        if ($PSCmdlet.ShouldProcess("$internalsPath", "Copy to '$dest'")) {
            New-Item -ItemType Directory -Path $dest -Force | Out-Null

            # Copy Internals content preserving structure
            $sourcePattern = Join-Path $internalsPath '*'
            Copy-Item -Path $sourcePattern -Destination $dest -Recurse -Force:$Force.IsPresent -ErrorAction Stop

            # Copy selected root files from module base in a PS5/PS7-safe, readable way
            $rootFiles = @(Get-ChildItem -Path $Module.ModuleBase -File -ErrorAction SilentlyContinue)

            if ($includeReadme -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'README*') {
                        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force:$Force.IsPresent -ErrorAction SilentlyContinue
                    }
                }
            }
            if ($includeChlog -and $rootFiles.Count -gt 0) {
                foreach ($file in $rootFiles) {
                    if ($file.Name -like 'CHANGELOG*') {
                        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force:$Force.IsPresent -ErrorAction SilentlyContinue
                    }
                }
            }

            $resolvedTargets.Add($dest)
        }
    }
    end {
        if ($resolvedTargets.Count -gt 0) { $resolvedTargets | Select-Object -Unique }
    }
}
