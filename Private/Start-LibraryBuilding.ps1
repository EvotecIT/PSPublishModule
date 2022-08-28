function Start-LibraryBuilding {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string] $RootDirectory,
        [string] $Version,
        [System.Collections.IDictionary] $LibraryConfiguration

    )
    if ($LibraryConfiguration.Count -eq 0) {
        return
    }

    $TranslateFrameworks = [ordered] @{
        'NetStandard2.0' = 'Standard'
        'net472'         = 'Default'
        'netcoreapp3.1'  = 'Core'
    }

    if ($LibraryConfiguration.Configuration) {
        $Configuration = $LibraryConfiguration.Configuration
    } else {
        $Configuration = 'Release'
    }

    #$RootDirectory = $PWD
    #$RootDirectory = $PSScriptRoot
    $ModuleProjectFile = [System.IO.Path]::Combine($RootDirectory, "Sources", $moduleName, $moduleName, "$ModuleName.csproj")
    $SourceFolder = [System.IO.Path]::Combine($RootDirectory, "Sources", $moduleName, $moduleName)
    $ModuleBinFolder = [System.IO.Path]::Combine($RootDirectory, "Lib")
    if (Test-Path -LiteralPath $ModuleBinFolder) {
        $Items = Get-ChildItem -LiteralPath $ModuleBinFolder -Recurse -Force
        $Items | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    }
    New-Item -Path $ModuleBinFolder -ItemType Directory -Force | Out-Null

    Push-Location -Path $SourceFolder

    [xml] $ProjectInformation = Get-Content -Raw -LiteralPath $ModuleProjectFile
    $SupportedFrameworks = foreach ($PropertyGroup in $ProjectInformation.Project.PropertyGroup) {
        if ($PropertyGroup.TargetFrameworks) {
            $PropertyGroup.TargetFrameworks -split ";"
        }
    }

    foreach ($Framework in $TranslateFrameworks.Keys) {
        if ($SupportedFrameworks.Contains($Framework)) {
            Write-Host "Building $Framework ($Configuration)"
            dotnet publish --configuration $Configuration --verbosity q -nologo -p:Version=$Version --framework $Framework
            if ($LASTEXITCODE) {
                Write-Warning -Message "Failed to build for $framework"
            }
        } else {
            continue
        }

        $PublishDirFolder = [System.IO.Path]::Combine($SourceFolder, "bin", $Configuration, $Framework, "publish", "*")
        $ModuleBinFrameworkFolder = [System.IO.Path]::Combine($ModuleBinFolder, $TranslateFrameworks[$Framework])

        New-Item -Path $ModuleBinFrameworkFolder -ItemType Directory -ErrorAction SilentlyContinue | Out-Null
        try {
            Copy-Item -Path $PublishDirFolder -Destination $ModuleBinFrameworkFolder -Recurse -Filter "*.dll" -ErrorAction Stop
        } catch {
            Write-Warning -Message "Copying $PublishDirFolder to $ModuleBinFrameworkFolder failed. Error: $($_.Exception.Message)"
        }
    }

    Pop-Location
}