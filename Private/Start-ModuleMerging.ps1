function Start-ModuleMerging {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ProjectName,
        [string] $FullTemporaryPath,
        [string] $FullModuleTemporaryPath,
        [string] $FullProjectPath,
        [Array] $LinkDirectories,
        [Array] $LinkFilesRoot,
        [Array] $LinkPrivatePublicFiles,
        [string[]] $DirectoriesWithPS1,
        [string[]] $DirectoriesWithClasses,
        [System.Collections.IDictionary] $AliasesAndFunctions
    )
    if ($Configuration.Steps.BuildModule.Merge) {
        foreach ($Directory in $LinkDirectories) {
            $Dir = [System.IO.Path]::Combine($FullTemporaryPath, "$Directory")
            Add-Directory -Directory $Dir
        }
        # Workaround to link files that are not ps1/psd1
        [Array] $CompareWorkaround = foreach ($Directory in $DirectoriesWithPS1) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $Dir = -join ($Directory, "\")
            } else {
                $Dir = -join ($Directory, "/")
            }
        }

        $LinkDirectoriesWithSupportFiles = $LinkDirectories | Where-Object { $_ -notin $CompareWorkaround }
        foreach ($Directory in $LinkDirectoriesWithSupportFiles) {
            $Dir = [System.IO.Path]::Combine($FullModuleTemporaryPath, "$Directory")
            Add-Directory -Directory $Dir
        }

        $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
        Copy-InternalFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
        Copy-InternalFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
        Write-Text -End -Time $LinkingFilesTime

        # Workaround to link files that are not ps1/psd1
        $FilesToLink = $LinkPrivatePublicFiles | Where-Object { $_ -notlike '*.ps1' -and $_ -notlike '*.psd1' }
        Copy-InternalFiles -LinkFiles $FilesToLink -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath

        if ($Configuration.Information.LibrariesStandard) {
            # User provided option, we don't care
        } elseif ($Configuration.Information.LibrariesCore -and $Configuration.Information.LibrariesDefault) {
            # User provided option for core and default we don't care
        } else {
            # user hasn't provided any option, we set it to default
            $Configuration.Information.LibrariesStandard = [System.IO.Path]::Combine("Lib", "Standard")
            $Configuration.Information.LibrariesCore = [System.IO.Path]::Combine("Lib", "Core")
            $Configuration.Information.LibrariesDefault = [System.IO.Path]::Combine("Lib", "Default")
        }

        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesCore)) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $StartsWithCore = -join ($Configuration.Information.LibrariesCore, "\")
            } else {
                $StartsWithCore = -join ($Configuration.Information.LibrariesCore, "/")
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesDefault)) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $StartsWithDefault = -join ($Configuration.Information.LibrariesDefault, "\")
            } else {
                $StartsWithDefault = -join ($Configuration.Information.LibrariesDefault, "/")
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesStandard)) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $StartsWithStandard = -join ($Configuration.Information.LibrariesStandard, "\")
            } else {
                $StartsWithStandard = -join ($Configuration.Information.LibrariesStandard, "/")
            }
        }

        $CoreFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithCore) }
        $DefaultFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithDefault) }
        $StandardFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithStandard) }

        $Default = $false
        $Core = $false
        $Standard = $false
        if ($CoreFiles.Count -gt 0) {
            $Core = $true
        }
        if ($DefaultFiles.Count -gt 0) {
            $Default = $true
        }
        if ($StandardFiles.Count -gt 0) {
            $Standard = $true
        }
        if ($Standard -and $Core -and $Default) {
            $FrameworkNet = 'Default'
            $Framework = 'Standard'
        } elseif ($Standard -and $Core) {
            $Framework = 'Standard'
            $FrameworkNet = 'Standard'
        } elseif ($Core -and $Default) {
            $Framework = 'Core'
            $FrameworkNet = 'Default'
        } elseif ($Standard -and $Default) {
            $Framework = 'Standard'
            $FrameworkNet = 'Default'
        } elseif ($Standard) {
            $Framework = 'Standard'
            $FrameworkNet = 'Standard'
        } elseif ($Core) {
            $Framework = 'Core'
            $FrameworkNet = ''
        } elseif ($Default) {
            $Framework = ''
            $FrameworkNet = 'Default'
        }

        if ($Framework -eq 'Core') {
            $FilesLibrariesCore = $CoreFiles
        } elseif ($Framework -eq 'Standard') {
            $FilesLibrariesCore = $StandardFiles
        }
        if ($FrameworkNet -eq 'Default') {
            $FilesLibrariesDefault = $DefaultFiles
        } elseif ($FrameworkNet -eq 'Standard') {
            $FilesLibrariesDefault = $StandardFiles
        }
        if ($FrameworkNet -eq 'Standard' -and $Framework -eq 'Standard') {
            $FilesLibrariesStandard = $FilesLibrariesCore
        }
        $Success = Merge-Module -ModuleName $ProjectName `
            -ModulePathSource $FullTemporaryPath `
            -ModulePathTarget $FullModuleTemporaryPath `
            -Sort $Configuration.Options.Merge.Sort `
            -FunctionsToExport $Configuration.Information.Manifest.FunctionsToExport `
            -AliasesToExport $Configuration.Information.Manifest.AliasesToExport `
            -AliasesAndFunctions $AliasesAndFunctions `
            -LibrariesStandard $FilesLibrariesStandard `
            -LibrariesCore $FilesLibrariesCore `
            -LibrariesDefault $FilesLibrariesDefault `
            -FormatCodePSM1 $Configuration.Options.Merge.FormatCodePSM1 `
            -FormatCodePSD1 $Configuration.Options.Merge.FormatCodePSD1 `
            -Configuration $Configuration -DirectoriesWithPS1 $DirectoriesWithPS1 `
            -ClassesPS1 $DirectoriesWithClasses -IncludeAsArray $Configuration.Information.IncludeAsArray

        if ($Success -eq $false) {
            return $false
        }

        if ($Configuration.Steps.BuildModule.CreateFileCatalog) {
            # Something is wrong here for folders other than root, need investigation
            $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
            Write-Text "[+] Creating file catalog" -Color Blue
            $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
            $CategoryPaths = @(
                $FullModuleTemporaryPath
                $NotEmptyPaths = (Get-ChildItem -Directory -Path $FullModuleTemporaryPath -Recurse).FullName
                if ($NotEmptyPaths) {
                    $NotEmptyPaths
                }
            )
            foreach ($CatPath in $CategoryPaths) {
                $CatalogFile = [io.path]::Combine($CatPath, "$ProjectName.cat")
                $FileCreated = New-FileCatalog -Path $CatPath -CatalogFilePath $CatalogFile -CatalogVersion 2.0
                if ($FileCreated) {
                    Write-Text "   [>] Catalog file covering $CatPath was created $($FileCreated.Name)" -Color Yellow
                }
            }
            $TimeToExecuteSign.Stop()
            Write-Text "[+] Creating file catalog [Time: $($($TimeToExecuteSign.Elapsed).Tostring())]" -Color Blue
        }
        $SuccessFullSigning = Start-ModuleSigning -Configuration $Configuration -FullModuleTemporaryPath $FullModuleTemporaryPath
        if ($SuccessFullSigning -eq $false) {
            return $false
        }
    } else {
        foreach ($Directory in $LinkDirectories) {
            $Dir = [System.IO.Path]::Combine($FullModuleTemporaryPath, "$Directory")
            Add-Directory -Directory $Dir
        }
        $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
        Copy-InternalFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath
        Copy-InternalFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath
        Write-Text -End -Time $LinkingFilesTime
    }
}