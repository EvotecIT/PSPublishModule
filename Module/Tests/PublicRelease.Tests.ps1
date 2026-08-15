Set-StrictMode -Version Latest

Describe 'Standalone PowerForge installer host compatibility' {
    BeforeAll {
        $script:RepositoryRoot = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot '..\..'))
        $script:ToolInstallerPath = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot '..\..\Build\Install-PowerForgeTool.ps1'))
    }

    It 'parses without errors in the current PowerShell host' {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile(
            $script:ToolInstallerPath,
            [ref] $tokens,
            [ref] $errors) | Out-Null

        @($errors).Count | Should -Be 0
    }

    It 'uses the shared Windows PowerShell 5.1 and PowerShell 7 surface' {
        $content = Get-Content -Raw -LiteralPath $script:ToolInstallerPath
        $content | Should -Not -Match 'ConvertFrom-Json\s+-Depth'
        $content | Should -Not -Match '\$(?:IsWindows|IsMacOS|IsLinux)\b'
        $content | Should -Match '\$PSVersionTable\.PSEdition'
    }

    It 'publishes a native standalone CLI asset for every installer host RID' {
        $releaseConfig = Get-Content -Raw -LiteralPath (
            Join-Path $script:RepositoryRoot 'Build\release.json') | ConvertFrom-Json
        $powerForgeTarget = @($releaseConfig.Tools.Targets) |
            Where-Object Name -EQ 'PowerForge' |
            Select-Object -First 1

        $powerForgeTarget | Should -Not -BeNullOrEmpty
        @($powerForgeTarget.Runtimes) | Should -Be @(
            'win-x64',
            'win-arm64',
            'linux-x64',
            'linux-arm64',
            'osx-x64',
            'osx-arm64'
        )
    }
}

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

    It 'uses the package-level PackageId when only the outer build overrides it' {
        $projectPath = Join-Path `
            $script:ReleaseVersionTestRoot `
            'Sample\Sample.csproj'
        (Get-Content -Raw -LiteralPath $projectPath).Replace(
            '<PackageId>Sample.Package</PackageId>',
            @"
    <PackageId Condition="'`$(TargetFramework)' == ''">Outer.Package</PackageId>
"@) | Set-Content -LiteralPath $projectPath -Encoding UTF8

        $packageIds = Get-PowerForgeReleasePackageIds `
            -RepositoryRoot $script:ReleaseVersionTestRoot `
            -ReleaseConfig $script:ReleaseConfig

        @($packageIds) | Should -Be @('Outer.Package')
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
                    return [pscustomobject] @{
                        ref = 'refs/tags/v3.0.84'
                        object = [pscustomobject] @{
                            type = 'commit'
                            sha = $expectedCommit
                        }
                    }
                }
                throw "Unexpected probe URI: $uri"
            }

        $commit | Should -Be $expectedCommit
        @($script:GitHubProbeUris).Count | Should -Be 1
    }

    It 'peels an annotated tag through its exact tag object' {
        $script:GitHubProbeUris = @()
        $tagObject = '1111111111111111111111111111111111111111'
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
                    return [pscustomobject] @{
                        ref = 'refs/tags/v3.0.84'
                        object = [pscustomobject] @{
                            type = 'tag'
                            sha = $tagObject
                        }
                    }
                }
                if ($uri -like "*/git/tags/$tagObject") {
                    return [pscustomobject] @{
                        object = [pscustomobject] @{
                            type = 'commit'
                            sha = $expectedCommit
                        }
                    }
                }
                throw "Unexpected probe URI: $uri"
            }

        $commit | Should -Be $expectedCommit
        @($script:GitHubProbeUris).Count | Should -Be 2
        $script:GitHubProbeUris[1] | Should -BeLike "*/git/tags/$tagObject"
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
                            object = [pscustomobject] @{
                                type = 'tag'
                                sha = '1111111111111111111111111111111111111111'
                            }
                        }
                    }
                    $null
                }
        } | Should -Throw '*annotated tag object is not accessible*'
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

    It 'rejects a repository token without release-capable write permission' {
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
                -GetRepository {
                    [pscustomobject] @{
                        permissions = [pscustomobject] @{
                            pull = $true
                            push = $false
                        }
                    }
                } `
                -GetReleaseByTag {
                    throw 'Release probe must not run.'
                } `
                -GetTagCommit {
                    throw 'Tag probe must not run.'
                } `
                -GetRegistryState {
                    throw 'Registry probe must not run.'
                }
        } | Should -Throw '*not writable*'
    }
}
