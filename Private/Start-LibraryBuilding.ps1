function Start-LibraryBuilding {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string] $RootDirectory,
        [string] $Version,
        [System.Collections.IDictionary] $LibraryConfiguration,
        [System.Collections.IDictionary] $CmdletsAliases

    )
    if ($LibraryConfiguration.Count -eq 0) {
        return
    }
    if ($LibraryConfiguration.Enable -ne $true) {
        return
    }
    $TranslateFrameworks = [ordered] @{
        'net472'         = 'Default'
        'net48'          = 'Default'
        'net482'         = 'Default'
        'net470'         = 'Default'
        'net471'         = 'Default'
        'net452'         = 'Default'
        'net451'         = 'Default'
        'NetStandard2.0' = 'Standard'
        'netStandard2.1' = 'Standard'
        'netcoreapp2.1'  = 'Core'
        'netcoreapp3.1'  = 'Core'
        'net5.0'         = 'Core'
        'net6.0'         = 'Core'
        'net6.0-windows' = 'Core'
        'net7.0'         = 'Core'
        'net7.0-windows' = 'Core'
        'net8.0'         = 'Core'
    }

    if ($LibraryConfiguration.Configuration) {
        $Configuration = $LibraryConfiguration.Configuration
    } else {
        $Configuration = 'Release'
    }
    if ($LibraryConfiguration.ProjectName) {
        $ModuleName = $LibraryConfiguration.ProjectName
    }
    if ($LibraryConfiguration.NETProjectPath) {
        $ModuleProjectFile = [System.IO.Path]::Combine($LibraryConfiguration.NETProjectPath, "$($LibraryConfiguration.ProjectName).csproj")
        $SourceFolder = [System.IO.Path]::Combine($LibraryConfiguration.NETProjectPath)
    } else {
        $ModuleProjectFile = [System.IO.Path]::Combine($RootDirectory, "Sources", $ModuleName, "$ModuleName.csproj")
        $SourceFolder = [System.IO.Path]::Combine($RootDirectory, "Sources", $ModuleName)
    }
    $ModuleBinFolder = [System.IO.Path]::Combine($RootDirectory, "Lib")
    if (Test-Path -LiteralPath $ModuleBinFolder) {
        $Items = Get-ChildItem -LiteralPath $ModuleBinFolder -Recurse -Force
        $Items | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    }
    $null = New-Item -Path $ModuleBinFolder -ItemType Directory -Force

    try {
        Push-Location -Path $SourceFolder -ErrorAction Stop
    } catch {
        Write-Text "[-] Couldn't switch to folder $SourceFolder. Error: $($_.Exception.Message)" -Color Red
        return $false
    }
    try {
        [xml] $ProjectInformation = Get-Content -Raw -LiteralPath $ModuleProjectFile -Encoding UTF8 -ErrorAction Stop
    } catch {
        Write-Text "[-] Can't read $ModuleProjectFile file. Error: $($_.Exception.Message)" -Color Red
        return $false
    }

    if ($IsLinux) {
        $OSVersion = 'Linux'
    } elseif ($IsMacOS) {
        $OSVersion = 'OSX'
    } else {
        $OSVersion = 'Windows'
    }

    $SupportedFrameworks = foreach ($PropertyGroup in $ProjectInformation.Project.PropertyGroup) {
        if ($PropertyGroup.TargetFrameworks) {
            if ($PropertyGroup.TargetFrameworks -is [array]) {
                foreach ($Target in $PropertyGroup.TargetFrameworks) {
                    if ($Target.Condition -like "*$OSVersion*" -and $Target.'#text') {
                        $Target.'#text'.Trim() -split ";"
                    }
                }
            } else {
                $PropertyGroup.TargetFrameworks -split ";"
            }
        } elseif ($PropertyGroup.TargetFrameworkVersion) {
            throw "TargetFrameworkVersion is not supported. Please use TargetFrameworks/TargetFramework instead which may require different project profile."
        } elseIf ($PropertyGroup.TargetFramework) {
            $PropertyGroup.TargetFramework
        }
    }

    $Count = 0
    foreach ($Framework in $TranslateFrameworks.Keys) {
        if ($SupportedFrameworks.Contains($Framework.ToLower()) -and $LibraryConfiguration.Framework.Contains($Framework.ToLower())) {
            Write-Text "[+] Building $Framework ($Configuration)"
            $null = dotnet publish --configuration $Configuration --verbosity q -nologo -p:Version=$Version --framework $Framework
            if ($LASTEXITCODE) {
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Building $Framework - failed. Error: $LASTEXITCODE" -Color Red
                Exit
            }
        } else {
            continue
        }

        $InitialFolder = [System.IO.Path]::Combine($SourceFolder, "bin", $Configuration, $Framework, "publish")
        $InitialFolder = (Resolve-Path -Path $InitialFolder).Path
        $PublishDirFolder = [System.IO.Path]::Combine($SourceFolder, "bin", $Configuration, $Framework, "publish", "*")
        $ModuleBinFrameworkFolder = [System.IO.Path]::Combine($ModuleBinFolder, $TranslateFrameworks[$Framework])

        $null = New-Item -Path $ModuleBinFrameworkFolder -ItemType Directory -ErrorAction SilentlyContinue

        try {
            $List = Get-ChildItem -Filter "*" -ErrorAction Stop -Path $PublishDirFolder -File -Recurse
            $List = @(
                foreach ($File in $List) {
                    if ($File.Extension -in '.dll', '.so', 'dylib') {
                        $File
                    }
                }
            )
        } catch {
            Write-Text "[-] Can't list files in $PublishDirFolder folder. Error: $($_.Exception.Message)" -Color Red
            return $false
        }
        $Count++
        $Errors = $false

        Write-Text -Text "[i] Preparing copying module files for $ModuleName from $InitialFolder" -Color DarkGray

        :fileLoop foreach ($File in $List) {
            if ($LibraryConfiguration.ExcludeMainLibrary -and $File.Name -eq "$ModuleName.dll") {
                continue
            }
            if ($LibraryConfiguration.ExcludeLibraryFilter) {
                foreach ($Library in $LibraryConfiguration.ExcludeLibraryFilter) {
                    if ($File.Name -like $Library) {
                        continue fileLoop
                    }
                }
            }

            if ($Count -eq 1) {
                if (-not $LibraryConfiguration.BinaryModuleCmdletScanDisabled) {
                    # Skip assembly verification if it doesn't match the current PowerShell edition
                    # We only have to do it once anyways as we expect support for both editions
                    $SkipAssembly = $false
                    if ($PSVersionTable.PSEdition -eq 'Core') {
                        if ($TranslateFrameworks[$Framework] -eq 'Default') {
                            $SkipAssembly = $true
                        }
                    } else {
                        if ($TranslateFrameworks[$Framework] -eq 'Core') {
                            $SkipAssembly = $true
                        }
                    }

                    if (-not $SkipAssembly) {
                        $CmdletsFound = Get-PowerShellAssemblyMetadata -Path $File.FullName
                        if ($CmdletsFound -eq $false) {
                            $Errors = $true
                        } else {
                            if ($CmdletsFound.CmdletsToExport.Count -gt 0 -or $CmdletsFound.AliasesToExport.Count -gt 0) {
                                Write-Text -Text "Found $($CmdletsFound.CmdletsToExport.Count) cmdlets and $($CmdletsFound.AliasesToExport.Count) aliases in $File" -Color Yellow -PreAppend Information -SpacesBefore "   "
                                if ($CmdletsFound.CmdletsToExport.Count -gt 0) {
                                    Write-Text -Text "Cmdlets: $($CmdletsFound.CmdletsToExport -join ', ')" -Color Yellow -PreAppend Plus -SpacesBefore "      "
                                }
                                if ($CmdletsFound.AliasesToExport.Count -gt 0) {
                                    Write-Text -Text "Aliases: $($CmdletsFound.AliasesToExport -join ', ')" -Color Yellow -PreAppend Plus -SpacesBefore "      "
                                }
                                $CmdletsAliases[$File.FullName] = $CmdletsFound
                            }
                        }
                    }
                }
            }
            try {
                if ($LibraryConfiguration.NETDoNotCopyLibrariesRecursively) {
                    Write-Text -Text "   [+] Copying '$($File.FullName)' to folder '$ModuleBinFrameworkFolder'" -Color DarkGray
                    Copy-Item -Path $File.FullName -Destination $ModuleBinFrameworkFolder -ErrorAction Stop
                } else {
                    #     # Calculate the relative path of the file
                    $relativePath = $File.FullName.Substring($InitialFolder.Length + 1)

                    #     # Combine the destination path with the relative path
                    $destinationFilePath = [System.IO.Path]::Combine($ModuleBinFrameworkFolder, $relativePath)

                    #     # Create the directory if it doesn't exist
                    $destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationFilePath)
                    if (-not (Test-Path -Path $destinationDirectory)) {
                        New-Item -ItemType Directory -Path $destinationDirectory -Force
                    }
                    Write-Text -Text "   [+] Copying '$($File.FullName)' as file '$destinationFilePath'" -Color DarkGray
                    # Copy the file to the destination
                    Copy-Item -Path $File.FullName -Destination $destinationFilePath -ErrorAction Stop
                }
            } catch {
                Write-Text "[-] Copying $File to $ModuleBinFrameworkFolder failed. Error: $($_.Exception.Message)" -Color Red
                $Errors = $true
            }
        }
        if ($Errors) {
            return $false
        }

        Write-Text -Text "[i] Preparing copying module files for $ModuleName from $($InitialFolder). Completed!" -Color DarkGray

        # Copying XML files if required
        if ($LibraryConfiguration.NETBinaryModuleDocumenation) {
            $Errors = $false
            try {
                $List = Get-ChildItem -Filter "*.xml" -ErrorAction Stop -Path $PublishDirFolder -File
            } catch {
                Write-Text "[-] Can't list files in $PublishDirFolder folder. Error: $($_.Exception.Message)" -Color Red
                return $false
            }
            :fileLoop foreach ($File in $List) {
                if ($LibraryConfiguration.ExcludeMainLibrary -and $File.Name -eq "$ModuleName.dll") {
                    continue
                }

                $Culture = 'en-US'
                #$TargetPathFolder = [System.IO.Path]::Combine($ModuleBinFrameworkFolder)
                $TargetPathFolder = [System.IO.Path]::Combine($ModuleBinFrameworkFolder, $Culture)
                $TargetPath = [System.IO.Path]::Combine($TargetPathFolder, ($File.Name -replace ".xml", ".dll-Help.xml"))
                if (-not (Test-Path -Path $TargetPathFolder)) {
                    $null = New-Item -Path $TargetPathFolder -ItemType Directory -ErrorAction SilentlyContinue
                }
                try {
                    Copy-Item -Path $File.FullName -Destination $TargetPath -ErrorAction Stop
                } catch {
                    Write-Text "[-] Copying $File to $TargetPath failed. Error: $($_.Exception.Message)" -Color Red
                    $Errors = $true
                }
            }
            if ($Errors) {
                return $false
            }
        }
    }
    Try {
        Pop-Location -ErrorAction Stop
    } catch {
        Write-Text "[-] Couldn't switch back to the root folder. Error: $($_.Exception.Message)" -Color Red
        return $false
    }
}