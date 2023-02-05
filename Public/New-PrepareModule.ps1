function New-PrepareModule {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER Settings
    Parameter description

    .PARAMETER Path
    Path to the folder where new project will be created. If not provided it will be created in one up folder from the location of build script.

    .PARAMETER ModuleName
    Module name to be used for the project.

    .PARAMETER FunctionsToExportFolder
    Parameter description

    .PARAMETER AliasesToExportFolder
    Parameter description

    .PARAMETER Configuration
    Parameter description

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param (
        [Parameter(Position = 0)][scriptblock] $Settings,
        [string] $Path,
        [alias('ProjectName')][string] $ModuleName,
        [string] $FunctionsToExportFolder = 'Public',
        [string] $AliasesToExportFolder = 'Public',
        [System.Collections.IDictionary] $Configuration,
        [string[]] $ExcludeFromPackage = @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'),
        [string[]] $IncludeRoot = @('*.psm1', '*.psd1', 'License*'),
        [string[]] $IncludePS1 = @('Private', 'Public', 'Enums', 'Classes'),
        [string[]] $IncludeAll = @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data'),
        [scriptblock] $IncludeCustomCode,
        [System.Collections.IDictionary] $IncludeToArray,
        [string] $LibrariesCore = 'Lib\Core',
        [string] $LibrariesDefault = 'Lib\Default',
        [string] $LibrariesStandard = 'Lib\Standard'
    )
    # this assumes that the script running this in Build or Publish folder (or any other folder that is 1 level below the root of the project)
    [string] $PathToProject = Get-Item "$($MyInvocation.PSScriptRoot)/.."

    Write-Host "[i] Module Building Initializing..." -ForegroundColor Yellow
    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()
    if ($Configuration) {
        # Lets precreate structure if it's not available
        if (-not $Configuration.Information) {
            $Configuration.Information = [ordered] @{}
        }
        if (-not $Configuration.Information.Manifest) {
            $Configuration.Information.Manifest = [ordered] @{}
        }
        # This deals with OneDrive redirection or similar
        if (-not $Configuration.Information.DirectoryModulesCore) {
            $Configuration.Information.DirectoryModulesCore = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\PowerShell\Modules"
        }
        # This deals with OneDrive redirection or similar
        if (-not $Configuration.Information.DirectoryModules) {
            $Configuration.Information.DirectoryModules = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\WindowsPowerShell\Modules"
        }
        if ($ModuleName) {
            $Configuration.Information.ModuleName = $ModuleName
        }
        if ($ExcludeFromPackage) {
            $Configuration.Information.Exclude = $ExcludeFromPackage
        }
        if ($IncludeRoot) {
            $Configuration.Information.IncludeRoot = $IncludeRoot
        }
        if ($IncludePS1) {
            $Configuration.Information.IncludePS1 = $IncludePS1
        }
        if ($IncludeAll) {
            $Configuration.Information.IncludeAll = $IncludeAll
        }
        if ($IncludeCustomCode) {
            $Configuration.Information.IncludeCustomCode = $IncludeCustomCode
        }
        if ($IncludeToArray) {
            $Configuration.Information.IncludeToArray = $IncludeToArray
        }
        if ($LibrariesCore) {
            $Configuration.Information.LibrariesCore = $LibrariesCore
        }
        if ($LibrariesDefault) {
            $Configuration.Information.LibrariesDefault = $LibrariesDefault
        }
        if ($LibrariesStandard) {
            $Configuration.Information.LibrariesStandard = $LibrariesStandard
        }
        if ($DirectoryProjects) {
            $Configuration.Information.DirectoryProjects = $Path
        }
        if ($FunctionsToExportFolder) {
            $Configuration.Information.FunctionsToExport = $FunctionsToExportFolder
        }
        if ($AliasesToExportFolder) {
            $Configuration.Information.AliasesToExport = $AliasesToExportFolder
        }
        if ($Settings) {
            $ExecutedSettings = & $Settings
            foreach ($Setting in $ExecutedSettings) {
                if ($Setting.Type -eq 'RequiredModule') {
                    if ($Configuration.Information.Manifest.RequiredModules -isnot [System.Collections.Generic.List[System.Object]]) {
                        $Configuration.Information.Manifest.RequiredModules = [System.Collections.Generic.List[System.Object]]::new()
                    }
                    $Configuration.Information.Manifest.RequiredModules.Add($Setting.Configuration)
                } elseif ($Setting.Type -eq 'ExternalModule') {
                    if ($Configuration.Information.Manifest.ExternalModuleDependencies -isnot [System.Collections.Generic.List[System.Object]]) {
                        $Configuration.Information.Manifest.ExternalModuleDependencies = [System.Collections.Generic.List[System.Object]]::new()
                    }
                    $Configuration.Information.Manifest.ExternalModuleDependencies.Add($Setting.Configuration)
                } elseif ($Setting.Type -eq 'Manifest') {
                    foreach ($Key in $Setting.Configuration.Keys) {
                        $Configuration.Information.Manifest[$Key] = $Setting.Configuration[$Key]
                    }
                } elseif ($Setting.Type -eq 'Information') {
                    foreach ($Key in $Setting.Configuration.Keys) {
                        $Configuration.Information[$Key] = $Setting.Configuration[$Key]
                    }
                }
            }
        }

        # We build module or do other stuff with it
        if ($Configuration.Steps.BuildModule.Enable -or
            $Configuration.Steps.BuildModule.EnableDesktop -or
            $Configuration.Steps.BuildModule.EnableCore -or
            $Configuration.Steps.BuildDocumentation -eq $true -or
            $Configuration.Steps.BuildLibraries.Enable -or
            $Configuration.Steps.PublishModule.Enable -or
            $Configuration.Steps.PublishModule.Enabled) {
            $Success = Start-ModuleBuilding -Configuration $Configuration -PathToProject $PathToProject
            if ($Success -eq $false) {
                return
            }
        }
    }
    if ($Path -and $ModuleName) {
        if (-not (Test-Path -Path $Path)) {
            Write-Text "[-] Path $Path doesn't exists. This shouldn't be the case." -Color Red
        } else {
            $FullProjectPath = [io.path]::Combine($Path, $ModuleName)
            $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Publish', 'Enums', 'Data', 'Classes'
            Add-Directory -Directory $FullProjectPath
            foreach ($folder in $Folders) {
                $SubFolder = [io.path]::Combine($FullProjectPath, $Folder)
                Add-Directory -Directory $SubFolder
            }
            Copy-File -Source "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-ModuleStarter.txt" -Destination "$FullProjectPath\$ProjectName.psm1"
        }
    }
    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    Write-Host "[i] Module Building " -NoNewline -ForegroundColor Yellow
    Write-Host "[Time Total: $Execute]" -ForegroundColor Green
}