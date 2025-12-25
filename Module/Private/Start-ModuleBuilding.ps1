function Start-ModuleBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $PathToProject
    )
    $DestinationPaths = [ordered] @{ }
    if ($Configuration.Information.Manifest.CompatiblePSEditions) {
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Desktop') {
            $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        }
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Core') {
            $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
        }
    } else {
        # Means missing from config - send to both
        $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
    }

    [string] $Random = Get-Random 10000000000
    [string] $FullModuleTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
    [string] $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName + "_TEMP_$Random"
    if ($Configuration.Information.DirectoryProjects) {
        [string] $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
    } else {
        [string] $FullProjectPath = $PathToProject
    }
    [string] $ProjectName = $Configuration.Information.ModuleName

    $PSD1FilePath = [System.IO.Path]::Combine($FullProjectPath, "$ProjectName.psd1")
    $PSM1FilePath = [System.IO.Path]::Combine($FullProjectPath, "$ProjectName.psm1")

    if ($Configuration.Information.Manifest.ModuleVersion) {
        if ($Configuration.Steps.BuildModule.LocalVersion) {
            $Versioning = Step-Version -Module $Configuration.Information.ModuleName -ExpectedVersion $Configuration.Information.Manifest.ModuleVersion -Advanced -LocalPSD1 $PSD1FilePath
        } else {
            $Versioning = Step-Version -Module $Configuration.Information.ModuleName -ExpectedVersion $Configuration.Information.Manifest.ModuleVersion -Advanced
        }
        $Configuration.Information.Manifest.ModuleVersion = $Versioning.Version
    } else {
        # lets fake the version if there's no PSD1, and there's no version in config
        $Configuration.Information.Manifest.ModuleVersion = 1.0.0
    }
    Write-Text '----------------------------------------------------'
    Write-Text "[i] Project/Module Name: $ProjectName" -Color Yellow
    if ($Configuration.Steps.BuildModule.LocalVersion) {
        Write-Text "[i] Current Local Version: $($Versioning.CurrentVersion)" -Color Yellow
    } else {
        Write-Text "[i] Current PSGallery Version: $($Versioning.CurrentVersion)" -Color Yellow
    }
    Write-Text "[i] Expected Version: $($Configuration.Information.Manifest.ModuleVersion)" -Color Yellow
    Write-Text "[i] Full module temporary path: $FullModuleTemporaryPath" -Color Yellow
    Write-Text "[i] Full project path: $FullProjectPath" -Color Yellow
    Write-Text "[i] Full temporary path: $FullTemporaryPath" -Color Yellow
    Write-Text "[i] PSScriptRoot: $PSScriptRoot" -Color Yellow
    Write-Text "[i] Current PSEdition: $PSEdition" -Color Yellow
    Write-Text "[i] Destination Desktop: $($DestinationPaths.Desktop)" -Color Yellow
    Write-Text "[i] Destination Core: $($DestinationPaths.Core)" -Color Yellow
    Write-Text '----------------------------------------------------'

    if (-not $Configuration.Steps.BuildModule) {
        Write-Text '[-] Section BuildModule is missing. Terminating.' -Color Red
        return $false
    }
    # We need to make sure module name is set, otherwise bad things will happen
    if (-not $Configuration.Information.ModuleName) {
        Write-Text '[-] Section Information.ModuleName is missing. Terminating.' -Color Red
        return $false
    }
    # check if project exists
    if (-not (Test-Path -Path $FullProjectPath)) {
        Write-Text "[-] Project path doesn't exists $FullProjectPath. Terminating" -Color Red
        return $false
    }

    # Resolve install/build strategy early and compute assembly version to avoid in-session DLL conflicts
    $Roots = @()
    if ($DestinationPaths.Desktop) { $Roots += [System.IO.Path]::GetDirectoryName($DestinationPaths.Desktop) }
    if ($DestinationPaths.Core)    { $Roots += [System.IO.Path]::GetDirectoryName($DestinationPaths.Core) }
    $Roots = $Roots | Select-Object -Unique

    $strategy = 'AutoRevision'
    if ($Configuration.Steps.BuildModule.VersionedInstallStrategy) { $strategy = [string]$Configuration.Steps.BuildModule.VersionedInstallStrategy }
    # Auto-switch to Exact when any publish target is enabled
    $publishingPlanned = $false
    foreach ($n in @($Configuration.Steps.BuildModule.GalleryNugets)) { if ($n -and $n.Enabled) { $publishingPlanned = $true; break } }
    if (-not $publishingPlanned) { foreach ($n in @($Configuration.Steps.BuildModule.GitHubNugets)) { if ($n -and $n.Enabled) { $publishingPlanned = $true; break } } }
    if (-not $publishingPlanned -and $Configuration.Steps.PublishModule.Enabled) { $publishingPlanned = $true }
    if (-not $publishingPlanned -and $Configuration.Steps.PublishModule.GitHub)  { $publishingPlanned = $true }
    if ($publishingPlanned) { $strategy = 'Exact' }

    # Compute assembly/version for build
    $baseVersion = [string]$Configuration.Information.Manifest.ModuleVersion
    if ($strategy -ieq 'AutoRevision') {
        # Next revision across module roots
        $max = -1
        foreach ($r in $Roots) {
            $mr = Join-Path $r $ProjectName
            if (Test-Path -LiteralPath $mr) {
                Get-ChildItem -LiteralPath $mr -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                    $n = $_.Name
                    $re = "^$([Regex]::Escape($baseVersion))(?:\.(\d+))?$"
                    $m = [Regex]::Match($n, $re, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                    if ($m.Success) {
                        if ($m.Groups[1].Success) {
                            $v = 0; [void][int]::TryParse($m.Groups[1].Value, [ref]$v); if ($v -gt $max) { $max = $v }
                        } else {
                            if ($max -lt 0) { $max = 0 }
                        }
                    }
                }
            }
        }
        $resolvedAssemblyVersion = if ($max -ge 0) { "$baseVersion.$($max + 1)" } else { "$baseVersion.1" }
    } else {
        $resolvedAssemblyVersion = $baseVersion
    }

    # Stamp resolved version for the entire build/runtime to ensure unique assembly identity
    $Configuration.Information.Manifest.ModuleVersion = $resolvedAssemblyVersion

    $CmdletsAliases = [ordered] @{}

    $startLibraryBuildingSplat = @{
        RootDirectory        = $FullProjectPath
        Version              = $Configuration.Information.Manifest.ModuleVersion
        ModuleName           = $ProjectName
        LibraryConfiguration = $Configuration.Steps.BuildLibraries
        CmdletsAliases       = $CmdletsAliases
    }

    $Success = Start-LibraryBuilding @startLibraryBuildingSplat
    if ($Success -contains $false) {
        return $false
    }

    # Verify if manifest contains required modules and fix it if nessecary
    $Success = Convert-RequiredModules -Configuration $Configuration
    if ($Success -eq $false) {
        return $false
    }

    if ($Configuration.Steps.BuildModule.Enable -eq $true) {
        $CurrentLocation = (Get-Location).Path

        $Success = Start-PreparingStructure -Configuration $Configuration -FullProjectPath $FullProjectPath -FullTemporaryPath $FullTemporaryPath -FullModuleTemporaryPath $FullModuleTemporaryPath -DestinationPaths $DestinationPaths
        if ($Success -eq $false) {
            return $false
        }

        $Variables = Start-PreparingVariables -Configuration $Configuration -FullProjectPath $FullProjectPath
        if ($Variables -eq $false) {
            return $false
        }

        # lets build variables for later use
        $LinkDirectories = $Variables.LinkDirectories
        $LinkFilesRoot = $Variables.LinkFilesRoot
        $LinkPrivatePublicFiles = $Variables.LinkPrivatePublicFiles
        $DirectoriesWithClasses = $Variables.DirectoriesWithClasses
        $DirectoriesWithPS1 = $Variables.DirectoriesWithPS1
        $Files = $Variables.Files

        $startPreparingFunctionsAndAliasesSplat = @{
            Configuration   = $Configuration
            FullProjectPath = $FullProjectPath
            Files           = $Files
            CmdletsAliases  = $CmdletsAliases
        }

        $AliasesAndFunctions = Start-PreparingFunctionsAndAliases @startPreparingFunctionsAndAliasesSplat
        if ($AliasesAndFunctions -contains $false) {
            return $false
        }

        # Copy Configuration
        # Ensure PowerForge is available before using ManifestWriter
        if (-not ([type]::GetType('PowerForge.ManifestWriter, PowerForge', $false, $false))) {
            Write-Verbose '[ManifestWriter] Not loaded. Loading PowerForge from FullProjectPath\Lib.'
            $libRoot = if ($PSEdition -eq 'Core') { [IO.Path]::Combine($FullProjectPath, 'Lib', 'Core') } else { [IO.Path]::Combine($FullProjectPath, 'Lib', 'Default') }
            $pf = [IO.Path]::Combine($libRoot, 'PowerForge.dll')
            if (-not (Test-Path -LiteralPath $pf -PathType Leaf)) {
                Write-Text "[-] Required assembly not found for manifest generation at '$pf'. Aborting." -Color Red
                return $false
            }
            try { Add-Type -Path $pf -ErrorAction Stop } catch { $msg = $_.Exception.Message; if ($msg -notmatch 'already loaded') { Write-Text "[-] Failed to load '$pf': $msg" -Color Red; return $false } }
            if (-not ([type]::GetType('PowerForge.ManifestWriter, PowerForge', $false, $false))) {
                Write-Text '[-] PowerForge.ManifestWriter still not available after load. Aborting.' -Color Red
                return $false
            }
        }

        # Generate PSD1 manifest (C#) and set PSData basics
        $ModuleVersion = [string]$Configuration.Information.Manifest.ModuleVersion
        $Author        = [string]$Configuration.Information.Manifest.Author
        $CompanyName   = [string]$Configuration.Information.Manifest.CompanyName
        $Description   = [string]$Configuration.Information.Manifest.Description
        $Compat        = if ($Configuration.Information.Manifest.CompatiblePSEditions) { [string[]]$Configuration.Information.Manifest.CompatiblePSEditions } else { @('Desktop','Core') }
        $RootModule    = "$ProjectName.psm1"
        $ScriptsToProcess = @()
        if ($Configuration.UsingInPlace -and -not [string]::IsNullOrWhiteSpace($Configuration.UsingInPlace)) { $ScriptsToProcess += [string]$Configuration.UsingInPlace }
        try {
            [void][PowerForge.ManifestWriter]::Generate($PSD1FilePath, $ProjectName, $ModuleVersion, $Author, $CompanyName, $Description, ([string[]]$Compat), $RootModule, ([string[]]$ScriptsToProcess))
        } catch {
            Write-Text "[-] Manifest generation failed: $($_.Exception.Message)" -Color Red
            return $false
        }
        try {
            if ($Configuration.Information.Manifest.PrivateData.PSData.Tags) { [void][PowerForge.BuildServices]::SetPsDataStringArray($PSD1FilePath, 'Tags', ([string[]]$Configuration.Information.Manifest.PrivateData.PSData.Tags)) }
            if ($Configuration.Information.Manifest.PrivateData.PSData.IconUri) { [void][PowerForge.BuildServices]::SetPsDataString($PSD1FilePath, 'IconUri', [string]$Configuration.Information.Manifest.PrivateData.PSData.IconUri) }
            if ($Configuration.Information.Manifest.PrivateData.PSData.ProjectUri) { [void][PowerForge.BuildServices]::SetPsDataString($PSD1FilePath, 'ProjectUri', [string]$Configuration.Information.Manifest.PrivateData.PSData.ProjectUri) }
            if ($Configuration.Information.Manifest.PrivateData.PSData.ReleaseNotes) { [void][PowerForge.BuildServices]::SetPsDataString($PSD1FilePath, 'ReleaseNotes', [string]$Configuration.Information.Manifest.PrivateData.PSData.ReleaseNotes) }
            if ($Configuration.Information.Manifest.PrivateData.PSData.Prerelease) { [void][PowerForge.BuildServices]::SetPsDataString($PSD1FilePath, 'Prerelease', [string]$Configuration.Information.Manifest.PrivateData.PSData.Prerelease) }
            if ($Configuration.Information.Manifest.PrivateData.PSData.RequireLicenseAcceptance -ne $null) { [void][PowerForge.BuildServices]::SetPsDataBool($PSD1FilePath, 'RequireLicenseAcceptance', [bool]$Configuration.Information.Manifest.PrivateData.PSData.RequireLicenseAcceptance) }
        } catch {
            Write-Text "[-] Writing PSData basics failed: $($_.Exception.Message)" -Color Red
            return $false
        }

        Write-TextWithTime -Text "Verifying created PSD1 is readable" -PreAppend Information {
            if (Test-Path -LiteralPath $PSD1FilePath) {
                try {
                    $null = Import-PowerShellDataFile -Path $PSD1FilePath -ErrorAction Stop
                } catch {
                    Write-Text "[-] PSD1 Reading $PSD1FilePath failed. Error: $($_.Exception.Message)" -Color Red
                    return $false
                }
            } else {
                Write-Text "[-] PSD1 Reading $PSD1FilePath failed. File not created..." -Color Red
                return $false
            }
        } -ColorBefore Yellow -ColorTime Yellow -Color Yellow

        # Ensure PSPublishModule/PowerForge assemblies are loaded (self-build scenario)
        if (-not ([type]::GetType('PowerForge.BuildServices, PowerForge', $false, $false))) {
            Write-Verbose '[BuildServices] Not loaded. Loading PowerForge from FullProjectPath\Lib.'
            $libRoot = if ($PSEdition -eq 'Core') { [IO.Path]::Combine($FullProjectPath, 'Lib', 'Core') } else { [IO.Path]::Combine($FullProjectPath, 'Lib', 'Default') }
            $pf = [IO.Path]::Combine($libRoot, 'PowerForge.dll')
            $pfExists = Test-Path -LiteralPath $pf -PathType Leaf
            if (-not $pfExists) {
                Write-Text "[-] Required assemblies not found in '$libRoot'. Aborting formatting step." -Color Red
                return $false
            }
            try { Add-Type -Path $pf -ErrorAction Stop } catch { Write-Text "[-] Failed to load '$pf': $($_.Exception.Message)" -Color Red; return $false }

            $bs = $null
            foreach ($asm in [AppDomain]::CurrentDomain.GetAssemblies()) {
                $bs = $asm.GetType('PowerForge.BuildServices', $false)
                if ($bs) { break }
            }
            if (-not $bs) { Write-Text '[-] PowerForge.BuildServices still not available after load. Aborting.' -Color Red; return $false }
            Write-Verbose "[BuildServices] Loaded from '$libRoot'"
        }

        # Format PSD1 and PSM1 using C# pipeline (preprocess + PSSA + normalize)
        $SettingsJsonPSD1 = $null
        if ($Configuration.Options.Standard.FormatCodePSD1.FormatterSettings) { try { $SettingsJsonPSD1 = ($Configuration.Options.Standard.FormatCodePSD1.FormatterSettings | ConvertTo-Json -Depth 20 -Compress) } catch { $SettingsJsonPSD1 = $null } }
        $utf8Bom = $true
        [void][PowerForge.BuildServices]::Format(([string[]]@($PSD1FilePath)),
            [bool]$Configuration.Options.Standard.FormatCodePSD1.RemoveCommentsInParamBlock,
            [bool]$Configuration.Options.Standard.FormatCodePSD1.RemoveCommentsBeforeParamBlock,
            [bool]$Configuration.Options.Standard.FormatCodePSD1.RemoveAllEmptyLines,
            [bool]$Configuration.Options.Standard.FormatCodePSD1.RemoveEmptyLines,
            $SettingsJsonPSD1,
            120,
            [PowerForge.LineEnding]::CRLF,
            $utf8Bom)

        $SettingsJsonPSM1 = $null
        if ($Configuration.Options.Standard.FormatCodePSM1.FormatterSettings) { try { $SettingsJsonPSM1 = ($Configuration.Options.Standard.FormatCodePSM1.FormatterSettings | ConvertTo-Json -Depth 20 -Compress) } catch { $SettingsJsonPSM1 = $null } }
        [void][PowerForge.BuildServices]::Format(([string[]]@($PSM1FilePath)),
            [bool]$Configuration.Options.Standard.FormatCodePSM1.RemoveCommentsInParamBlock,
            [bool]$Configuration.Options.Standard.FormatCodePSM1.RemoveCommentsBeforeParamBlock,
            [bool]$Configuration.Options.Standard.FormatCodePSM1.RemoveAllEmptyLines,
            [bool]$Configuration.Options.Standard.FormatCodePSM1.RemoveEmptyLines,
            $SettingsJsonPSM1,
            120,
            [PowerForge.LineEnding]::CRLF,
            $utf8Bom)

        if ($Configuration.Steps.BuildModule.RefreshPSD1Only) {
            return
        }

        $startModuleMergingSplat = @{
            Configuration           = $Configuration
            ProjectName             = $ProjectName
            FullTemporaryPath       = $FullTemporaryPath
            FullModuleTemporaryPath = $FullModuleTemporaryPath
            FullProjectPath         = $FullProjectPath
            LinkDirectories         = $LinkDirectories
            LinkFilesRoot           = $LinkFilesRoot
            LinkPrivatePublicFiles  = $LinkPrivatePublicFiles
            DirectoriesWithPS1      = $DirectoriesWithPS1
            DirectoriesWithClasses  = $DirectoriesWithClasses
            AliasesAndFunction      = $AliasesAndFunctions
            CmdletsAliases          = $CmdletsAliases
        }

        $Success = Start-ModuleMerging @startModuleMergingSplat
        if ($Success -contains $false) {
            return $false
        }

        # Revers Path to current locatikon
        Set-Location -Path $CurrentLocation

        $Success = if ($Configuration.Steps.BuildModule.Enable) {
            # Versioned install using C# (avoids folder-in-use and preserves older versions)
            $Roots = @()
            if ($DestinationPaths.Desktop) { $Roots += [System.IO.Path]::GetDirectoryName($DestinationPaths.Desktop) }
            if ($DestinationPaths.Core) { $Roots += [System.IO.Path]::GetDirectoryName($DestinationPaths.Core) }

            # Allow configuration overrides
            $strategy = [PowerForge.InstallationStrategy]::AutoRevision
            if ($Configuration.Steps.BuildModule.VersionedInstallStrategy) {
                switch -Regex ($Configuration.Steps.BuildModule.VersionedInstallStrategy) {
                    '^Exact$'        { $strategy = [PowerForge.InstallationStrategy]::Exact; break }
                    '^AutoRevision$' { $strategy = [PowerForge.InstallationStrategy]::AutoRevision; break }
                }
            }
            # Auto-switch to Exact when publishing is planned (default: true)
            $autoSwitch = $true
            if ($null -ne $Configuration.Steps.BuildModule.AutoSwitchExactOnPublish) { $autoSwitch = [bool]$Configuration.Steps.BuildModule.AutoSwitchExactOnPublish }
            if ($autoSwitch) {
                $publishingPlanned = $false
                foreach ($n in @($Configuration.Steps.BuildModule.GalleryNugets)) { if ($n -and $n.Enabled) { $publishingPlanned = $true; break } }
                if (-not $publishingPlanned) { foreach ($n in @($Configuration.Steps.BuildModule.GitHubNugets)) { if ($n -and $n.Enabled) { $publishingPlanned = $true; break } } }
                if ($publishingPlanned) { $strategy = [PowerForge.InstallationStrategy]::Exact }
            }
            $keep = 3
            if ($Configuration.Steps.BuildModule.VersionedInstallKeep) { $keep = [int]$Configuration.Steps.BuildModule.VersionedInstallKeep }

            # Resolve module version for install (prefer configuration, then PSD1)
            $moduleVersionForInstall = $Configuration.Information.Manifest.ModuleVersion
            if (-not $moduleVersionForInstall -and (Test-Path -LiteralPath $PSD1FilePath)) {
                try { $moduleVersionForInstall = (Import-PowerShellDataFile -Path $PSD1FilePath -ErrorAction Stop).ModuleVersion } catch { $moduleVersionForInstall = $null }
            }
            if (-not $moduleVersionForInstall) { Write-Text "[-] ModuleVersion is not resolved; cannot proceed with versioned install." -Color Red; return $false }

            # Optional: attempt to kill processes locking module roots (Windows only)
            if ($Configuration.Steps.BuildModule.KillLockersBeforeInstall) {
                try {
                    $locks = [PowerForge.BuildServices]::GetLockingProcesses(([string[]]$Roots))
                    if ($locks.Count -gt 0) {
                        Write-Text "   [i] Locking processes detected: $($locks | ForEach-Object { "PID=$($_.Item1) Name=$($_.Item2)" } -join ', ')" -Color Yellow
                        $killed = [PowerForge.BuildServices]::TerminateLockingProcesses(([string[]]$Roots), [bool]$Configuration.Steps.BuildModule.KillLockersForce)
                        Write-Text "   [i] Terminated $killed locking process(es)." -Color Yellow
                    }
                } catch { Write-Text "   [i] Locker inspection failed: $($_.Exception.Message)" -Color Yellow }
            }

            Write-TextWithTime -Text "Installing module (versioned) into user Modules roots (strategy=$strategy, keep=$keep)" {
                try {
                    $res = [PowerForge.BuildServices]::InstallVersioned(
                        $FullModuleTemporaryPath,
                        $ProjectName,
                        ([string]$moduleVersionForInstall),
                        $strategy,
                        $keep,
                        ([string[]]$Roots),
                        $true
                    )
                    foreach ($p in $res.InstalledPaths) { Write-Text "   [+] Installed: $p" -Color DarkGray }
                    if ($res.PrunedPaths.Count -gt 0) { Write-Text "   [i] Pruned old versions: $($res.PrunedPaths.Count)" -Color Yellow }
                } catch {
                    Write-Text "[-] Versioned install failed: $($_.Exception.Message)" -Color Red
                    return $false
                }
            } -PreAppend Plus
        }
        if ($Success -contains $false) {
            return $false
        }
        $Success = Write-TextWithTime -Text "Building artefacts" -PreAppend Information {
            # Old configuration still supported
            $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -Type 'Releases'
            if ($Success -eq $false) {
                return $false
            }
            # Old configuration still supported
            $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -Type 'ReleasesUnpacked'
            if ($Success -eq $false) {
                return $false
            }
            # new configuration building multiple artefacts
            foreach ($Artefact in  $Configuration.Steps.BuildModule.Artefacts) {
                $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -ChosenArtefact $Artefact
                if ($Success -contains $false) {
                    return $false
                }
            }
        } -ColorBefore Yellow -ColorTime Yellow -Color Yellow
        if ($Success -contains $false) {
            return $false
        }
    }

    # Import Modules Section, useful to check before publishing
    if ($Configuration.Steps.ImportModules) {
        $ImportSuccess = Start-ImportingModules -Configuration $Configuration -ProjectName $ProjectName
        if ($ImportSuccess -contains $false) {
            return $false
        }
    }

    if ($Configuration.Options.TestsAfterMerge) {
        $TestsSuccess = Initialize-InternalTests -Configuration $Configuration -Type 'TestsAfterMerge'
        if ($TestsSuccess -eq $false) {
            return $false
        }
    }

    # Publish Module Section (old configuration)
    if ($Configuration.Steps.PublishModule.Enabled) {
        $Publishing = Start-PublishingGallery -Configuration $Configuration -ModulePath $FullModuleTemporaryPath
        if ($Publishing -eq $false) {
            return $false
        }
    }
    # Publish Module Section to GitHub (old configuration)
    if ($Configuration.Steps.PublishModule.GitHub) {
        $Publishing = Start-PublishingGitHub -Configuration $Configuration -ProjectName $ProjectName
        if ($Publishing -eq $false) {
            return $false
        }
    }

    # new configuration allowing multiple galleries
    foreach ($ChosenNuget in $Configuration.Steps.BuildModule.GalleryNugets) {
        $Success = Start-PublishingGallery -Configuration $Configuration -ChosenNuget $ChosenNuget -ModulePath $FullModuleTemporaryPath
        if ($Success -eq $false) {
            return $false
        }
    }
    # new configuration allowing multiple githubs/releases
    foreach ($ChosenNuget in $Configuration.Steps.BuildModule.GitHubNugets) {
        $Success = Start-PublishingGitHub -Configuration $Configuration -ChosenNuget $ChosenNuget -ProjectName $ProjectName
        if ($Success -eq $false) {
            return $false
        }
    }

    if ($Configuration.Steps.BuildDocumentation) {
        Start-DocumentationBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -ProjectName $ProjectName
    }

    # Cleanup temp directory
    Write-Text "[+] Cleaning up directories created in TEMP directory" -Color Yellow
    $Success = Remove-Directory $FullModuleTemporaryPath
    if ($Success -eq $false) {
        return $false
    }
    $Success = Remove-Directory $FullTemporaryPath
    if ($Success -eq $false) {
        return $false
    }
}
