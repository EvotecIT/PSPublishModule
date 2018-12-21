function New-PrepareModule {
    [CmdletBinding()]
    param (
        [string] $ProjectName,
        [string] $ProjectPath,
        [string] $ModulePath,
        [string] $DeleteModulePath,
        [System.Collections.IDictionary] $Configuration
    )
    Begin {

        if ($Configuration) {
            $FullModulePath = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
            $FullModulePathDelete = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
            $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
            $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
            $ProjectName = $Configuration.Information.ModuleName
        } else {
            $FullModulePath = "$modulePath\$projectName"
            $FullProjectPath = "$projectPath\$projectName"
            $FullModulePathDelete = "$DeleteModulePath\$projectName"
            $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $ProjectName
        }
        Write-Verbose '----------------------------------------------------'
        Write-Verbose "Project Name: $ProjectName"
        Write-Verbose "Full module path: $FullModulePath"
        Write-Verbose "Full project path: $FullProjectPath"
        Write-Verbose "Full module path to delete: $FullModulePathDelete"
        Write-Verbose "Full temporary path: $FullTemporaryPath"
        Write-Verbose "PSScriptRoot: $PSScriptRoot"
        Write-Verbose '----------------------------------------------------'

        $CurrentLocation = (Get-Location).Path
        Set-Location -Path $FullProjectPath

        Remove-Directory $FullModulePathDelete
        Remove-Directory $FullModulePath
        Remove-Directory $FullTemporaryPath
        Add-Directory $FullModulePath
        Add-Directory $FullTemporaryPath

        $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates', 'Resources'

        $LinkFiles = @()
        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()
        $LinkFilesSpecial = @()

    }
    Process {


        $Directories = Get-ChildItem -Path $FullProjectPath -Directory -Recurse
        foreach ($directory in $Directories) {
            $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
            $RelativeDirectoryPath = "$RelativeDirectoryPath\"
            foreach ($LookupDir in $DirectoryTypes) {
                #Write-Verbose "New-PrepareModule - RelativeDirectoryPath: $RelativeDirectoryPath LookupDir: $LookupDir\"
                if ($RelativeDirectoryPath -like "$LookupDir\*" ) {
                    $LinkDirectories += Add-ObjectTo -Object $RelativeDirectoryPath -Type 'Directory List'
                }
            }
        }

        $Files = Get-ChildItem -Path $FullProjectPath -File -Recurse
        $AllFiles = @()
        foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $AllFiles += $RelativeFilePath
        }

        $RootFiles = @()
        $Files = Get-ChildItem -Path $FullProjectPath -File
        foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RootFiles += $RelativeFilePath
        }

        $LinkFilesRoot = @()
        # Link only files in Root Directory
        foreach ($File in $RootFiles) {
            switch -Wildcard ($file) {
                '*.psd1' {
                    #Write-Color $File -Color Red
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
                '*.psm1' {
                    # Write-Color $File.FulllName -Color Red
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
                'License*' {
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
            }
        }

        # Link only files from subfolers
        foreach ($file in $AllFiles) {
            switch -Wildcard ($file) {
                "*.dll" {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Lib'
                }
                "*.exe" {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Bin'
                }
                '*.ps1' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Private', 'Public', 'Enums'
                }
                '*license*' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Lib', 'Resources'
                }
                '*.jpg' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Images', 'Resources'
                }
                '*.png' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Images', 'Resources'
                }
                '*.xml' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Templates'
                }
                '*.docx' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Templates'
                }
                '*.js' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Resources'
                }
                '*.css' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Resources'
                }
                '*.rcs' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Resources'
                }
                '*.gif' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Resources'
                }
            }
        }

        if ($Configuration.Information.Manifest) {

            $Functions = Get-FunctionNamesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.FunctionsToExport
            if ($Functions) {
                Write-Verbose "Functions export: $Functions"
                $Configuration.Information.Manifest.FunctionsToExport = $Functions
            }
            $Aliases = Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.AliasesToExport
            if ($Aliases) {
                Write-Verbose "Aliases export: $Aliases"
                $Configuration.Information.Manifest.AliasesToExport = $Aliases
            }

            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.ScriptsToProcess)) {
                $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)\"
                $FilesEnums = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithEnums) }

                if ($FilesEnums.Count -gt 0) {
                    Write-Verbose "ScriptsToProcess export: $FilesEnums"
                    $Configuration.Information.Manifest.ScriptsToProcess = $FilesEnums
                }
            }

            New-PersonalManifest -Configuration $Configuration -ManifestPath $FullProjectPath\$ProjectName.psd1 -AddScriptsToProcess
        }



        if ($Configuration.Options.Merge.Use) {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullTemporaryPath\$Directory"
                Add-Directory $Dir
            }
            # Workaround to link files that are not ps1/psd1
            $LinkDirectoriesWithSupportFiles = $LinkDirectories | Where-Object { $_ -ne 'Public\' -and $_ -ne 'Private\' }
            foreach ($Directory in $LinkDirectoriesWithSupportFiles) {
                $Dir = "$FullModulePath\$Directory"
                Add-Directory $Dir
            }

            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath

            # Workaround to link files that are not ps1/psd1
            $FilesToLink = $LinkPrivatePublicFiles | Where-Object { $_ -notlike '*.ps1' -and $_ -notlike '*.psd1' }
            Set-LinkedFiles -LinkFiles $FilesToLink -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath

            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesCore)) {
                $StartsWithCore = "$($Configuration.Information.LibrariesCore)\"
                $FilesLibrariesCore = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithCore) }
                #$FilesLibrariesCore
            }
            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesDefault)) {
                $StartsWithDefault = "$($Configuration.Information.LibrariesDefault)\"
                $FilesLibrariesDefault = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithDefault) }
                #$FilesLibrariesDefault
            }

            Merge-Module -ModuleName $ProjectName `
                -ModulePathSource $FullTemporaryPath `
                -ModulePathTarget $FullModulePath `
                -Sort $Configuration.Options.Merge.Sort `
                -FunctionsToExport $Configuration.Information.Manifest.FunctionsToExport `
                -AliasesToExport $Configuration.Information.Manifest.AliasesToExport `
                -LibrariesCore $FilesLibrariesCore `
                -LibrariesDefault $FilesLibrariesDefault

        } else {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullModulePath\$Directory"
                Add-Directory $Dir
            }

            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            #Set-LinkedFiles -LinkFiles $LinkFilesSpecial -FullModulePath $PrivateProjectPath -FullProjectPath $AddPrivate -Delete
            #Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        }
        if ($Configuration.Steps.BuildDocumentation) {
            $DocumentationPath = "$FullProjectPath\$($Configuration.Options.Documentation.Path)"
            $ReadMePath = "$FullProjectPath\$($Configuration.Options.Documentation.PathReadme)"
            Write-Verbose "Generating documentation to $DocumentationPath with $ReadMePath"

            if (-not (Test-Path -Path $DocumentationPath)) {
                New-Item -Path "$FullProjectPath\Docs" -ItemType Directory -Force
            }
            $Files = Get-ChildItem -Path $DocumentationPath
            if ($Files.Count -gt 0) {
                Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath #-Verbose
            } else {
                New-MarkdownHelp -Module $ProjectName -WithModulePage -OutputFolder $DocumentationPath #-ModulePagePath $ReadMePath
                Move-Item -Path "$DocumentationPath\$ProjectName.md" -Destination $ReadMePath
            }


        }
        if ($Configuration.Steps.PublishModule) {
            if ($Configuration.Options.PowerShellGallery.FromFile) {
                $ApiKey = Get-Content -Path $Configuration.Options.PowerShellGallery.ApiKey
                New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $ApiKey
            } else {
                New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Options.PowerShellGallery.ApiKey
            }
        }

        if ($Configuration.Publish.Use) {
            New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Publish.ApiKey
        }
    }
    end {
        # Revers Path to current locatikon
        Set-Location -Path $CurrentLocation

        # Import Modules Section
        if ($Configuration) {
            if ($Configuration.Options.ImportModules.RequiredModules) {
                foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                    Import-Module -Name $Module -Force
                }
            }
            if ($Configuration.Options.ImportModules.Self) {
                Import-Module -Name $ProjectName -Force
            }
        }

    }
}