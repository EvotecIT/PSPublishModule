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
