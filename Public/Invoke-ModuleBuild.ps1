function Invoke-ModuleBuild {
    <#
    .SYNOPSIS
    Command to create new module or update existing one.
    It will create new module structure and everything around it, or update existing one.

    .DESCRIPTION
    Command to create new module or update existing one.
    It will create new module structure and everything around it, or update existing one.

    .PARAMETER Settings
    Provide settings for the module in form of scriptblock.
    It's using DSL to define settings for the module.

    .PARAMETER Path
    Path to the folder where new project will be created, or existing project will be updated.
    If not provided it will be created in one up folder from the location of build script.

    .PARAMETER ModuleName
    Provide name of the module. It's required parameter.

    .PARAMETER FunctionsToExportFolder
    Public functions folder name. Default is 'Public'.
    It will be used as part of PSD1 and PSM1 to export only functions from this folder.

    .PARAMETER AliasesToExportFolder
    Public aliases folder name. Default is 'Public'.
    It will be used as part of PSD1 and PSM1 to export only aliases from this folder.

    .PARAMETER Configuration
    Provides a way to configure module using hashtable.
    It's the old way of configuring module, that requires knowledge of inner workings of the module to name proper key/value pairs
    It's required for compatibility with older versions of the module.

    .PARAMETER ExcludeFromPackage
    Exclude files from Artefacts. Default is '.*, 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'.

    .PARAMETER IncludeRoot
    Include files in the Artefacts from root of the project. Default is '*.psm1', '*.psd1', 'License*' files.
    Other files will be ignored.

    .PARAMETER IncludePS1
    Include *.ps1 files in the Artefacts from given folders. Default are 'Private', 'Public', 'Enums', 'Classes' folders.
    If the folder doesn't exists it will be ignored.

    .PARAMETER IncludeAll
    Include all files in the Artefacts from given folders. Default are 'Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data' folders.

    .PARAMETER IncludeCustomCode
    Parameter description

    .PARAMETER IncludeToArray
    Parameter description

    .PARAMETER LibrariesCore
    Parameter description

    .PARAMETER LibrariesDefault
    Parameter description

    .PARAMETER LibrariesStandard
    Parameter description

    .PARAMETER ExitCode
    Exit code to be returned to the caller. If not provided, it will not exit the script.
    Exit code 0 means success, 1 means failure.

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [alias('New-PrepareModule', 'Build-Module')]
    [CmdletBinding()]
    param (
        [Parameter(Position = 0)][scriptblock] $Settings,
        [string] $Path,
        [parameter(Mandatory)][alias('ProjectName')][string] $ModuleName,
        [string] $FunctionsToExportFolder = 'Public',
        [string] $AliasesToExportFolder = 'Public',
        [System.Collections.IDictionary] $Configuration = [ordered] @{},
        [string[]] $ExcludeFromPackage = @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'),
        [string[]] $IncludeRoot = @('*.psm1', '*.psd1', 'License*'),
        [string[]] $IncludePS1 = @('Private', 'Public', 'Enums', 'Classes'),
        [string[]] $IncludeAll = @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data'),
        [scriptblock] $IncludeCustomCode,
        [System.Collections.IDictionary] $IncludeToArray,
        [string] $LibrariesCore = 'Lib\Core',
        [string] $LibrariesDefault = 'Lib\Default',
        [string] $LibrariesStandard = 'Lib\Standard',
        [switch] $ExitCode
    )
    # this assumes that the script running this in Build or Publish folder (or any other folder that is 1 level below the root of the project)
    [string] $PathToProject = Get-Item -LiteralPath "$($MyInvocation.PSScriptRoot)/.."

    Write-Host "[i] Module Building Initializing..." -ForegroundColor Yellow
    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()

    $ModuleOutput = New-PrepareStructure -Configuration $Configuration -Settings $Settings -PathToProject $PathToProject

    if ($Path -and $ModuleName) {
        if (-not (Test-Path -Path $Path)) {
            Write-Text "[-] Path $Path doesn't exists. This shouldn't be the case." -Color Red
        } else {
            $FullProjectPath = [io.path]::Combine($Path, $ModuleName)
            $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Build'
            Add-Directory -Directory $FullProjectPath
            foreach ($folder in $Folders) {
                $SubFolder = [io.path]::Combine($FullProjectPath, $Folder)
                Add-Directory -Directory $SubFolder
            }
            Copy-File -Source "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-ModuleStarter.txt" -Destination "$FullProjectPath\$ModuleName.psm1"
        }
    }
    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    if ($ModuleOutput -notcontains $false) {
        Write-Host "[i] Module Building Completed " -NoNewline -ForegroundColor Green
        Write-Host "[Time Total: $Execute]" -ForegroundColor Green
        if ($ExitCode) {
            Exit 1
        }
    } else {
        Write-Host "[i] Module Building Failed " -NoNewline -ForegroundColor Red
        Write-Host "[Time Total: $Execute]" -ForegroundColor Red
        if ($ExitCode) {
            Exit 0
        }
    }
}