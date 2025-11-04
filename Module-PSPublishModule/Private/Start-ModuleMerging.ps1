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
        [System.Collections.IDictionary] $AliasesAndFunctions,
        [System.Collections.IDictionary] $CmdletsAliases
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
        # Additional guard: do not stage merge directories or their subpaths (Private/Public/Classes/Enums)
        $LinkDirectoriesWithSupportFiles = $LinkDirectoriesWithSupportFiles | Where-Object {
            $_ -notlike 'Private*' -and $_ -notlike 'Public*' -and $_ -notlike 'Classes*' -and $_ -notlike 'Enums*'
        }
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

        # Copy non-merge PS1 files (e.g., Internals\Scripts\*.ps1) into the final module
        foreach ($supportDir in $LinkDirectoriesWithSupportFiles) {
            $dirTrim = $supportDir.TrimEnd([char]'/',[char]'\')
            if ($dirTrim -notlike 'Internals*') { continue }
            $glob = [System.IO.Path]::Combine($FullProjectPath, $dirTrim, '*.ps1')
            $files = if ($PSEdition -eq 'Core') {
                Get-ChildItem -Path $glob -ErrorAction SilentlyContinue -Recurse -FollowSymlink
            } else {
                Get-ChildItem -Path $glob -ErrorAction SilentlyContinue -Recurse
            }
            foreach ($file in $files) {
                $rel = (Resolve-Path -LiteralPath $file.FullName -Relative)
                if ($null -eq $IsWindows -or $IsWindows -eq $true) { $rel = $rel.Replace('.\\','') } else { $rel = $rel.Replace('./','') }
                $relDir = [System.IO.Path]::GetDirectoryName($rel)
                if ($relDir) { Add-Directory -Directory ([System.IO.Path]::Combine($FullModuleTemporaryPath, $relDir)) }
                Copy-InternalFiles -LinkFiles @($rel) -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath
            }
        }


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
        if ($null -ne $LinkPrivatePublicFiles) {
            $CoreFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithCore) }
            $DefaultFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithDefault) }
            $StandardFiles = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithStandard) }
        } else {
            $CoreFiles = @()
            $DefaultFiles = @()
            $StandardFiles = @()
        }
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

        $mergeModuleSplat = @{
            ModuleName          = $ProjectName
            ModulePathSource    = $FullTemporaryPath
            ModulePathTarget    = $FullModuleTemporaryPath
            Sort                = $Configuration.Options.Merge.Sort
            FunctionsToExport   = $Configuration.Information.Manifest.FunctionsToExport
            AliasesToExport     = $Configuration.Information.Manifest.AliasesToExport
            AliasesAndFunctions = $AliasesAndFunctions
            CmdletsAliases      = $CmdletsAliases
            LibrariesStandard   = $FilesLibrariesStandard
            LibrariesCore       = $FilesLibrariesCore
            LibrariesDefault    = $FilesLibrariesDefault
            FormatCodePSM1      = $Configuration.Options.Merge.FormatCodePSM1
            FormatCodePSD1      = $Configuration.Options.Merge.FormatCodePSD1
            Configuration       = $Configuration
            DirectoriesWithPS1  = $DirectoriesWithPS1
            ClassesPS1          = $DirectoriesWithClasses
            IncludeAsArray      = $Configuration.Information.IncludeAsArray
        }

        $Success = Merge-Module @mergeModuleSplat

        if ($Success -eq $false) {
            return $false
        }

        # If Delivery metadata is enabled, stage root README/CHANGELOG to requested destinations
        if ($Configuration.Options -and $Configuration.Options.Delivery -and $Configuration.Options.Delivery.Enable) {
            $internalsRel = if ($Configuration.Options.Delivery.InternalsPath) { [string]$Configuration.Options.Delivery.InternalsPath } else { 'Internals' }
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $internalsRel = $internalsRel.Replace('/', '\')
            } else {
                $internalsRel = $internalsRel.Replace('\\', '/')
            }
            $destInternals = [System.IO.Path]::Combine($FullModuleTemporaryPath, $internalsRel)
            Add-Directory -Directory $destInternals

            $rootFiles = Get-ChildItem -Path $FullProjectPath -File -ErrorAction SilentlyContinue
            $readmeDest = if ($Configuration.Options.Delivery.ReadmeDestination) { $Configuration.Options.Delivery.ReadmeDestination } else { 'Internals' }
            $chlogDest  = if ($Configuration.Options.Delivery.ChangelogDestination) { $Configuration.Options.Delivery.ChangelogDestination } else { 'Internals' }
            $licDest    = if ($Configuration.Options.Delivery.LicenseDestination) { $Configuration.Options.Delivery.LicenseDestination } else { 'Internals' }

            if ($rootFiles) {
                foreach ($rf in $rootFiles) {
                    if ($rf.Name -like 'README*') {
                        switch ($readmeDest) {
                            'Internals' { Copy-Item -LiteralPath $rf.FullName -Destination $destInternals -Force -ErrorAction SilentlyContinue }
                            'Root'      { Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath $rf.Name) -Force -ErrorAction SilentlyContinue }
                            'Both'      {
                                Copy-Item -LiteralPath $rf.FullName -Destination $destInternals -Force -ErrorAction SilentlyContinue
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath $rf.Name) -Force -ErrorAction SilentlyContinue
                            }
                        }
                    } elseif ($rf.Name -like 'CHANGELOG*') {
                        switch ($chlogDest) {
                            'Internals' { Copy-Item -LiteralPath $rf.FullName -Destination $destInternals -Force -ErrorAction SilentlyContinue }
                            'Root'      { Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath $rf.Name) -Force -ErrorAction SilentlyContinue }
                            'Both'      {
                                Copy-Item -LiteralPath $rf.FullName -Destination $destInternals -Force -ErrorAction SilentlyContinue
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath $rf.Name) -Force -ErrorAction SilentlyContinue
                            }
                        }
                    } elseif ($rf.Name -like 'LICENSE*') {
                        switch ($licDest) {
                            'Internals' {
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $destInternals 'license.txt') -Force -ErrorAction SilentlyContinue
                            }
                            'Root' {
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath 'license.txt') -Force -ErrorAction SilentlyContinue
                            }
                            'Both' {
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $destInternals 'license.txt') -Force -ErrorAction SilentlyContinue
                                Copy-Item -LiteralPath $rf.FullName -Destination (Join-Path $FullModuleTemporaryPath 'license.txt') -Force -ErrorAction SilentlyContinue
                            }
                        }
                    }
                }
                # Enforce root license.txt when RequireLicenseAcceptance is true
                $requireAccept = $false
                if ($Configuration.Information.Manifest.RequireLicenseAcceptance) { $requireAccept = $true }
                if ($requireAccept) {
                    # If no license.txt present at root, but license exists under rootFiles or Internals, ensure root license.txt
                    $existingRootLicense = Join-Path $FullModuleTemporaryPath 'license.txt'
                    if (-not (Test-Path -LiteralPath $existingRootLicense)) {
                        $sourceLic = ($rootFiles | Where-Object { $_.Name -like 'LICENSE*' } | Select-Object -First 1)
                        if (-not $sourceLic -and (Test-Path -LiteralPath $destInternals)) {
                            $cand = Get-ChildItem -LiteralPath $destInternals -Filter 'LICENSE*' -File -ErrorAction SilentlyContinue | Select-Object -First 1
                            if ($cand) { $sourceLic = $cand }
                        }
                        if ($sourceLic) {
                            Copy-Item -LiteralPath $sourceLic.FullName -Destination $existingRootLicense -Force -ErrorAction SilentlyContinue
                        }
                    }
                }
            }
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
