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
        [System.Collections.IDictionary] $AliasesAndFunctions,
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
    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] Merging" -Color Blue

    $PSM1FilePath = "$ModulePathTarget\$ModuleName.psm1"
    $PSD1FilePath = "$ModulePathTarget\$ModuleName.psd1"

    # [Array] $ClassesFunctions = foreach ($Directory in $DirectoriesWithPS1) {
    #     if ($PSEdition -eq 'Core') {
    #         Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse -FollowSymlink
    #     } else {
    #         Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse
    #     }
    # }

    [Array] $ArrayIncludes = foreach ($VariableName in $IncludeAsArray.Keys) {
        $FilePathVariables = [System.IO.Path]::Combine($ModulePathSource, $IncludeAsArray[$VariableName], "*.ps1")

        [Array] $FilesInternal = if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $FilePathVariables -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $FilePathVariables -ErrorAction SilentlyContinue -Recurse
        }
        "$VariableName = @("
        foreach ($Internal in $FilesInternal) {
            Get-Content -Path $Internal.FullName -Raw
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
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse
        }
    }
    [Array] $ClassesFunctions = foreach ($Directory in $ClassesPS1) {
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $ModulePathSource\$Directory\*.ps1 -ErrorAction SilentlyContinue -Recurse
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
        $ArrayIncludes | Out-File -Append -LiteralPath $PSM1FilePath -Encoding utf8
    }
    $Success = Get-ScriptsContent -Files $ScriptFunctions -OutputPath $PSM1FilePath
    if ($Success -eq $false) {
        return $false
    }

    # Using file is needed if there are 'using namespaces' - this is a workaround provided by seeminglyscience
    $FilePathUsing = "$ModulePathTarget\$ModuleName.Usings.ps1"

    $UsingInPlace = Format-UsingNamespace -FilePath $PSM1FilePath -FilePathUsing $FilePathUsing
    if ($UsingInPlace) {
        $Success = Format-Code -FilePath $FilePathUsing -FormatCode $FormatCodePSM1
        if ($Success -eq $false) {
            return $false
        }
        $Configuration.UsingInPlace = "$ModuleName.Usings.ps1"
    }

    $TimeToExecute.Stop()
    Write-Text "[+] Merging [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

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
    [Array] $ApprovedModules = $Configuration.Options.Merge.Integrate.ApprovedModules | Sort-Object -Unique

    $ModulesThatWillMissBecauseOfIntegrating = [System.Collections.Generic.List[string]]::new()
    [Array] $DependantRequiredModules = foreach ($_ in $RequiredModules) {
        [Array] $TemporaryDependant = Find-RequiredModules -Name $_
        if ($_ -in $ApprovedModules) {
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
    [Array] $CommandsWithoutModule = $MissingFunctions.Summary | Where-Object { $_.CommandType -eq '' } | Sort-Object -Unique -Property 'Source'

    if ($ApplicationsCheck.Source) {
        Write-Text "[i] Applications used by this module. Make sure those are present on destination system. " -Color Yellow
        foreach ($Application in $ApplicationsCheck.Source) {
            Write-Text "   [>] Application $Application " -Color Yellow
        }
    }
    Write-Text "[+] Pre-Verification of Approved Modules" -Color DarkYellow
    foreach ($ApprovedModule in $ApprovedModules) {
        $ApprovedModuleStatus = Get-Module -Name $ApprovedModule -ListAvailable
        if ($ApprovedModuleStatus) {
            Write-Text "   [>] Approved module $ApprovedModule exists - can be used for merging." -Color Green
        } else {
            Write-Text "   [>] Approved module $ApprovedModule doesn't exists. Potentially issue with merging." -Color Red
        }
    }
    foreach ($Module in $ModulesToCheck.Source) {
        if ($Module -in $RequiredModules -and $Module -in $ApprovedModules) {
            Write-Text "[+] Module $Module is in required modules with ability to merge." -Color DarkYellow
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                if ($F.IsPrivate) {
                    Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsPrivate: $($F.IsPrivate))" -Color Magenta
                } else {
                    Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsPrivate: $($F.IsPrivate))" -Color DarkYellow
                }
            }
        } elseif ($Module -in $DependantRequiredModules -and $Module -in $ApprovedModules) {
            Write-Text "[+] Module $Module is in dependant required module within required modules with ability to merge." -Color DarkYellow
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color DarkYellow
            }
        } elseif ($Module -in $DependantRequiredModules) {
            Write-Text "[+] Module $Module is in dependant required module within required modules." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Green
            }
        } elseif ($Module -in $RequiredModules) {
            Write-Text "[+] Module $Module is in required modules." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Green
            }
        } elseif ($Module -notin $RequiredModules -and $Module -in $ApprovedModules) {
            Write-Text "[+] Module $Module is missing in required module, but it's in approved modules." -Color Magenta
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Magenta
            }
        } else {
            Write-Text "[-] Module $Module is missing in required modules. Potential issue." -Color Red
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }) #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command affected $($F.Name) (Command Type: $($F.CommandType) / IsAlias: $($F.IsAlias)) / IsAlias: $($F.IsPrivate))" -Color Red
            }
        }
    }
    if ($CommandsWithoutModule.Count -gt 0) {
        Write-Text "[-] Some commands couldn't be resolved to functions (private function maybe?). Potential issue." -Color Red
        foreach ($F in $CommandsWithoutModule) {
            Write-Text "   [>] Command affected $($F.Name) (Command Type: Unknown / IsAlias: $($F.IsAlias))" -Color Red
        }
    }
    foreach ($Module in $ModulesThatWillMissBecauseOfIntegrating) {
        #Write-Text "[-] Module $Module is missing in required modules due to integration of some approved module. Potential issue." -Color Red
    }

    $TimeToExecute.Stop()
    Write-Text "[+] Detecting commands used [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    if ($Configuration.Steps.BuildModule.MergeMissing -eq $true) {
        if (Test-Path -LiteralPath $PSM1FilePath) {
            $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
            Write-Text "[+] Merge mergable commands" -Color Blue


            $PSM1Content = Get-Content -LiteralPath $PSM1FilePath -Raw
            $IntegrateContent = @(
                $MissingFunctions.Functions
                $PSM1Content
            )
            $IntegrateContent | Set-Content -LiteralPath $PSM1FilePath -Encoding UTF8

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
    $LibraryContent = @(
        if ($LibrariesStandard.Count -gt 0) {
            foreach ($File in $LibrariesStandard) {
                $Extension = $File.Substring($File.Length - 4, 4)
                if ($Extension -eq '.dll') {
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                    $Output
                }
            }
        } elseif ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {
            'if ($PSEdition -eq ''Core'') {'
            if ($LibrariesCore.Count -gt 0) {
                foreach ($File in $LibrariesCore) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
            '} else {'
            if ($LibrariesDefault.Count -gt 0) {
                foreach ($File in $LibrariesDefault) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
            '}'
        }
    )
    # Add libraries (DLL) into separate file and either dot source it or load as script processing in PSD1 or both (for whatever reason)
    if ($LibraryContent.Count -gt 0) {
        if ($Configuration.Steps.BuildModule.LibrarySeparateFile -eq $true) {
            $LibariesPath = "$ModulePathTarget\$ModuleName.Libraries.ps1"
            $ScriptsToProcessLibrary = "$ModuleName.Libraries.ps1"
        }
        if ($Configuration.Steps.BuildModule.LibraryDotSource -eq $true) {
            $LibariesPath = "$ModulePathTarget\$ModuleName.Libraries.ps1"
            $DotSourcePath = ". `$PSScriptRoot\$ModuleName.Libraries.ps1"
        }
        if ($LibariesPath) {
            $LibraryContent | Add-Content -Path $LibariesPath
        }
    }


    if ($ClassesFunctions.Count -gt 0) {
        $ClassesPath = "$ModulePathTarget\$ModuleName.Classes.ps1"
        $DotSourceClassPath = ". `$PSScriptRoot\$ModuleName.Classes.ps1"
        $Success = Get-ScriptsContent -Files $ClassesFunctions -OutputPath $ClassesPath
        if ($Success -eq $false) {
            return $false
        }
    }

    # Adjust PSM1 file by adding dot sourcing or directly libraries to the PSM1 file
    if ($LibariesPath -gt 0 -or $ClassesPath -gt 0 -or $Configuration.Steps.BuildModule.ResolveBinaryConflicts) {
        $PSM1Content = Get-Content -LiteralPath $PSM1FilePath -Raw
        $IntegrateContent = @(
            # add resolve conflicting binary option
            if ($Configuration.Steps.BuildModule.ResolveBinaryConflicts -is [System.Collections.IDictionary]) {
                New-DLLResolveConflict -ProjectName $Configuration.Steps.BuildModule.ResolveBinaryConflicts.ProjectName
            } elseif ($Configuration.Steps.BuildModule.ResolveBinaryConflicts -eq $true) {
                New-DLLResolveConflict
            }
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
        $IntegrateContent | Set-Content -LiteralPath $PSM1FilePath -Encoding UTF8
    }

    if ($Configuration.Information.Manifest.DotNetFrameworkVersion) {
        Find-NetFramework -RequireVersion $Configuration.Information.Manifest.DotNetFrameworkVersion | Add-Content -LiteralPath $PSM1FilePath -Encoding UTF8
    }

    # Finalize PSM1 by adding export functions/aliases and internal modules loading
    New-PSMFile -Path $PSM1FilePath `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -AliasesAndFunctions $AliasesAndFunctions `
        -LibrariesStandard $LibrariesStandard `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault `
        -ModuleName $ModuleName `
        -UsingNamespaces:$UsingInPlace `
        -LibariesPath $LibariesPath `
        -InternalModuleDependencies $Configuration.Information.Manifest.InternalModuleDependencies `
        -CommandModuleDependencies $Configuration.Information.Manifest.CommandModuleDependencies

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
    New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath -AddUsingsToProcess -ScriptsToProcessLibrary $ScriptsToProcessLibrary -OnMerge
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