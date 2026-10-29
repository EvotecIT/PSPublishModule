BeforeAll {
    $script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

Describe 'Public release GitHub environment credential binding' -Tag 'CredentialBinding' {
    BeforeAll {
        . (Join-Path $script:RepositoryRoot 'Build/Private/Set-PowerForgeReleaseGitHubEnvironment.ps1')
    }

    BeforeEach {
        $script:CredentialVariable = 'POWERFORGE_TEST_GITHUB_' + [Guid]::NewGuid().ToString('N')
        $script:Configuration = [pscustomobject]@{
            GitHub = [pscustomobject]@{ Owner = 'Example'; Repository = 'Product'; TokenFilePath = 'release-token.txt'; Token = 'old-inline-token'; TokenEnvName = 'OLD_TOKEN' }
            Packages = [pscustomobject]@{ GitHubAccessTokenFilePath = 'package-token.txt'; GitHubAccessToken = 'old-package-token' }
            Tools = [pscustomobject]@{ GitHub = [pscustomobject]@{ TokenFilePath = 'tool-token.txt'; Token = 'old-tool-token' } }
        }
    }

    AfterEach {
        [Environment]::SetEnvironmentVariable($script:CredentialVariable, $null, 'Process')
    }

    It 'keeps the configured credential routes when no override is requested' {
        $before = $script:Configuration | ConvertTo-Json -Depth 8
        $result = Set-PowerForgeReleaseGitHubEnvironment -ReleaseConfig $script:Configuration -EnvironmentVariable ''
        ($result | ConvertTo-Json -Depth 8) | Should -BeExactly $before
    }

    It 'binds every configured GitHub lane without copying the secret into configuration' {
        [Environment]::SetEnvironmentVariable($script:CredentialVariable, 'synthetic-process-only-secret', 'Process')
        $result = Set-PowerForgeReleaseGitHubEnvironment -ReleaseConfig $script:Configuration -EnvironmentVariable $script:CredentialVariable
        $result.GitHub.TokenEnvName | Should -BeExactly $script:CredentialVariable
        $result.Packages.GitHubAccessTokenEnvName | Should -BeExactly $script:CredentialVariable
        $result.Tools.GitHub.TokenEnvName | Should -BeExactly $script:CredentialVariable
        $result.GitHub.Owner | Should -BeExactly 'Example'
        $result.GitHub.Repository | Should -BeExactly 'Product'
        ($result | ConvertTo-Json -Depth 8) | Should -Not -Match 'synthetic-process-only-secret|old-inline-token|old-package-token|old-tool-token|release-token.txt|package-token.txt|tool-token.txt'
    }

    It 'does not invent optional package or tool lanes' {
        [Environment]::SetEnvironmentVariable($script:CredentialVariable, 'synthetic-secret', 'Process')
        $result = Set-PowerForgeReleaseGitHubEnvironment -ReleaseConfig ([pscustomobject]@{ GitHub = [pscustomobject]@{ Owner = 'Example'; Repository = 'Product' } }) -EnvironmentVariable $script:CredentialVariable
        $result.GitHub.TokenEnvName | Should -BeExactly $script:CredentialVariable
        $result.PSObject.Properties.Name | Should -Be @('GitHub')
    }

    It 'rejects an absent or blank environment credential before changing configuration' -TestCases @(
        @{ Value = $null }
        @{ Value = '   ' }
    ) {
        param($Value)
        [Environment]::SetEnvironmentVariable($script:CredentialVariable, $Value, 'Process')
        $before = $script:Configuration | ConvertTo-Json -Depth 8
        { Set-PowerForgeReleaseGitHubEnvironment -ReleaseConfig $script:Configuration -EnvironmentVariable $script:CredentialVariable } | Should -Throw '*GitHub credential environment variable*is missing or empty*'
        ($script:Configuration | ConvertTo-Json -Depth 8) | Should -BeExactly $before
    }

    It 'rejects an invalid variable name without exposing a credential' {
        { Set-PowerForgeReleaseGitHubEnvironment -ReleaseConfig $script:Configuration -EnvironmentVariable 'invalid=name' } | Should -Throw
    }
}

Describe 'Native public release environment option' -Tag 'NativeBinding' {
    It 'passes the trimmed process credential to recovery without persisting its value' -Tag 'NativeTrim' -Skip:($PSVersionTable.PSEdition -ne 'Desktop' -and -not $IsWindows) {
        $fixture = Join-Path $TestDrive 'present-release-source'
        New-Item -ItemType Directory -Path (Join-Path $fixture 'Build') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'Build/Private') -Destination (Join-Path $fixture 'Build/Private') -Recurse
        $wrapper = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'Build/Invoke-PowerForgePublicRelease.ps1') -Raw
        $tokens = $null
        $parseErrors = $null
        $ast = [Management.Automation.Language.Parser]::ParseInput($wrapper, [ref] $tokens, [ref] $parseErrors)
        # The fixture substitutes certificate metadata only. It never opens a
        # persistent certificate store or private key, and never reaches a build.
        $metadataBoundary = @'

function Get-Item {
    [CmdletBinding()]
    param([string] $LiteralPath)
    if ($LiteralPath -like 'Cert:*') {
        return [pscustomobject]@{ HasPrivateKey = $true; NotAfter = [datetime]::UtcNow.AddYears(1) }
    }
    Microsoft.PowerShell.Management\Get-Item @PSBoundParameters
}
'@
        $wrapper = $wrapper.Insert($ast.ParamBlock.Extent.EndOffset, $metadataBoundary)
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Invoke-PowerForgePublicRelease.ps1') -Value $wrapper
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Build-Project.ps1') -Value "throw 'The build boundary must not execute.'"
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Private/Assert-PowerForgeCommittedReleaseVersion.ps1') -Value 'function Assert-PowerForgeCommittedReleaseVersion { param($RepositoryRoot, $Version, $ReleaseConfig) }'
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Private/Get-PowerForgeReleasePackageIds.ps1') -Value 'function Get-PowerForgeReleasePackageIds { param($ReleaseConfig, $RepositoryRoot) "Fixture.Package" }'
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Private/Enable-PowerForgeVerifiedGitHubReleaseRecovery.ps1') -Value @'
function Enable-PowerForgeVerifiedGitHubReleaseRecovery {
    param($ReleaseConfig, $Version, $ExpectedCommit, $Token, $PackageIds, $NuGetSource, $ModuleName)
    [pscustomobject]@{ TrimmedCredentialMatches = ($Token -ceq 'synthetic-process-secret'); EnvironmentReference = $ReleaseConfig.GitHub.TokenEnvName } |
        ConvertTo-Json | Set-Content -LiteralPath $env:POWERFORGE_TEST_RECOVERY_RECORD
    throw 'Synthetic recovery boundary reached.'
}
'@
        [ordered]@{
            Module = @{ ModuleName = 'Fixture'; ModuleVersion = '1.2.3' }
            Packages = @{ VersionTracks = @{ Fixture = @{ ExpectedVersion = '1.2.X' } }; UpdateVersions = $false; CertificateThumbprint = 'FIXTURE'; CertificateStore = 'CurrentUser'; PublishApiKeyFilePath = $null; GitHubAccessTokenFilePath = 'unavailable.txt'; PublishSource = 'https://api.nuget.org/v3/index.json' }
            GitHub = @{ Owner = 'Example'; Repository = 'Product'; TokenFilePath = 'unavailable.txt' }
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'Build/release.json')
        '{"Segments":[{"Type":"GalleryNuget","Configuration":{"Enabled":true,"ApiKeyFilePath":null}}]}' |
            Set-Content -LiteralPath (Join-Path $fixture 'powerforge.json')
        & git -C $fixture init --quiet
        & git -C $fixture config core.autocrlf false
        & git -C $fixture add .
        & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit --quiet -m 'Present credential release fixture'
        if ($LASTEXITCODE) { throw 'Could not create the isolated Git fixture.' }
        $revision = (& git -C $fixture rev-parse HEAD).Trim()
        $variable = 'POWERFORGE_NATIVE_PRESENT_' + [Guid]::NewGuid().ToString('N')
        $record = Join-Path $TestDrive 'recovery-record.json'
        $receipt = Join-Path $TestDrive 'present-receipt.json'
        $previousRecordVariable = [Environment]::GetEnvironmentVariable('POWERFORGE_TEST_RECOVERY_RECORD')
        [Environment]::SetEnvironmentVariable($variable, '  synthetic-process-secret  ', 'Process')
        [Environment]::SetEnvironmentVariable('POWERFORGE_TEST_RECOVERY_RECORD', $record, 'Process')
        try {
            $output = & (Get-Command pwsh -ErrorAction Stop).Source -NoProfile -File (Join-Path $fixture 'Build/Invoke-PowerForgePublicRelease.ps1') -Operation Publish -Version 1.2.3 -ExpectedCommit $revision -GitHubTokenEnvironmentVariable $variable -ReceiptPath $receipt 2>&1
            $LASTEXITCODE | Should -Not -Be 0
            (Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json).ErrorMessage | Should -BeExactly 'Synthetic recovery boundary reached.'
            $proof = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
            $proof.TrimmedCredentialMatches | Should -BeTrue
            $proof.EnvironmentReference | Should -BeExactly $variable
            (Get-Content -LiteralPath $record -Raw) | Should -Not -Match 'synthetic-process-secret'
            (Get-Content -LiteralPath $receipt -Raw) | Should -Not -Match 'synthetic-process-secret'
            ($output | Out-String) | Should -Not -Match 'synthetic-process-secret|build boundary must not execute'
        } finally {
            [Environment]::SetEnvironmentVariable($variable, $null, 'Process')
            [Environment]::SetEnvironmentVariable('POWERFORGE_TEST_RECOVERY_RECORD', $previousRecordVariable, 'Process')
        }
    }

    It 'reports a missing environment credential before touching a signing key or publishing' -Skip:($PSVersionTable.PSEdition -ne 'Desktop' -and -not $IsWindows) {
        $fixture = Join-Path $TestDrive 'release-source'
        New-Item -ItemType Directory -Path (Join-Path $fixture 'Build') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'Build/Private') -Destination (Join-Path $fixture 'Build/Private') -Recurse
        Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'Build/Invoke-PowerForgePublicRelease.ps1') -Destination (Join-Path $fixture 'Build')
        Set-Content -LiteralPath (Join-Path $fixture 'Build/Build-Project.ps1') -Value "throw 'The build boundary must not execute.'"
        Set-Content -LiteralPath (Join-Path $fixture 'Build/release.json') -Value '{"GitHub":{"Owner":"Example","Repository":"Product","TokenFilePath":"unavailable.txt"}}'
        & git -C $fixture init --quiet
        & git -C $fixture config core.autocrlf false
        & git -C $fixture add .
        & git -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit --quiet -m 'Release fixture'
        if ($LASTEXITCODE) { throw 'Could not create the isolated Git fixture.' }
        $revision = (& git -C $fixture rev-parse HEAD).Trim()
        $variable = 'POWERFORGE_NATIVE_MISSING_' + [Guid]::NewGuid().ToString('N')
        $receipt = Join-Path $TestDrive 'receipt.json'
        $output = & (Get-Command pwsh -ErrorAction Stop).Source -NoProfile -File (Join-Path $fixture 'Build/Invoke-PowerForgePublicRelease.ps1') -Operation Publish -Version 1.2.3 -ExpectedCommit $revision -GitHubTokenEnvironmentVariable $variable -ReceiptPath $receipt 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        $failure = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
        $failure.ErrorMessage | Should -Match 'GitHub credential environment variable.*is missing or empty'
        ($output | Out-String) | Should -Not -Match 'signing certificate|build boundary must not execute'
        $failure.Success | Should -BeFalse
    }
}
