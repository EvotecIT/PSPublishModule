Set-StrictMode -Version Latest

Describe 'Public release committed version validation' {
    BeforeAll {
        $script:RepositoryRoot = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot '..\..'))
        . (Join-Path $script:RepositoryRoot `
            'Build\Private\Assert-PowerForgeCommittedReleaseVersion.ps1')

        $script:ReleaseVersionTestRoot = Join-Path `
            ([IO.Path]::GetTempPath()) `
            ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory `
            -Path (Join-Path $script:ReleaseVersionTestRoot 'Module') `
            -Force | Out-Null
        New-Item -ItemType Directory `
            -Path (Join-Path $script:ReleaseVersionTestRoot 'Sample') `
            -Force | Out-Null
        @"
@{
    RootModule = 'PSPublishModule.psm1'
    ModuleVersion = '3.0.84'
    GUID = '00000000-0000-0000-0000-000000000001'
}
"@ | Set-Content `
            -LiteralPath (Join-Path `
                $script:ReleaseVersionTestRoot `
                'Module\PSPublishModule.psd1') `
            -Encoding UTF8
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <VersionPrefix>3.0.84</VersionPrefix>
  </PropertyGroup>
  <PropertyGroup Condition="'`$(TargetFramework)' == 'net8.0'">
    <GenerateDependencyFile>false</GenerateDependencyFile>
  </PropertyGroup>
</Project>
"@ | Set-Content `
            -LiteralPath (Join-Path `
                $script:ReleaseVersionTestRoot `
                'Sample\Sample.csproj') `
            -Encoding UTF8
        $script:ReleaseConfig = [pscustomobject] @{
            Packages = [pscustomobject] @{
                VersionTracks = [pscustomobject] @{
                    Main = [pscustomobject] @{
                        AnchorProject = 'Sample'
                        Projects = @()
                    }
                }
            }
        }
    }

    AfterAll {
        if ($script:ReleaseVersionTestRoot `
            -and (Test-Path -LiteralPath $script:ReleaseVersionTestRoot)) {
            Remove-Item -LiteralPath $script:ReleaseVersionTestRoot `
                -Recurse `
                -Force
        }
    }

    It 'accepts conditional property groups without VersionPrefix' {
        {
            Assert-PowerForgeCommittedReleaseVersion `
                -RepositoryRoot $script:ReleaseVersionTestRoot `
                -Version '3.0.84' `
                -ReleaseConfig $script:ReleaseConfig
        } | Should -Not -Throw
    }

    It 'still rejects a missing committed project version' {
        $projectPath = Join-Path `
            $script:ReleaseVersionTestRoot `
            'Sample\Sample.csproj'
        (Get-Content -Raw -LiteralPath $projectPath).Replace(
            '<VersionPrefix>3.0.84</VersionPrefix>',
            '') | Set-Content -LiteralPath $projectPath -Encoding UTF8

        {
            Assert-PowerForgeCommittedReleaseVersion `
                -RepositoryRoot $script:ReleaseVersionTestRoot `
                -Version '3.0.84' `
                -ReleaseConfig $script:ReleaseConfig
        } | Should -Throw '*found ''<missing>''*'
    }
}
