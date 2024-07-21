function Merge-Module {
    [CmdletBinding()]
    param (
        [string] $ModuleName,
        [string] $ModulePathSource,
        [string] $ModulePathTarget,
        [Parameter(Mandatory = $false, ValueFromPipeline = $false)]
        [ValidateSet("ASC", "DESC", "NONE", '')]
        [string] $Sort = 'NONE',
        [string[]] $FunctionsToExport,
        [string[]] $AliasesToExport,
        $AliasesAndFunctions,
        [System.Collections.IDictionary] $CmdletsAliases,
        [Array] $LibrariesStandard,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault,
        [System.Collections.IDictionary] $FormatCodePSM1,
        [System.Collections.IDictionary] $FormatCodePSD1,
        [System.Collections.IDictionary] $Configuration,
        [string[]] $DirectoriesWithPS1,
        [string[]] $ClassesPS1,
        [System.Collections.IDictionary] $IncludeAsArray

    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Merging files into PSM1" -Color Blue

    $PSM1FilePath = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.psm1")
    $PSD1FilePath = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.psd1")

    [Array] $ArrayIncludes = foreach ($VariableName in $IncludeAsArray.Keys) {
        $FilePathVariables = [System.IO.Path]::Combine($ModulePathSource, $IncludeAsArray[$VariableName], "*.ps1")

        [Array] $FilesInternal = if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $FilePathVariables -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $FilePathVariables -ErrorAction SilentlyContinue -Recurse
        }
        "$VariableName = @("
        foreach ($Internal in $FilesInternal) {
            Get-Content -Path $Internal.FullName -Raw -Encoding utf8
        }
        ")"
    }

    # If dot source classes option is enabled we treat classes into separete file, and that means we need to exclude it from standard case
    if ($Configuration.Steps.BuildModule.ClassesDotSource) {
        [Array] $ListDirectoriesPS1 = foreach ($Dir in $DirectoriesWithPS1) {
            if ($Dir -ne $ClassesPS1) {
                $Dir
            }
        }
    } else {
        [Array] $ListDirectoriesPS1 = $DirectoriesWithPS1
    }

    [Array] $ScriptFunctions = foreach ($Directory in $ListDirectoriesPS1) {
        $PathToFiles = [System.IO.Path]::Combine($ModulePathSource, $Directory, "*.ps1")
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $PathToFiles -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $PathToFiles -ErrorAction SilentlyContinue -Recurse
        }
    }
    [Array] $ClassesFunctions = foreach ($Directory in $ClassesPS1) {
        $PathToFiles = [System.IO.Path]::Combine($ModulePathSource, $Directory, "*.ps1")
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $PathToFiles -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $PathToFiles -ErrorAction SilentlyContinue -Recurse
        }
    }
    if ($Sort -eq 'ASC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Property Name
        $ClassesFunctions = $ClassesFunctions | Sort-Object -Property Name
    } elseif ($Sort -eq 'DESC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Descending -Property Name
        $ClassesFunctions = $ClassesFunctions | Sort-Object -Descending -Property Name
    }

    if ($ArrayIncludes.Count -gt 0) {
        $ArrayIncludes | Out-File -Append -LiteralPath $PSM1FilePath -Encoding $Encoding
    }
    $Success = Get-ScriptsContentAndTryReplace -Files $ScriptFunctions -OutputPath $PSM1FilePath -DoNotAttemptToFixRelativePaths:$Configuration.Steps.BuildModule.DoNotAttemptToFixRelativePaths
    if ($Success -eq $false) {
        return $false
    }

    # Using file is needed if there are 'using namespaces' - this is a workaround provided by seeminglyscience
    $FilePathUsing = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.Usings.ps1")

    $UsingInPlace = Format-UsingNamespace -FilePath $PSM1FilePath -FilePathUsing $FilePathUsing
    if ($UsingInPlace) {
        $Success = Format-Code -FilePath $FilePathUsing -FormatCode $FormatCodePSM1
        if ($Success -eq $false) {
            return $false
        }
        $Configuration.UsingInPlace = "$ModuleName.Usings.ps1"
    }

    $TimeToExecute.Stop()
    Write-Text "[+] Merging files into PSM1 [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Detecting required modules" -Color Blue

    $RequiredModules = @(
        if ($Configuration.Information.Manifest.RequiredModules.Count -gt 0) {
            if ($Configuration.Information.Manifest.RequiredModules[0] -is [System.Collections.IDictionary]) {
                $Configuration.Information.Manifest.RequiredModules.ModuleName
            } else {
                $Configuration.Information.Manifest.RequiredModules
            }
        }
        if ($Configuration.Information.Manifest.ExternalModuleDependencies.Count -gt 0) {
            $Configuration.Information.Manifest.ExternalModuleDependencies
        }
    )

    [Array] $DuplicateModules = $RequiredModules | Group-Object | Where-Object { $_.Count -gt 1 } | Select-Object -ExpandProperty Name
    if ($DuplicateModules.Count -gt 0) {
        Write-Text "   [!] Duplicate modules detected in required modules configuration. Please fix your configuration." -Color Red
        foreach ($DuplicateModule in $DuplicateModules) {
            Write-Text "      [>] Duplicate module $DuplicateModule" -Color Red
        }
        return $false
    }

    [Array] $ApprovedModules = $Configuration.Options.Merge.Integrate.ApprovedModules | Sort-Object -Unique

    $ModulesThatWillMissBecauseOfIntegrating = [System.Collections.Generic.List[string]]::new()
    [Array] $DependantRequiredModules = foreach ($Module in $RequiredModules) {
        [Array] $TemporaryDependant = Find-RequiredModules -Name $Module
        if ($TemporaryDependant.Count -gt 0) {
            if ($Module -in $ApprovedModules) {
                # We basically skip dependant modules and tell the user to use it separatly
                # This is because if the module PSSharedGoods has requirements like PSWriteColor
                # and we don't integrate PSWriteColor separatly it would be skipped
                foreach ($ModulesTemp in $TemporaryDependant) {
                    $ModulesThatWillMissBecauseOfIntegrating.Add($ModulesTemp)
                }
            } else {
                $TemporaryDependant
            }
        }
    }
    $DependantRequiredModules = $DependantRequiredModules | Sort-Object -Unique

    $TimeToExecute.Stop()
    Write-Text "[+] Detecting required modules [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Searching for missing functions" -Color Blue

    $MissingFunctions = Get-MissingFunctions -FilePath $PSM1FilePath -SummaryWithCommands -ApprovedModules $ApprovedModules

    $TimeToExecute.Stop()
    Write-Text "[+] Searching for missing functions [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Detecting commands used" -Color Blue

    #[Array] $CommandsWithoutType = $MissingFunctions.Summary | Where-Object { $_.CommandType -eq '' } | Sort-Object -Unique -Property 'Source'
    [Array] $ApplicationsCheck = $MissingFunctions.Summary | Where-Object { $_.CommandType -eq 'Application' } | Sort-Object -Unique -Property 'Source'
    [Array] $ModulesToCheck = $MissingFunctions.Summary | Where-Object { $_.CommandType -ne 'Application' -and $_.CommandType -ne '' } | Sort-Object -Unique -Property 'Source'
    [Array] $CommandsWithoutModule = $MissingFunctions.Summary | Where-Object { $_.CommandType -eq '' } #| Sort-Object -Unique -Property 'Source'

    if ($ApplicationsCheck.Source) {
        Write-Text "[i] Applications used by this module. Make sure those are present on destination system. " -Color Yellow
        foreach ($Application in $ApplicationsCheck.Source) {
            Write-Text "   [>] Application $Application " -Color Yellow
        }
    }
    $TimeToExecute.Stop()
    Write-Text "[+] Detecting commands used [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    # Analyze required, approved modules
    $approveRequiredModulesSplat = @{
        ApprovedModules          = $ApprovedModules
        ModulesToCheck           = $ModulesToCheck
        RequiredModules          = $RequiredModules
        DependantRequiredModules = $DependantRequiredModules
        MissingFunctions         = $MissingFunctions
        Configuration            = $Configuration
        CommandsWithoutModule    = $CommandsWithoutModule
    }

    $Success = Approve-RequiredModules @approveRequiredModulesSplat
    if ($Success -eq $false) {
        return $false
    }

    if ($Configuration.Steps.BuildModule.MergeMissing -eq $true) {
        if (Test-Path -LiteralPath $PSM1FilePath) {
            $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
            Write-Text "[+] Merge mergable commands" -Color Blue

            $PSM1Content = Get-Content -LiteralPath $PSM1FilePath -Raw -Encoding $Encoding
            $IntegrateContent = @(
                $MissingFunctions.Functions
                $PSM1Content
            )
            $IntegrateContent | Set-Content -LiteralPath $PSM1FilePath -Encoding $Encoding

            # Overwrite Required Modules
            $NewRequiredModules = foreach ($_ in $Configuration.Information.Manifest.RequiredModules) {
                if ($_ -is [System.Collections.IDictionary]) {
                    if ($_.ModuleName -notin $ApprovedModules) {
                        $_
                    }
                } else {
                    if ($_ -notin $ApprovedModules) {
                        $_
                    }
                }
            }
            $Configuration.Information.Manifest.RequiredModules = $NewRequiredModules
            $TimeToExecute.Stop()
            Write-Text "[+] Merge mergable commands [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue
        }
    }


    $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Finalizing PSM1/PSD1" -Color Blue

    # lets set the defaults to disabled value
    if ($null -eq $Configuration.Steps.BuildModule.DebugDLL) {
        $Configuration.Steps.BuildModule.DebugDLL = $false
    }


    if ($Configuration.Steps.BuildLibraries.NETLineByLineAddType) {
        $DoNotOptimizeLoading = $Configuration.Steps.BuildLibraries.NETLineByLineAddType
    } else {
        $DoNotOptimizeLoading = $false
    }

    [Array] $LibraryContent = New-LibraryContent -Configuration $Configuration -LibrariesStandard $LibrariesStandard -LibrariesCore $LibrariesCore -LibrariesDefault $LibrariesDefault -OptimizedLoading:(-not $DoNotOptimizeLoading)

    # Add libraries (DLL) into separate file and either dot source it or load as script processing in PSD1 or both (for whatever reason)
    if ($LibraryContent.Count -gt 0) {
        if ($Configuration.Steps.BuildModule.LibrarySeparateFile -eq $true) {
            $LibariesPath = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.Libraries.ps1")
            $ScriptsToProcessLibrary = "$ModuleName.Libraries.ps1"
        }
        if ($Configuration.Steps.BuildModule.LibraryDotSource -eq $true) {
            $LibariesPath = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.Libraries.ps1")
            $DotSourcePath = ". `$PSScriptRoot\$ModuleName.Libraries.ps1"
        }
        if ($LibariesPath) {
            $LibraryContent | Out-File -Append -LiteralPath $LibariesPath -Encoding $Encoding
        }
    }


    if ($ClassesFunctions.Count -gt 0) {
        $ClassesPath = [System.IO.Path]::Combine($ModulePathTarget, "$ModuleName.Classes.ps1")
        $DotSourceClassPath = ". `$PSScriptRoot\$ModuleName.Classes.ps1"
        $Success = Get-ScriptsContentAndTryReplace -Files $ClassesFunctions -OutputPath $ClassesPath -DoNotAttemptToFixRelativePaths:$Configuration.Steps.BuildModule.DoNotAttemptToFixRelativePaths
        if ($Success -eq $false) {
            return $false
        }
    }

    # Adjust PSM1 file by adding dot sourcing or directly libraries to the PSM1 file
    if ($LibariesPath -gt 0 -or $ClassesPath -gt 0 -or $Configuration.Steps.BuildModule.ResolveBinaryConflicts) {
        if (Test-Path -LiteralPath $PSM1FilePath) {
            $PSM1Content = Get-Content -LiteralPath $PSM1FilePath -Raw -Encoding UTF8
        } else {
            Write-Text "[+] PSM1 file doesn't exists. Creating empty content" -Color Blue
            $PSM1Content = ''
        }
        $IntegrateContent = @(
            # add resolve conflicting binary option
            if ($Configuration.Steps.BuildModule.ResolveBinaryConflicts -is [System.Collections.IDictionary]) {
                New-DLLResolveConflict -ProjectName $Configuration.Steps.BuildModule.ResolveBinaryConflicts.ProjectName -LibraryConfiguration $Configuration.Steps.BuildLibraries
            } elseif ($Configuration.Steps.BuildModule.ResolveBinaryConflicts -eq $true) {
                New-DLLResolveConflict -LibraryConfiguration $Configuration.Steps.BuildLibraries
            }

            Add-BinaryImportModule -Configuration $Configuration -LibrariesStandard $LibrariesStandard -LibrariesCore $LibrariesCore -LibrariesDefault $LibrariesDefault

            if ($LibraryContent.Count -gt 0) {
                if ($DotSourcePath) {
                    "# Dot source all libraries by loading external file"
                    $DotSourcePath
                    ""
                }
                if (-not $LibariesPath) {
                    "# Load all types"
                    $LibraryContent
                    ""
                }
            }
            if ($ClassesPath) {
                "# Dot source all classes by loading external file"
                $DotSourceClassPath
                ""
            }
            $PSM1Content
        )
        $IntegrateContent | Set-Content -LiteralPath $PSM1FilePath -Encoding $Encoding
    }

    if ($Configuration.Information.Manifest.DotNetFrameworkVersion) {
        Find-NetFramework -RequireVersion $Configuration.Information.Manifest.DotNetFrameworkVersion | Out-File -Append -LiteralPath $PSM1FilePath -Encoding $Encoding
    }

    # Finalize PSM1 by adding export functions/aliases and internal modules loading
    $newPSMFileSplat = @{
        Path                       = $PSM1FilePath
        FunctionNames              = $FunctionsToExport
        FunctionAliaes             = $AliasesToExport
        AliasesAndFunctions        = $AliasesAndFunctions
        CmdletsAliases             = $CmdletsAliases
        LibrariesStandard          = $LibrariesStandard
        LibrariesCore              = $LibrariesCore
        LibrariesDefault           = $LibrariesDefault
        ModuleName                 = $ModuleName
        #UsingNamespaces            = $UsingInPlace
        LibariesPath               = $LibariesPath
        InternalModuleDependencies = $Configuration.Information.Manifest.InternalModuleDependencies
        CommandModuleDependencies  = $Configuration.Information.Manifest.CommandModuleDependencies
        # we need to inform PSM1 we have binary module (at least partially)
        # this will let us export cmdlet *
        BinaryModule               = $Configuration.Steps.BuildLibraries.BinaryModule
        Configuration              = $Configuration
    }

    $Success = New-PSMFile @newPSMFileSplat
    if ($Success -eq $false) {
        return $false
    }

    # Format standard PSM1 file
    $Success = Format-Code -FilePath $PSM1FilePath -FormatCode $FormatCodePSM1
    if ($Success -eq $false) {
        return $false
    }
    # Format libraries PS1 file
    if ($LibariesPath) {
        $Success = Format-Code -FilePath $LibariesPath -FormatCode $FormatCodePSM1
        if ($Success -eq $false) {
            return $false
        }
    }
    # Build PSD1 file
    $newPersonalManifestSplat = @{
        Configuration           = $Configuration
        ManifestPath            = $PSD1FilePath
        AddUsingsToProcess      = $true
        ScriptsToProcessLibrary = $ScriptsToProcessLibrary
        OnMerge                 = $true
    }

    if ($Configuration.Steps.BuildLibraries.BinaryModule) {
        $newPersonalManifestSplat.BinaryModule = $Configuration.Steps.BuildLibraries.BinaryModule
    }

    New-PersonalManifest @newPersonalManifestSplat
    # Format PSD1 file
    $Success = Format-Code -FilePath $PSD1FilePath -FormatCode $FormatCodePSD1
    if ($Success -eq $false) {
        return $false
    }
    # cleans up empty directories
    Get-ChildItem $ModulePathTarget -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
        Remove-Item #-Verbose

    $TimeToExecuteSign.Stop()
    Write-Text "[+] Finalizing PSM1/PSD1 [Time: $($($TimeToExecuteSign.Elapsed).Tostring())]" -Color Blue
}