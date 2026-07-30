Set-StrictMode -Version Latest

Describe 'Public release committed version validation' {
    BeforeAll {
        $script:RepositoryRoot = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot '..\..'))
        . (Join-Path $script:RepositoryRoot `
            'Build\Private\Assert-PowerForgeCommittedReleaseVersion.ps1')
        . (Join-Path $script:RepositoryRoot `
            'Build\Private\Get-PowerForgeReleasePackageIds.ps1')
        . (Join-Path $script:RepositoryRoot `
            'Build\Private\Enable-PowerForgeVerifiedGitHubReleaseRecovery.ps1')

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
        $script:SampleProjectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <VersionPrefix>3.0.84</VersionPrefix>
    <PackageId>Sample.Package</PackageId>
  </PropertyGroup>
  <PropertyGroup Condition="'`$(TargetFramework)' == 'net8.0'">
    <GenerateDependencyFile>false</GenerateDependencyFile>
  </PropertyGroup>
</Project>
"@
        $script:SampleProjectContent | Set-Content `
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

    BeforeEach {
        $script:SampleProjectContent | Set-Content `
            -LiteralPath (Join-Path `
                $script:ReleaseVersionTestRoot `
                'Sample\Sample.csproj') `
            -Encoding UTF8
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

    It 'resolves PackageId when conditional property groups omit it' {
        $packageIds = Get-PowerForgeReleasePackageIds `
            -RepositoryRoot $script:ReleaseVersionTestRoot `
            -ReleaseConfig $script:ReleaseConfig

        @($packageIds) | Should -Be @('Sample.Package')
    }

    It 'uses Release when the optional package configuration is omitted' {
        {
            Get-PowerForgeReleasePackageIds `
                -RepositoryRoot $script:ReleaseVersionTestRoot `
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

    It 'falls back to the project name when PackageId is omitted' {
        $projectPath = Join-Path `
            $script:ReleaseVersionTestRoot `
            'Sample\Sample.csproj'
        (Get-Content -Raw -LiteralPath $projectPath).Replace(
            '<PackageId>Sample.Package</PackageId>',
            '') | Set-Content -LiteralPath $projectPath -Encoding UTF8

        $packageIds = Get-PowerForgeReleasePackageIds `
            -RepositoryRoot $script:ReleaseVersionTestRoot `
            -ReleaseConfig $script:ReleaseConfig

        @($packageIds) | Should -Be @('Sample')
    }

    It 'uses the evaluated AssemblyName when PackageId is omitted' {
        $projectPath = Join-Path `
            $script:ReleaseVersionTestRoot `
            'Sample\Sample.csproj'
        (Get-Content -Raw -LiteralPath $projectPath).Replace(
            '<PackageId>Sample.Package</PackageId>',
            '<AssemblyName>Evaluated.Package</AssemblyName>') |
            Set-Content -LiteralPath $projectPath -Encoding UTF8

        $packageIds = Get-PowerForgeReleasePackageIds `
            -RepositoryRoot $script:ReleaseVersionTestRoot `
            -ReleaseConfig $script:ReleaseConfig

        @($packageIds) | Should -Be @('Evaluated.Package')
    }

    It 'rejects package IDs that differ across target frameworks' {
        $projectPath = Join-Path `
            $script:ReleaseVersionTestRoot `
            'Sample\Sample.csproj'
        (Get-Content -Raw -LiteralPath $projectPath).Replace(
            '<PackageId>Sample.Package</PackageId>',
            @"
    <PackageId>Sample.Package</PackageId>
  </PropertyGroup>
  <PropertyGroup Condition="'`$(TargetFramework)' == 'net10.0'">
    <PackageId>Sample.Package.Net10</PackageId>
"@) | Set-Content -LiteralPath $projectPath -Encoding UTF8

        {
            Get-PowerForgeReleasePackageIds `
                -RepositoryRoot $script:ReleaseVersionTestRoot `
                -ReleaseConfig $script:ReleaseConfig
        } | Should -Throw '*exactly one package ID*'
    }

    It 'parses MSBuild JSON after first-run dotnet banner output' {
        $result = ConvertFrom-PowerForgeMsBuildPropertyOutput `
            -ProjectPath 'Sample\Sample.csproj' `
            -Output @(
                'Welcome to .NET 10.0!',
                'Telemetry information',
                '{',
                '  "Properties": {',
                '    "PackageId": "Sample.Package",',
                '    "TargetFrameworks": "net8.0;net10.0"',
                '  }',
                '}'
            )

        $result.Properties.PackageId | Should -Be 'Sample.Package'
        $result.Properties.TargetFrameworks | Should -Be 'net8.0;net10.0'
    }

    It 'treats a missing Git tag ref as an unpublished tag' {
        $script:GitHubProbeUris = @()
        $commit = Get-PowerForgeGitHubTagCommit `
            -Owner 'EvotecIT' `
            -Repository 'PSPublishModule' `
            -Tag 'v3.0.84' `
            -Token 'test-token' `
            -Probe {
                param($uri, $token)
                $script:GitHubProbeUris += $uri
                $null
            }

        $commit | Should -BeNullOrEmpty
        @($script:GitHubProbeUris).Count | Should -Be 1
        $script:GitHubProbeUris[0] |
            Should -BeLike '*/git/ref/tags/v3.0.84'
    }

    It 'resolves a commit only after the Git tag ref exists' {
        $script:GitHubProbeUris = @()
        $expectedCommit = '0123456789abcdef0123456789abcdef01234567'
        $commit = Get-PowerForgeGitHubTagCommit `
            -Owner 'EvotecIT' `
            -Repository 'PSPublishModule' `
            -Tag 'v3.0.84' `
            -Token 'test-token' `
            -Probe {
                param($uri, $token)
                $script:GitHubProbeUris += $uri
                if ($uri -like '*/git/ref/tags/*') {
                    return [pscustomobject] @{ ref = 'refs/tags/v3.0.84' }
                }
                [pscustomobject] @{ sha = $expectedCommit }
            }

        $commit | Should -Be $expectedCommit
        @($script:GitHubProbeUris).Count | Should -Be 2
        $script:GitHubProbeUris[1] |
            Should -BeLike '*/commits/v3.0.84'
    }

    It 'fails closed when an existing Git tag does not resolve to a commit' {
        {
            Get-PowerForgeGitHubTagCommit `
                -Owner 'EvotecIT' `
                -Repository 'PSPublishModule' `
                -Tag 'v3.0.84' `
                -Token 'test-token' `
                -Probe {
                    param($uri, $token)
                    if ($uri -like '*/git/ref/tags/*') {
                        return [pscustomobject] @{
                            ref = 'refs/tags/v3.0.84'
                        }
                    }
                    $null
                }
        } | Should -Throw '*exists but does not resolve*'
    }

    It 'proves repository access before interpreting release endpoint absence' {
        $releaseConfig = [pscustomobject] @{
            GitHub = [pscustomobject] @{
                Publish = $true
                Owner = 'EvotecIT'
                Repository = 'PSPublishModule'
                TagTemplate = 'v{Version}'
                ReuseExistingRelease = $false
                ReplaceExistingAssets = $false
            }
        }

        {
            Enable-PowerForgeVerifiedGitHubReleaseRecovery `
                -ReleaseConfig $releaseConfig `
                -Version '3.0.84' `
                -ExpectedCommit '0123456789abcdef0123456789abcdef01234567' `
                -Token 'test-token' `
                -PackageIds @('PowerForge') `
                -GetRepository { $null } `
                -GetReleaseByTag {
                    throw 'Release probe must not run.'
                } `
                -GetTagCommit {
                    throw 'Tag probe must not run.'
                } `
                -GetRegistryState {
                    throw 'Registry probe must not run.'
                }
        } | Should -Throw '*not accessible*'
    }
}
