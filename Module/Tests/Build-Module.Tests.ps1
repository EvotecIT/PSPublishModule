Describe 'Build-Module' {
    BeforeAll {
        # Import the module to make sure all functions are available
        $moduleManifest = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1'
        Import-Module $moduleManifest -Force

        # Set up temp directory
        if ($IsWindows) {
            $TempDir = $env:TEMP
        } else {
            $TempDir = '/tmp'
        }
    }

    It 'Create New Module' {
        $ModuleName = 'NewTestModule123456'
        $Path = [io.path]::Combine($TempDir, 'Junk')

        # lets remove junk first if it exists
        $FullModulePath = [io.path]::Combine($Path, $ModuleName)
        if (Test-Path -Path $FullModulePath) {
            Remove-Item -Path $FullModulePath -Recurse -Force
        }
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        # Handle module paths for different operating systems
        if ($IsWindows) {
            $DirectoryModulesCore = [io.path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments), 'PowerShell', 'Modules')
            $DirectoryModules = [io.path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments), 'WindowsPowerShell', 'Modules')
        } else {
            $DirectoryModulesCore = [io.path]::Combine($env:HOME, '.local', 'share', 'powershell', 'Modules')
            $DirectoryModules = $DirectoryModulesCore # On non-Windows, use the same path
        }

        $Desktop = [io.path]::Combine($DirectoryModules, $ModuleName)
        $Core = [io.path]::Combine($DirectoryModulesCore, $ModuleName)

        if (Test-Path -Path $Desktop) {
            Remove-Item -Path $Desktop -Recurse -Force
        }
        if (Test-Path -Path $Core) {
            Remove-Item -Path $Core -Recurse -Force
        }

        $Exists = Test-Path -Path $Desktop
        $Exists | Should -BeFalse

        $Exists = Test-Path -Path $Core
        $Exists | Should -BeFalse

        # lets create the path to folder as we create it deep in temp
        New-Item -Path $Path -ItemType Directory -Force | Out-Null

        # Lets build module
        Build-Module -Path $Path -ModuleName $ModuleName

        # lets see if module is created
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeTrue

        Get-ChildItem -Path $FullModulePath -Force | Format-Table -AutoSize | Out-String | Write-Host

        # lets find if all files are copied
        $FilesRelative = "$ModuleName.psd1", "$ModuleName.psm1", "CHANGELOG.MD", ".gitignore", "LICENSE", "README.MD"
        foreach ($File in $FilesRelative) {
            $FilePath = [io.path]::Combine($FullModulePath, $File)
            $Exists = Test-Path -Path $FilePath
            $Exists | Should -BeTrue

            $Item = Get-Item -Path $FilePath -Force
            $Item.Length | Should -BeGreaterThan 0
        }
        $FilesFullPath = [io.path]::Combine($FullModulePath, "Build", "Build-Module.ps1")
        foreach ($File in $FilesFullPath) {
            $Exists = Test-Path -Path $File
            $Exists | Should -BeTrue
        }
        $Directories = "Build", "Examples", "Ignore", "Private", 'Public'
        foreach ($Directory in $Directories) {
            $Exists = Test-Path -Path ([io.path]::Combine($FullModulePath, $Directory)) -PathType Container
            $Exists | Should -BeTrue
        }
    }
}
