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
        'NetStandard2.0' = 'Standard'
        'netStandard2.1' = 'Standard'
        'net472'         = 'Default'
        'net48'          = 'Default'
        'net482'         = 'Default'
        'net470'         = 'Default'
        'net471'         = 'Default'
        'net452'         = 'Default'
        'net451'         = 'Default'
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
            dotnet publish --configuration $Configuration --verbosity q -nologo -p:Version=$Version --framework $Framework
            if ($LASTEXITCODE) {
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Building $Framework - failed. Error: $LASTEXITCODE" -Color Red
                Exit
            }
        } else {
            continue
        }

        $PublishDirFolder = [System.IO.Path]::Combine($SourceFolder, "bin", $Configuration, $Framework, "publish", "*")
        $ModuleBinFrameworkFolder = [System.IO.Path]::Combine($ModuleBinFolder, $TranslateFrameworks[$Framework])

        New-Item -Path $ModuleBinFrameworkFolder -ItemType Directory -ErrorAction SilentlyContinue | Out-Null

        try {
            $List = Get-ChildItem -Filter "*.dll" -ErrorAction Stop -Path $PublishDirFolder -File
        } catch {
            Write-Text "[-] Can't list files in $PublishDirFolder folder. Error: $($_.Exception.Message)" -Color Red
            return $false
        }
        $Count++
        $Errors = $false
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
            try {
                Copy-Item -Path $File.FullName -Destination $ModuleBinFrameworkFolder -ErrorAction Stop
            } catch {
                Write-Text "[-] Copying $File to $ModuleBinFrameworkFolder failed. Error: $($_.Exception.Message)" -Color Red
                $Errors = $true
            }
        }
        if ($Errors) {
            return $false
        }
    }
    Try {
        Pop-Location -ErrorAction Stop
    } catch {
        Write-Text "[-] Couldn't switch back to the root folder. Error: $($_.Exception.Message)" -Color Red
        return $false
    }
}