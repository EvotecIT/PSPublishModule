function Show-ModuleDocumentation {
    <#
    .SYNOPSIS
    Shows README/CHANGELOG or a chosen document for a module, with a simple console view.

    .DESCRIPTION
    Finds a module (by name or PSModuleInfo) and renders README/CHANGELOG from the module root
    or from its Internals folder (as defined in PrivateData.PSData.PSPublishModuleDelivery).
    You can also point directly to a docs folder via -DocsPath (e.g., output of Install-ModuleDocumentation).

    .PARAMETER Name
    Module name to show documentation for. Accepts pipeline by value.

    .PARAMETER Module
    A PSModuleInfo object (e.g., from Get-Module -ListAvailable) to operate on directly.

    .PARAMETER RequiredVersion
    Specific version of the module to target. If omitted, selects the highest available.

    .PARAMETER DocsPath
    A folder that contains documentation to display (e.g., the destination created by Install-ModuleDocumentation).
    When provided, the cmdlet does not look up the module and shows docs from this folder.

    .PARAMETER Readme
    Show README*. If both root and Internals copies exist, the root copy is preferred unless -PreferInternals is set.

    .PARAMETER Changelog
    Show CHANGELOG*. If both root and Internals copies exist, the root copy is preferred unless -PreferInternals is set.

    .PARAMETER File
    Relative path to a specific file to display (relative to module root or Internals). If rooted, used as-is.

    .PARAMETER PreferInternals
    Prefer the Internals copy of README/CHANGELOG when both exist.

    .PARAMETER List
    List available README/CHANGELOG files found (root and Internals) instead of displaying content.

    .PARAMETER Raw
    Output the raw file content (no styling).

    .PARAMETER Open
    Open the resolved file in the system default viewer instead of rendering in the console.

    .EXAMPLE
    Show-ModuleDocumentation -Name EFAdminManager -Readme

    .EXAMPLE
    Get-Module -ListAvailable EFAdminManager | Show-ModuleDocumentation -Changelog

    .EXAMPLE
    Show-ModuleDocumentation -DocsPath 'C:\Docs\EFAdminManager\3.0.0' -Readme -Open
    #>
    [CmdletBinding(DefaultParameterSetName='ByName')]
    param(
        [Parameter(ParameterSetName='ByName', Position=0, ValueFromPipeline=$true, ValueFromPipelineByPropertyName=$true)]
        [Alias('ModuleName')]
        [string] $Name,
        [Parameter(ParameterSetName='ByModule', ValueFromPipeline=$true)]
        [Alias('InputObject','ModuleInfo')]
        [System.Management.Automation.PSModuleInfo] $Module,
        [version] $RequiredVersion,
        [Parameter(ParameterSetName='ByPath')]
        [string] $DocsPath,
        [switch] $Readme,
        [switch] $Changelog,
        [switch] $License,
        [switch] $Intro,
        [switch] $Upgrade,
        [string] $File,
        [switch] $PreferInternals,
        [switch] $List,
        [switch] $Raw,
        [switch] $Open
    )

    begin {}
    process {
        $rootBase = $null
        $internalsBase = $null
        $moduleName = $null
        $moduleVersion = $null

        if ($PSCmdlet.ParameterSetName -eq 'ByPath') {
            if (-not $DocsPath) { throw 'Specify -DocsPath for the ByPath parameter set.' }
            if (-not (Test-Path -LiteralPath $DocsPath)) { throw "DocsPath '$DocsPath' not found." }
            $rootBase = $DocsPath
            $intCandidate = Join-Path $DocsPath 'Internals'
            if (Test-Path -LiteralPath $intCandidate) { $internalsBase = $intCandidate }
        } else {
            if ($PSCmdlet.ParameterSetName -eq 'ByName') {
                if (-not $Name) { throw "Specify -Name or pipe a module via -Module." }
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
                    $resolved = Get-Module -ListAvailable -Name $Module.Name | Where-Object { $_.Version -eq $RequiredVersion } | Sort-Object Version -Descending | Select-Object -First 1
                    if ($resolved) { $Module = $resolved } else { throw "Module '$($Module.Name)' with version $RequiredVersion not found." }
                }
            }
            $rootBase = $Module.ModuleBase
            $moduleName = $Module.Name
            $moduleVersion = $Module.Version

            $manifestPath = Join-Path $rootBase ("{0}.psd1" -f $Module.Name)
            $delivery = $null
            if (Test-Path -LiteralPath $manifestPath) {
                try { $manifest = Test-ModuleManifest -Path $manifestPath; $delivery = $manifest.PrivateData.PSData.PSPublishModuleDelivery } catch {}
            }
            $internalsRel = if ($delivery -and $delivery.InternalsPath) { [string]$delivery.InternalsPath } else { 'Internals' }
            $intCandidate = Join-Path $rootBase $internalsRel
            if (Test-Path -LiteralPath $intCandidate) { $internalsBase = $intCandidate }
        }

        if ($List) {
            $rows = @()
            if ($rootBase) {
                $rows += Get-ChildItem -LiteralPath $rootBase -Filter 'README*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Root' } }
                $rows += Get-ChildItem -LiteralPath $rootBase -Filter 'CHANGELOG*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Root' } }
                $rows += Get-ChildItem -LiteralPath $rootBase -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Root' } }
            }
            if ($internalsBase) {
                $rows += Get-ChildItem -LiteralPath $internalsBase -Filter 'README*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Internals' } }
                $rows += Get-ChildItem -LiteralPath $internalsBase -Filter 'CHANGELOG*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Internals' } }
                $rows += Get-ChildItem -LiteralPath $internalsBase -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name=$_.Name; FullName=$_.FullName; Area='Internals' } }
            }
            if ($rows.Count -eq 0) { Write-Warning 'No README/CHANGELOG found.' } else { $rows }
            return
        }

        $target = $null
        if ($File) {
            if ([System.IO.Path]::IsPathRooted($File)) {
                if (-not (Test-Path -LiteralPath $File)) { throw "File '$File' not found." }
                $target = (Get-Item -LiteralPath $File).FullName
            } else {
                $try1 = if ($rootBase) { Join-Path $rootBase $File }
                $try2 = if ($internalsBase) { Join-Path $internalsBase $File }
                if ($try1 -and (Test-Path -LiteralPath $try1)) { $target = (Get-Item -LiteralPath $try1).FullName }
                elseif ($try2 -and (Test-Path -LiteralPath $try2)) { $target = (Get-Item -LiteralPath $try2).FullName }
                else { throw "File '$File' not found under root or Internals." }
            }
        } elseif ($Readme) {
            $f = Resolve-DocFile -Kind 'README' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
            if ($f) { $target = $f.FullName } else { throw 'README not found.' }
        } elseif ($Changelog) {
            $f = Resolve-DocFile -Kind 'CHANGELOG' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
            if ($f) { $target = $f.FullName } else { throw 'CHANGELOG not found.' }
        } elseif ($License) {
            $f = Resolve-DocFile -Kind 'LICENSE' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
            if ($f) { $target = $f.FullName } else { throw 'LICENSE not found.' }
        } elseif ($Intro) {
            if ($manifest -and $manifest.PrivateData -and $manifest.PrivateData.PSData -and $manifest.PrivateData.PSData.PSPublishModuleDelivery -and $manifest.PrivateData.PSData.PSPublishModuleDelivery.IntroText) {
                $title = if ($moduleName) { "$moduleName $moduleVersion — Introduction" } else { 'Introduction' }
                Write-Heading -Text $title
                foreach ($line in [string[]]$manifest.PrivateData.PSData.PSPublishModuleDelivery.IntroText) { Write-Host $line }
                return
            } else {
                $f = Resolve-DocFile -Kind 'README' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
                if ($f) { $target = $f.FullName } else { throw 'Introduction not defined; README not found.' }
            }
        } elseif ($Upgrade) {
            if ($manifest -and $manifest.PrivateData -and $manifest.PrivateData.PSData -and $manifest.PrivateData.PSData.PSPublishModuleDelivery -and $manifest.PrivateData.PSData.PSPublishModuleDelivery.UpgradeText) {
                $title = if ($moduleName) { "$moduleName $moduleVersion — Upgrade" } else { 'Upgrade' }
                Write-Heading -Text $title
                foreach ($line in [string[]]$manifest.PrivateData.PSData.PSPublishModuleDelivery.UpgradeText) { Write-Host $line }
                return
            } else {
                $f = Resolve-DocFile -Kind 'UPGRADE' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
                if ($f) { $target = $f.FullName } else { throw 'Upgrade instructions not defined and no UPGRADE file found.' }
            }
        } else {
            # Default: README else CHANGELOG
            $f = Resolve-DocFile -Kind 'README' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals
            if (-not $f) { $f = Resolve-DocFile -Kind 'CHANGELOG' -RootBase $rootBase -InternalsBase $internalsBase -PreferInternals:$PreferInternals }
            if ($f) { $target = $f.FullName } else { throw 'No README or CHANGELOG found.' }
        }

        if ($Open) {
            Start-Process -FilePath $target | Out-Null
            return
        }

        if ($Raw) {
            Get-Content -LiteralPath $target -Raw -ErrorAction Stop
        } else {
            $title = if ($moduleName) { "$moduleName $moduleVersion — $([IO.Path]::GetFileName($target))" } else { [IO.Path]::GetFileName($target) }
            Write-Heading -Text $title
            try {
                $content = Get-Content -LiteralPath $target -Raw -ErrorAction Stop
                Write-Host $content
            } catch {
                Write-Warning "Failed to read '$target': $($_.Exception.Message)"
            }
        }
    }
}
