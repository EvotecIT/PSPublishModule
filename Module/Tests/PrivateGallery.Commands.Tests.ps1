function script:Get-PrivateGalleryTestBinaryModulePath {
    $releaseRoot = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..') -ChildPath 'PSPublishModule\bin\Release'
    if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
        return $null
    }

    $candidateFrameworks = Get-ChildItem -LiteralPath $releaseRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            if ($PSVersionTable.PSEdition -eq 'Desktop') {
                $_.Name -eq 'net472'
            } else {
                $_.Name -match '^net\d'
            }
        } |
        Sort-Object -Property Name -Descending

    foreach ($framework in $candidateFrameworks) {
        $path = Join-Path -Path $framework.FullName -ChildPath 'PSPublishModule.dll'
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    return $null
}

function script:Import-PrivateGalleryTestModule {
    param(
        [Parameter(Mandatory)]
        [string] $ModuleManifest
    )

    if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH -and (Test-Path -LiteralPath $ModuleManifest -PathType Leaf)) {
        return Import-Module $ModuleManifest -Force -PassThru -ErrorAction Stop
    }

    $loadedModule = Get-Module PSPublishModule -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($loadedModule) {
        return $loadedModule
    }

    $manifestBinary = Join-Path -Path (Join-Path -Path (Split-Path -Path $ModuleManifest -Parent) -ChildPath 'Lib') -ChildPath 'PSPublishModule.dll'
    if ((Test-Path -LiteralPath $ModuleManifest -PathType Leaf) -and
        (Test-Path -LiteralPath $manifestBinary -PathType Leaf)) {
        try {
            return Import-Module $ModuleManifest -Force -PassThru -ErrorAction Stop
        } catch {
            $script:PrivateGalleryManifestImportError = $_
        }
    }

    $binaryModule = Get-PrivateGalleryTestBinaryModulePath
    if ($binaryModule) {
        try {
            return Import-Module $binaryModule -Force -PassThru -ErrorAction Stop
        } catch {
            if ($script:PrivateGalleryManifestImportError) {
                throw $script:PrivateGalleryManifestImportError
            }

            return Import-Module $ModuleManifest -Force -PassThru -ErrorAction Stop
        }
    }

    return Import-Module $ModuleManifest -Force -PassThru -ErrorAction Stop
}

Describe 'Private gallery command metadata' {
    BeforeAll {
        $moduleManifest = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        $script:PrivateGalleryTestModule = Import-PrivateGalleryTestModule -ModuleManifest $moduleManifest

        $command = Get-Command Connect-ModuleRepository -ErrorAction Stop
        $script:PrivateGalleryTestAssembly = if ($script:PrivateGalleryTestModule.ImplementingAssembly) {
            $script:PrivateGalleryTestModule.ImplementingAssembly
        } else {
            $command.ImplementingType.Assembly
        }

        $script:PrivateGalleryProfileRoot = Join-Path ([IO.Path]::GetTempPath()) ("PSPublishModule.PrivateGallery.Tests." + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:PrivateGalleryProfileRoot -Force | Out-Null
        $script:PrivateGalleryProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'profiles.json'
        $script:PrivateGalleryMachineProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'machine-profiles.json'
        $script:PrivateGalleryLiveValidationRunnerPath = Join-Path $PSScriptRoot 'Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1'
        $script:PrivateGalleryJFrogValidationRunnerPath = Join-Path $PSScriptRoot 'Invoke-PrivateGalleryJFrogSsoValidation.ps1'
        $script:PrivateGalleryLiveEvidenceSummaryPath = Join-Path $PSScriptRoot 'Convert-PrivateGalleryLiveEvidenceToMarkdown.ps1'
        $script:PrivateGalleryGitHubConfigurationPath = Join-Path $PSScriptRoot 'Test-PrivateGalleryGitHubLiveValidationConfiguration.ps1'
        $script:PrivateGalleryRepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
        $script:PrivateGalleryManifestPath = Join-Path (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path 'PSPublishModule.psd1'
        $script:PrivateGalleryLiveValidationWorkflowPath = Join-Path $script:PrivateGalleryRepositoryRoot '.github\workflows\private-gallery-live-validation.yml'
        $script:PrivateGalleryBuildWorkflowPath = Join-Path $script:PrivateGalleryRepositoryRoot '.github\workflows\BuildModule.yml'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
        $env:POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH = $script:PrivateGalleryMachineProfilePath
    }

    AfterAll {
        Remove-Item Env:\POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:\POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH -ErrorAction SilentlyContinue
        if ($script:PrivateGalleryProfileRoot -and (Test-Path -LiteralPath $script:PrivateGalleryProfileRoot)) {
            Remove-Item -LiteralPath $script:PrivateGalleryProfileRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'keeps the Azure Artifacts live validation runner parseable' {
        foreach ($scriptPath in @($script:PrivateGalleryLiveValidationRunnerPath, $script:PrivateGalleryJFrogValidationRunnerPath, $script:PrivateGalleryLiveEvidenceSummaryPath, $script:PrivateGalleryGitHubConfigurationPath)) {
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref] $tokens, [ref] $errors) | Out-Null

            $errors | Should -BeNullOrEmpty
        }
    }

    It 'reports GitHub live validation configuration readiness without exposing secret values' {
        $variableJson = @(
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_ORGANIZATION' },
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_FEED' },
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_MODULE_NAME' },
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_RUNNER_LABELS' }
        ) | ConvertTo-Json
        $secretJson = @(
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS' }
        ) | ConvertTo-Json

        $result = & $script:PrivateGalleryGitHubConfigurationPath -Repository EvotecIT/PSPublishModule -VariableJson $variableJson -SecretJson $secretJson -RequireUnattendedCredentialProviderSecret -NoFail -PassThru

        $result.Succeeded | Should -BeTrue
        $result.RequiredVariablesMissing | Should -BeNullOrEmpty
        $result.RequiredVariablesPresent | Should -Contain 'PSPUBLISHMODULE_AZDO_ORGANIZATION'
        $result.OptionalVariablesPresent | Should -Contain 'PSPUBLISHMODULE_AZDO_RUNNER_LABELS'
        $result.CredentialProviderSecretsPresent | Should -Contain 'PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS'
        $result.UnattendedCredentialProviderSecretConfigured | Should -BeTrue
        ($result | ConvertTo-Json -Depth 4) | Should -Not -Match 'secret-value'
    }

    It 'reports required GitHub variables as missing before live validation dispatch' {
        $variableJson = @(
            [ordered]@{ name = 'PSPUBLISHMODULE_AZDO_ORGANIZATION' }
        ) | ConvertTo-Json
        $secretJson = @() | ConvertTo-Json

        $result = & $script:PrivateGalleryGitHubConfigurationPath -Repository EvotecIT/PSPublishModule -VariableJson $variableJson -SecretJson $secretJson -RequireUnattendedCredentialProviderSecret -NoFail -PassThru

        $result.Succeeded | Should -BeFalse
        $result.RequiredVariablesMissing | Should -Contain 'PSPUBLISHMODULE_AZDO_FEED'
        $result.RequiredVariablesMissing | Should -Contain 'PSPUBLISHMODULE_AZDO_MODULE_NAME'
        $result.UnattendedCredentialProviderSecretConfigured | Should -BeFalse
        $result.RequiredActions | Should -Contain "Define repository variable 'PSPUBLISHMODULE_AZDO_FEED'."
        $result.SuggestedSetupCommands | Should -Contain "gh variable set PSPUBLISHMODULE_AZDO_FEED --repo EvotecIT/PSPublishModule --body '<azure-artifacts-feed>'"
        $result.SuggestedSetupCommands | Should -Contain "gh secret set PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS --repo EvotecIT/PSPublishModule < external-feed-endpoints.json"
        $result.SuggestedDispatchCommands | Should -Contain "gh workflow run BuildModule.yml --repo EvotecIT/PSPublishModule --ref <feature-or-main-branch> -f privateGalleryLiveValidation=true -f privateGalleryGenerateDisposablePackage=true"
    }

    It 'renders live validation setup and dispatch guidance without secret values' {
        $markdown = & $script:PrivateGalleryGitHubConfigurationPath -Repository EvotecIT/PSPublishModule -VariableJson '[]' -SecretJson '[]' -RequireUnattendedCredentialProviderSecret -NoFail -Markdown
        $text = $markdown -join [Environment]::NewLine

        $text | Should -Match 'Suggested setup commands'
        $text | Should -Match 'gh variable set PSPUBLISHMODULE_AZDO_ORGANIZATION --repo EvotecIT/PSPublishModule'
        $text | Should -Match 'gh secret set PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS --repo EvotecIT/PSPublishModule < external-feed-endpoints\.json'
        $text | Should -Match 'gh workflow run BuildModule\.yml --repo EvotecIT/PSPublishModule'
        $text | Should -Match 'gh workflow run private-gallery-live-validation\.yml --repo EvotecIT/PSPublishModule'
        $text | Should -Not -Match 'secret-value'
    }

    It 'ships a manual Azure Artifacts live validation workflow' {
        Test-Path -LiteralPath $script:PrivateGalleryLiveValidationWorkflowPath -PathType Leaf | Should -BeTrue

        $workflow = Get-Content -LiteralPath $script:PrivateGalleryLiveValidationWorkflowPath -Raw
        $workflow | Should -Match 'name:\s+Private Gallery Live Validation'
        $workflow | Should -Match 'workflow_dispatch:'
        $workflow | Should -Match 'runnerLabels:'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ORGANIZATION'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_FEED'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_MODULE_NAME'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_RUNNER_LABELS'
        $workflow | Should -Match 'runs-on:\s+\$\{\{\s*fromJSON\(inputs\.runnerLabels\s+\|\|\s+vars\.PSPUBLISHMODULE_AZDO_RUNNER_LABELS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ARTIFACTS_FEED_ENDPOINTS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_VSS_NUGET_EXTERNAL_FEED_ENDPOINTS'
        $workflow | Should -Match 'Invoke-PrivateGalleryAzureArtifactsLiveValidation\.ps1'
        $workflow | Should -Match 'Convert-PrivateGalleryLiveEvidenceToMarkdown\.ps1'
        $workflow | Should -Match 'GenerateDisposablePackage'
        $workflow | Should -Match 'private-gallery-live\.evidence\.json'
        $workflow | Should -Match 'GITHUB_STEP_SUMMARY'
        $workflow | Should -Match 'actions/upload-artifact@v4'
    }

    It 'offers pre-merge live validation through the existing module build workflow' {
        Test-Path -LiteralPath $script:PrivateGalleryBuildWorkflowPath -PathType Leaf | Should -BeTrue

        $workflow = Get-Content -LiteralPath $script:PrivateGalleryBuildWorkflowPath -Raw
        $workflow | Should -Match 'privateGalleryLiveValidation:'
        $workflow | Should -Match 'PrivateGalleryLiveValidation:'
        $workflow | Should -Match 'inputs\.privateGalleryLiveValidation\s+==\s+true'
        $workflow | Should -Match 'privateGalleryRunnerLabels:'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ORGANIZATION'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_FEED'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_MODULE_NAME'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_RUNNER_LABELS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_ARTIFACTS_FEED_ENDPOINTS'
        $workflow | Should -Match 'PSPUBLISHMODULE_AZDO_VSS_NUGET_EXTERNAL_FEED_ENDPOINTS'
        $workflow | Should -Match 'Invoke-PrivateGalleryAzureArtifactsLiveValidation\.ps1'
        $workflow | Should -Match 'Convert-PrivateGalleryLiveEvidenceToMarkdown\.ps1'
        $workflow | Should -Match 'private-gallery-live-validation'
    }

    It 'formats Azure Artifacts live evidence as a non-secret Markdown summary' {
        $evidencePath = Join-Path $script:PrivateGalleryProfileRoot 'summary.evidence.json'
        [ordered]@{
            SchemaVersion          = 1
            GeneratedAtUtc         = '2026-05-20T00:00:00Z'
            Succeeded              = $true
            Provider               = 'AzureArtifacts'
            Organization           = 'contoso'
            Project                = 'Platform'
            Feed                   = 'Modules'
            ModuleName             = 'ModuleA'
            ProfileName            = 'LiveAzureArtifacts'
            PublishPackageSupplied = $true
            PublishPackageName     = 'Company.Tools.1.2.3.nupkg'
            GeneratedDisposablePackage = $true
            DisposablePackageName  = 'Company.Tools'
            DisposablePackageVersion = '1.2.3'
            UnattendedCredentialProviderEnvironment = [ordered]@{
                ArtifactsExternalFeedEndpointsConfigured = $true
                ArtifactsFeedEndpointsConfigured = $false
                LegacyVssExternalFeedEndpointsConfigured = $false
            }
            ValidationItems        = @(
                [ordered]@{
                    Name                              = 'OnboardingInstallUpdate'
                    Succeeded                         = $true
                    AccessProbeSucceeded              = $true
                    BootstrapPackageGenerated         = $true
                    BootstrapPackageContainsSecrets   = $false
                    BootstrapScriptExecuted           = $true
                    InstallResultReturned             = $true
                    UpdateResultReturned              = $true
                    PublishConfigurationHasCredential = $false
                },
                [ordered]@{
                    Name                 = 'PublishPackage'
                    Succeeded            = $true
                    AccessProbeSucceeded = $true
                    PushedPackageNames   = @('Company.Tools.1.2.3.nupkg')
                    FailedCount          = 0
                }
            )
            EvidenceValidationErrors = @()
            Pester                 = [ordered]@{
                Result               = 'Passed'
                TotalCount           = 2
                PassedCount          = 2
                FailedCount          = 0
                SkippedCount         = 0
                DurationMilliseconds = 250
            }
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

        $summary = & $script:PrivateGalleryLiveEvidenceSummaryPath -EvidenceFile $evidencePath

        $summary | Should -Match '### Private Gallery Live Validation'
        $summary | Should -Match '\| Succeeded \| True \|'
        $summary | Should -Match '\| Pester result \| Passed \|'
        $summary | Should -Match '\| Passed / Failed / Skipped \| 2 / 0 / 0 \|'
        $summary | Should -Match '\| Publish proof enabled \| True \|'
        $summary | Should -Match '\| Generated disposable package \| True \|'
        $summary | Should -Match '\| Credential-provider external endpoints configured \| True \|'
        $summary | Should -Match '\| Credential-provider feed endpoints configured \| False \|'
        $summary | Should -Match '\| Legacy VSS external endpoints configured \| False \|'
        $summary | Should -Match '\| OnboardingInstallUpdate \| True \| AccessProbe=True, BootstrapPackage=True, BootstrapScript=True, Install=True, Update=True \|'
        $summary | Should -Match '\| PublishPackage \| True \| AccessProbe=True, PushedPackages=1, FailedPackages=0 \|'
    }

    It 'restores the caller profile path after the Azure Artifacts live validation runner' {
        $originalProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'caller-profiles.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $originalProfilePath
        $env:POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_TIMEOUT_MINUTES = '11'
        $evidencePath = Join-Path $script:PrivateGalleryProfileRoot 'live.evidence.json'
        $outputPath = Join-Path $script:PrivateGalleryProfileRoot 'live.xml'
        $packagePath = Join-Path $script:PrivateGalleryProfileRoot 'Company.Tools.1.2.3.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'not a real package' -Encoding UTF8

        function Invoke-Pester {
            param(
                [string] $Path,
                [string] $Output,
                [string] $OutputFile,
                [string] $OutputFormat,
                [switch] $PassThru
            )

            $Path | Should -Match 'PrivateGallery\.AzureArtifacts\.Live\.Tests\.ps1$'
            $PassThru.IsPresent | Should -BeTrue
            $env:PSPUBLISHMODULE_AZDO_LIVE | Should -Be '1'
            $env:PSPUBLISHMODULE_AZDO_ORGANIZATION | Should -Be 'contoso'
            $env:PSPUBLISHMODULE_AZDO_FEED | Should -Be 'Modules'
            $env:PSPUBLISHMODULE_AZDO_MODULE_NAME | Should -Be 'ModuleA'
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = 'mutated-by-live-test'
            $env:POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_TIMEOUT_MINUTES | Should -Be '30'
            $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH | Should -Not -BeNullOrEmpty

            @(
                [ordered]@{
                    Name                              = 'OnboardingInstallUpdate'
                    Succeeded                         = $true
                    ProfileName                       = 'LiveAzureArtifacts'
                    AccessProbeSucceeded              = $true
                    BootstrapPackageGenerated         = $true
                    BootstrapPackageContainsSecrets   = $false
                    BootstrapScriptExecuted           = $true
                    PublishConfigurationHasCredential = $false
                    InstallResultReturned             = $true
                    UpdateResultReturned              = $true
                },
                [ordered]@{
                    Name                 = 'PublishPackage'
                    Succeeded            = $true
                    ProfileName          = 'LiveAzureArtifacts'
                    AccessProbeSucceeded = $true
                    PackageName          = 'Company.Tools.1.2.3.nupkg'
                    PushedPackageNames   = @('Company.Tools.1.2.3.nupkg')
                    FailedCount          = 0
                }
            ) | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH -Encoding UTF8

            [pscustomobject]@{
                FailedCount = 0
                PassedCount = 2
                SkippedCount = 0
                TotalCount  = 2
                Result      = 'Passed'
                Duration    = [TimeSpan]::FromMilliseconds(250)
            }
        }

        try {
            $result = & $script:PrivateGalleryLiveValidationRunnerPath -Organization contoso -Project Platform -Feed Modules -ModuleName ModuleA -PublishPackagePath $packagePath -OutputFile $outputPath -EvidenceFile $evidencePath -CredentialProviderWaitMinutes 30 -PassThru
            $result.FailedCount | Should -Be 0
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH | Should -Be $originalProfilePath
            $env:POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_TIMEOUT_MINUTES | Should -Be '11'
            $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            $evidence.Succeeded | Should -BeTrue
            $evidence.CredentialProviderWaitMinutes | Should -Be 30
            $evidence.UnattendedCredentialProviderEnvironment.ArtifactsExternalFeedEndpointsConfigured | Should -BeFalse
            $evidence.UnattendedCredentialProviderEnvironment.ArtifactsFeedEndpointsConfigured | Should -BeFalse
            $evidence.UnattendedCredentialProviderEnvironment.LegacyVssExternalFeedEndpointsConfigured | Should -BeFalse
            $evidence.Provider | Should -Be 'AzureArtifacts'
            $evidence.Organization | Should -Be 'contoso'
            $evidence.Project | Should -Be 'Platform'
            $evidence.Feed | Should -Be 'Modules'
            $evidence.ModuleName | Should -Be 'ModuleA'
            $evidence.ProfileName | Should -Be 'LiveAzureArtifacts'
            $evidence.PublishPackageSupplied | Should -BeTrue
            $evidence.PublishPackageName | Should -Be 'Company.Tools.1.2.3.nupkg'
            $evidence.ValidationItems.Count | Should -Be 2
            $evidence.ValidationItems[0].Name | Should -Be 'OnboardingInstallUpdate'
            $evidence.ValidationItems[0].AccessProbeSucceeded | Should -BeTrue
            $evidence.ValidationItems[0].BootstrapPackageGenerated | Should -BeTrue
            $evidence.ValidationItems[0].BootstrapPackageContainsSecrets | Should -BeFalse
            $evidence.ValidationItems[0].BootstrapScriptExecuted | Should -BeTrue
            $evidence.ValidationItems[0].PublishConfigurationHasCredential | Should -BeFalse
            $evidence.ValidationItems[0].InstallResultReturned | Should -BeTrue
            $evidence.ValidationItems[0].UpdateResultReturned | Should -BeTrue
            $evidence.ValidationItems[1].Name | Should -Be 'PublishPackage'
            $evidence.ValidationItems[1].PackageName | Should -Be 'Company.Tools.1.2.3.nupkg'
            $evidence.ValidationItems[1].PushedPackageNames | Should -Contain 'Company.Tools.1.2.3.nupkg'
            $evidence.ValidationItems[1].FailedCount | Should -Be 0
            $evidence.Pester.TotalCount | Should -Be 2
            $evidence.Pester.PassedCount | Should -Be 2
            $evidence.Pester.FailedCount | Should -Be 0
            $evidence.Pester.OutputFile | Should -Be $outputPath
            $evidence.Pester.OutputFormat | Should -Be 'NUnitXml'
        } finally {
            Remove-Item Function:\Invoke-Pester -ErrorAction SilentlyContinue
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
            Remove-Item Env:\POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_TIMEOUT_MINUTES -ErrorAction SilentlyContinue
        }
    }

    It 'generates a disposable package for Azure Artifacts live publish validation' {
        $originalProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'caller-profiles-generated-package.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $originalProfilePath
        $evidencePath = Join-Path $script:PrivateGalleryProfileRoot 'generated-package-live.evidence.json'

        function Invoke-Pester {
            param(
                [string] $Path,
                [string] $Output,
                [switch] $PassThru
            )

            $PassThru.IsPresent | Should -BeTrue
            $env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE | Should -Be '1'
            $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH -PathType Leaf | Should -BeTrue
            [IO.Path]::GetFileName($env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH) | Should -Be 'Company.LiveValidation.0.0.1-live.1.nupkg'

            @(
                [ordered]@{
                    Name                              = 'OnboardingInstallUpdate'
                    Succeeded                         = $true
                    ProfileName                       = 'LiveAzureArtifacts'
                    AccessProbeSucceeded              = $true
                    BootstrapPackageGenerated         = $true
                    BootstrapPackageContainsSecrets   = $false
                    BootstrapScriptExecuted           = $true
                    PublishConfigurationHasCredential = $false
                    InstallResultReturned             = $true
                    UpdateResultReturned              = $true
                },
                [ordered]@{
                    Name                 = 'PublishPackage'
                    Succeeded            = $true
                    ProfileName          = 'LiveAzureArtifacts'
                    AccessProbeSucceeded = $true
                    PackageName          = [IO.Path]::GetFileName($env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH)
                    PushedPackageNames   = @([IO.Path]::GetFileName($env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH))
                    FailedCount          = 0
                }
            ) | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH -Encoding UTF8

            [pscustomobject]@{
                FailedCount = 0
                PassedCount = 2
                SkippedCount = 0
                TotalCount  = 2
                Result      = 'Passed'
            }
        }

        try {
            & $script:PrivateGalleryLiveValidationRunnerPath `
                -Organization contoso `
                -Feed Modules `
                -ModuleName ModuleA `
                -GenerateDisposablePackage `
                -DisposablePackageName 'Company.LiveValidation' `
                -DisposablePackageVersion '0.0.1-live.1' `
                -EvidenceFile $evidencePath

            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH | Should -Be $originalProfilePath
            $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            $evidence.Succeeded | Should -BeTrue
            $evidence.PublishPackageSupplied | Should -BeTrue
            $evidence.GeneratedDisposablePackage | Should -BeTrue
            $evidence.DisposablePackageName | Should -Be 'Company.LiveValidation'
            $evidence.DisposablePackageVersion | Should -Be '0.0.1-live.1'
            $evidence.PublishPackageName | Should -Be 'Company.LiveValidation.0.0.1-live.1.nupkg'
            $evidence.EvidenceValidationErrors | Should -BeNullOrEmpty
        } finally {
            Remove-Item Function:\Invoke-Pester -ErrorAction SilentlyContinue
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
        }
    }

    It 'fails the Azure Artifacts live validation runner when Pester reports failures' {
        $originalProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'caller-profiles-failed.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $originalProfilePath
        $evidencePath = Join-Path $script:PrivateGalleryProfileRoot 'failed-live.evidence.json'

        function Invoke-Pester {
            [pscustomobject]@{
                FailedCount = 2
                PassedCount = 1
                SkippedCount = 0
                TotalCount  = 3
                Result      = 'Failed'
            }
        }

        try {
            { & $script:PrivateGalleryLiveValidationRunnerPath -Organization contoso -Feed Modules -ModuleName ModuleA -EvidenceFile $evidencePath } |
                Should -Throw "Live Azure Artifacts private gallery validation failed: 2 Pester test(s) failed."
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH | Should -Be $originalProfilePath
            $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            $evidence.Succeeded | Should -BeFalse
            $evidence.Pester.Result | Should -Be 'Failed'
            $evidence.Pester.FailedCount | Should -Be 2
        } finally {
            Remove-Item Function:\Invoke-Pester -ErrorAction SilentlyContinue
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
        }
    }

    It 'fails the Azure Artifacts live validation runner when required evidence details are missing' {
        $originalProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'caller-profiles-missing-evidence.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $originalProfilePath
        $evidencePath = Join-Path $script:PrivateGalleryProfileRoot 'missing-evidence-live.evidence.json'
        $packagePath = Join-Path $script:PrivateGalleryProfileRoot 'Company.Tools.9.9.9.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'not a real package' -Encoding UTF8

        function Invoke-Pester {
            param(
                [string] $Path,
                [string] $Output,
                [switch] $PassThru
            )

            $PassThru.IsPresent | Should -BeTrue
            $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH | Should -Not -BeNullOrEmpty

            [pscustomobject]@{
                FailedCount = 0
                PassedCount = 2
                SkippedCount = 0
                TotalCount  = 2
                Result      = 'Passed'
            }
        }

        try {
            {
                & $script:PrivateGalleryLiveValidationRunnerPath `
                    -Organization contoso `
                    -Feed Modules `
                    -ModuleName ModuleA `
                    -PublishPackagePath $packagePath `
                    -EvidenceFile $evidencePath
            } | Should -Throw "*Live Azure Artifacts evidence validation failed*"

            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH | Should -Be $originalProfilePath
            $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
            $evidence.Succeeded | Should -BeFalse
            $evidence.Pester.Result | Should -Be 'Passed'
            $evidence.EvidenceValidationErrors | Should -Contain "Required validation item 'OnboardingInstallUpdate' was not written."
            $evidence.EvidenceValidationErrors | Should -Contain "Required validation item 'PublishPackage' was not written."
        } finally {
            Remove-Item Function:\Invoke-Pester -ErrorAction SilentlyContinue
            $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
        }
    }

    It 'exposes the private gallery wrapper cmdlets' {
        $module = $script:PrivateGalleryTestModule
        $module.ExportedCmdlets.Keys | Should -Contain 'Connect-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Export-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Register-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Install-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Get-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Import-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Initialize-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'New-ModuleRepositoryBootstrap'
        $module.ExportedCmdlets.Keys | Should -Contain 'Set-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Remove-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Test-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Publish-NugetPackage'

        $manifest = Get-Content -LiteralPath $script:PrivateGalleryManifestPath -Raw
        $manifest | Should -Match "'New-ModuleRepositoryBootstrap'"
        $manifest | Should -Match "'New-GalleryBootstrap'"
        $manifest | Should -Match "'Initialize-Gallery'"
        $manifest | Should -Match "'Export-GalleryProfile'"
        $manifest | Should -Match "'Import-GalleryProfile'"
        $manifest | Should -Match "'Test-GalleryProfile'"
    }

    It 'keeps install/update wrapper parameter sets intact' {
        $install = Get-Command Install-PrivateModule -ErrorAction Stop
        $install.DefaultParameterSet | Should -Be 'Repository'
        $install.ParameterSets.Name | Should -Contain 'Repository'
        $install.ParameterSets.Name | Should -Contain 'AzureArtifacts'
        $install.ParameterSets.Name | Should -Contain 'MicrosoftArtifactRegistry'

        $update = Get-Command Update-PrivateModule -ErrorAction Stop
        $update.DefaultParameterSet | Should -Be 'Repository'
        $update.ParameterSets.Name | Should -Contain 'Repository'
        $update.ParameterSets.Name | Should -Contain 'AzureArtifacts'
        $update.ParameterSets.Name | Should -Contain 'MicrosoftArtifactRegistry'
        $update.ParameterSets.Name | Should -Contain 'Profile'
    }

    It 'offers onboarding-friendly aliases' {
        $module = $script:PrivateGalleryTestModule
        $connect = $module.ExportedCmdlets['Connect-ModuleRepository']
        $connect.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $connect.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $connect.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $connect.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $connect.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $connect.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $connect.Parameters.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $connect.Parameters['Repository'].ParameterSets.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $connect.ParameterSets.Name | Should -Contain 'MicrosoftArtifactRegistry'
        $connect.ParameterSets.Name | Should -Contain 'Profile'

        $register = $module.ExportedCmdlets['Register-ModuleRepository']
        $register.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $register.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $register.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $register.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $register.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $register.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $register.Parameters.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $register.Parameters.Keys | Should -Contain 'Repository'
        $register.Parameters['Repository'].ParameterSets.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $register.Parameters.Keys | Should -Contain 'RepositoryUri'
        $register.Parameters.Keys | Should -Contain 'JFrogBaseUri'
        $register.Parameters.Keys | Should -Contain 'JFrogRepository'
        $register.ParameterSets.Name | Should -Contain 'MicrosoftArtifactRegistry'
        $register.ParameterSets.Name | Should -Contain 'Profile'

        $updateRepository = $module.ExportedCmdlets['Update-ModuleRepository']
        $updateRepository.Parameters['Repository'].ParameterSets.Keys | Should -Contain 'MicrosoftArtifactRegistry'

        $install = $module.ExportedCmdlets['Install-PrivateModule']
        $install.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $install.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $install.Parameters['CredentialSecret'].Aliases | Should -Contain 'Token'
        $install.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $install.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $install.Parameters.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $install.Parameters.Keys | Should -Contain 'RepositoryUri'
        $install.Parameters.Keys | Should -Contain 'JFrogBaseUri'
        $install.Parameters.Keys | Should -Contain 'JFrogRepository'
        $install.ParameterSets.Name | Should -Contain 'Profile'

        $update = $module.ExportedCmdlets['Update-PrivateModule']
        $update.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $update.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $update.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $update.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $update.Parameters.Keys | Should -Contain 'MicrosoftArtifactRegistry'
        $update.Parameters.Keys | Should -Contain 'RepositoryUri'
        $update.Parameters.Keys | Should -Contain 'JFrogBaseUri'
        $update.Parameters.Keys | Should -Contain 'JFrogRepository'
        $update.ParameterSets.Name | Should -Contain 'Profile'

        $profile = $module.ExportedCmdlets['Set-ModuleRepositoryProfile']
        $profile.Parameters['Name'].Aliases | Should -Contain 'ProfileName'
        $profile.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $profile.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $profile.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $profile.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $profile.Parameters.Keys | Should -Contain 'Repository'
        $profile.Parameters.Keys | Should -Contain 'RepositoryUri'
        $profile.Parameters.Keys | Should -Contain 'JFrogBaseUri'
        $profile.Parameters.Keys | Should -Contain 'JFrogRepository'
        $profile.Parameters.Keys | Should -Contain 'GitHubOwner'
        $profile.Parameters['GitHubOwner'].Aliases | Should -Contain 'Owner'
        $profile.Parameters['GitHubOwner'].Aliases | Should -Contain 'Namespace'
        $profile.Parameters.Keys | Should -Contain 'Scope'

        $exportProfile = $module.ExportedCmdlets['Export-ModuleRepositoryProfile']
        $exportProfile.Parameters['Name'].Aliases | Should -Contain 'ProfileName'
        $exportProfile.Parameters.Keys | Should -Contain 'Scope'

        $importProfile = $module.ExportedCmdlets['Import-ModuleRepositoryProfile']
        $importProfile.Parameters.Keys | Should -Contain 'Overwrite'
        $importProfile.Parameters.Keys | Should -Contain 'Scope'

        $initialize = $module.ExportedCmdlets['Initialize-ModuleRepository']
        $initialize.ParameterSets.Name | Should -Contain 'Profile'
        $initialize.ParameterSets.Name | Should -Contain 'Import'
        $initialize.ParameterSets.Name | Should -Contain 'AzureArtifacts'
        $initialize.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'
        $initialize.Parameters['ProfileName'].Aliases | Should -Contain 'Name'
        $initialize.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $initialize.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $initialize.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $initialize.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $initialize.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $initialize.Parameters.Keys | Should -Contain 'SkipConnect'
        $initialize.Parameters.Keys | Should -Contain 'Repository'
        $initialize.Parameters.Keys | Should -Contain 'RepositoryUri'
        $initialize.Parameters.Keys | Should -Contain 'JFrogBaseUri'
        $initialize.Parameters.Keys | Should -Contain 'JFrogRepository'
        $initialize.Parameters.Keys | Should -Contain 'Scope'

        $bootstrap = $module.ExportedCmdlets['New-ModuleRepositoryBootstrap']
        $bootstrap.Parameters['ProfileName'].Aliases | Should -Contain 'Name'
        $bootstrap.Parameters['InstallModule'].Aliases | Should -Contain 'ModuleName'
        $bootstrap.Parameters.Keys | Should -Contain 'Scope'

        $testProfile = $module.ExportedCmdlets['Test-ModuleRepositoryProfile']
        $testProfile.Parameters['ProfileName'].Aliases | Should -Contain 'Name'
        $testProfile.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'
        $testProfile.Parameters.Keys | Should -Contain 'Scope'

        $publishPackage = $module.ExportedCmdlets['Publish-NugetPackage']
        $publishPackage.ParameterSets.Name | Should -Contain 'Profile'
        $publishPackage.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'
        $publishPackage.Parameters.Keys | Should -Contain 'InstallPrerequisites'
    }

    It 'saves Azure Artifacts profiles with Entra-first defaults' {
        $profile = Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules'

        $profile.Name | Should -Be 'Company'
        $profile.RepositoryName | Should -Be 'Modules'
        $profile.Priority | Should -Be 40
        $profile.Tool | Should -Be ([PowerForge.RepositoryRegistrationTool]::PSResourceGet)
        $profile.BootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
        Test-Path -LiteralPath $script:PrivateGalleryProfilePath | Should -BeTrue
    }

    It 'saves JFrog profiles with credential-prompt defaults' {
        $profile = Set-ModuleRepositoryProfile -Name 'JFrogCompany' -Provider JFrog -Repository 'powershell-virtual' -JFrogBaseUri 'https://company.jfrog.io/artifactory'

        $profile.Name | Should -Be 'JFrogCompany'
        $profile.Provider.ToString() | Should -Be 'JFrog'
        $profile.Repository | Should -Be 'powershell-virtual'
        $profile.RepositoryName | Should -Be 'powershell-virtual'
        $profile.RepositoryUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'
        $profile.RepositorySourceUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $profile.JFrogRepository | Should -Be 'powershell-virtual'
        $profile.BootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
        $profile.AuthenticationMode | Should -Be 'CredentialPrompt'
    }

    It 'saves GitHub Packages profiles with owner-scoped NuGet endpoints' {
        $profile = Set-ModuleRepositoryProfile -Name 'Licensing' -Provider GitHubPackages -GitHubOwner 'EvotecIT' -RepositoryName 'github-evotec'

        $profile.Name | Should -Be 'Licensing'
        $profile.Provider.ToString() | Should -Be 'GitHubPackages'
        $profile.GitHubOwner | Should -Be 'EvotecIT'
        $profile.Repository | Should -Be 'EvotecIT'
        $profile.RepositoryName | Should -Be 'github-evotec'
        $profile.RepositoryUri | Should -Be 'https://nuget.pkg.github.com/EvotecIT/index.json'
        $profile.RepositorySourceUri | Should -Be $profile.RepositoryUri
        $profile.RepositoryPublishUri | Should -Be $profile.RepositoryUri
        $profile.BootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
        $profile.AuthenticationMode | Should -Be 'CredentialPrompt'
    }

    It 'resolves machine-wide profiles for other users without sharing credentials' {
        Set-ModuleRepositoryProfile -Name 'CompanyMachine' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -Scope Machine | Out-Null

        Test-Path -LiteralPath $script:PrivateGalleryMachineProfilePath | Should -BeTrue
        Test-Path -LiteralPath $script:PrivateGalleryProfilePath | Should -BeTrue

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyMachine'
        $profile.Name | Should -Be 'CompanyMachine'
        $profile.Scope | Should -Be ([PowerForge.ModuleRepositoryProfileScope]::Machine)
        $profile.ProfileStorePath | Should -Be $script:PrivateGalleryMachineProfilePath
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'

        $readiness = Test-ModuleRepositoryProfile -ProfileName 'CompanyMachine'
        $readiness.ProfileFound | Should -BeTrue
        $readiness.Scope | Should -Be ([PowerForge.ModuleRepositoryProfileScope]::Machine)
        $readiness.ProfileStorePath | Should -Be $script:PrivateGalleryMachineProfilePath

        $userProfile = Set-ModuleRepositoryProfile -Name 'CompanyMachine' -AzureDevOpsOrganization 'fabrikam' -AzureArtifactsFeed 'UserModules'
        $userProfile.Scope | Should -Be ([PowerForge.ModuleRepositoryProfileScope]::User)

        $resolved = Get-ModuleRepositoryProfile -Name 'CompanyMachine'
        $resolved.Scope | Should -Be ([PowerForge.ModuleRepositoryProfileScope]::User)
        $resolved.AzureDevOpsOrganization | Should -Be 'fabrikam'
        $resolved.ProfileStorePath | Should -Be $script:PrivateGalleryProfilePath
    }

    It 'exports and imports non-secret managed profile files' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'Company.profile.json'

        $exported = Export-ModuleRepositoryProfile -Name 'Company' -Path $exportPath -Force -PassThru
        $json = Get-Content -LiteralPath $exportPath -Raw

        $exported.Name | Should -Be 'Company'
        $json | Should -Match '"Profiles"'
        $json | Should -Not -Match '"Secret"'
        $json | Should -Not -Match '"Password"'
        $json | Should -Not -Match '"Token"'

        Remove-ModuleRepositoryProfile -Name 'Company'
        Get-ModuleRepositoryProfile -Name 'Company' -ErrorAction SilentlyContinue | Should -BeNullOrEmpty

        $imported = Import-ModuleRepositoryProfile -Path $exportPath

        $imported.Name | Should -Be 'Company'
        $profile = Get-ModuleRepositoryProfile -Name 'Company'
        $profile.AzureDevOpsOrganization | Should -Be 'contoso'
        $profile.AzureDevOpsProject | Should -Be 'Platform'
        $profile.AzureArtifactsFeed | Should -Be 'Modules'
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
    }

    It 'creates non-secret managed workstation bootstrap packages' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $outputDirectory = Join-Path $script:PrivateGalleryProfileRoot 'bootstrap'

        $package = New-ModuleRepositoryBootstrap -ProfileName 'Company' -OutputDirectory $outputDirectory -InstallModule 'ModuleA' -Force

        $package | Should -Not -BeNullOrEmpty
        $package.ProfileNames | Should -Contain 'Company'
        $package.InstallModules | Should -Contain 'ModuleA'
        $package.ContainsSecrets | Should -BeFalse
        $package.RecommendedCommand | Should -Be ".\Initialize-PrivateGallery.ps1 -ProfileName 'Company'"
        Test-Path -LiteralPath $package.ProfilePath -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath $package.ScriptPath -PathType Leaf | Should -BeTrue

        $profileJson = Get-Content -LiteralPath $package.ProfilePath -Raw
        $profileJson | Should -Match '"Name": "Company"'
        $profileJson | Should -Not -Match '"Secret"'
        $profileJson | Should -Not -Match '"Password"'
        $profileJson | Should -Not -Match '"Token"'

        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($package.ScriptPath, [ref] $tokens, [ref] $errors) | Out-Null
        $errors | Should -BeNullOrEmpty

        $global:BootstrapInitializeArguments = $null
        $global:BootstrapInstallArguments = $null
        function global:Initialize-ModuleRepository {
            param(
                [string] $Path,
                [string] $ProfileName,
                [switch] $Overwrite,
                [switch] $InstallPrerequisites,
                [switch] $SkipConnect
            )

            $global:BootstrapInitializeArguments = $PSBoundParameters
            [pscustomobject]@{ ProfileName = $ProfileName }
        }

        function global:Install-PrivateModule {
            param(
                [string] $ProfileName,
                [string[]] $Name,
                [switch] $InstallPrerequisites
            )

            $global:BootstrapInstallArguments = $PSBoundParameters
            [pscustomobject]@{ ProfileName = $ProfileName; Name = $Name }
        }

        try {
            & $package.ScriptPath -SkipInstallPrerequisites | Out-Null

            $global:BootstrapInitializeArguments.Path | Should -Be $package.ProfilePath
            $global:BootstrapInitializeArguments.ProfileName | Should -Be 'Company'
            $global:BootstrapInitializeArguments.Overwrite.IsPresent | Should -BeTrue
            $global:BootstrapInitializeArguments.ContainsKey('InstallPrerequisites') | Should -BeFalse
            $global:BootstrapInstallArguments.ProfileName | Should -Be 'Company'
            $global:BootstrapInstallArguments.Name | Should -Contain 'ModuleA'
            $global:BootstrapInstallArguments.InstallPrerequisites.IsPresent | Should -BeFalse
        } finally {
            Remove-Item Function:\Initialize-ModuleRepository -ErrorAction SilentlyContinue
            Remove-Item Function:\Install-PrivateModule -ErrorAction SilentlyContinue
            Remove-Variable -Name BootstrapInitializeArguments -Scope Global -ErrorAction SilentlyContinue
            Remove-Variable -Name BootstrapInstallArguments -Scope Global -ErrorAction SilentlyContinue
        }
    }

    It 'requires overwrite when importing an existing managed profile' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'Company.overwrite.profile.json'
        Export-ModuleRepositoryProfile -Name 'Company' -Path $exportPath -Force

        {
            Import-ModuleRepositoryProfile -Path $exportPath
        } | Should -Throw "*already exists*"

        $imported = Import-ModuleRepositoryProfile -Path $exportPath -Overwrite

        $imported.Name | Should -Be 'Company'
    }

    It 'initializes a new Azure Artifacts profile without connecting when requested' {
        $result = Initialize-ModuleRepository -Name 'CompanyInit' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -SkipConnect

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryOnboardingResult'
        $result.ProfileName | Should -Be 'CompanyInit'
        $result.ProfileFound | Should -BeTrue
        $result.ProfileWritten | Should -BeTrue
        $result.ConnectAttempted | Should -BeFalse
        $result.ConnectSkipped | Should -BeTrue
        $result.Succeeded | Should -BeTrue
        $result.Profile.RepositoryName | Should -Be 'CompanyInit'
        $result.Profile.Priority | Should -Be 40
        $result.Readiness.RepositoryName | Should -Be 'CompanyInit'
        $result.Readiness.Priority | Should -Be 40
        $result.RecommendedInstallCommand | Should -Be "Install-PrivateModule -ProfileName 'CompanyInit' -Name <ModuleName>"
        $result.RecommendedUpdateCommand | Should -Be "Update-PrivateModule -ProfileName 'CompanyInit' -Name <ModuleName>"

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyInit'
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
    }

    It 'initializes a new Azure Artifacts profile using the canonical ProfileName parameter' {
        $result = Initialize-ModuleRepository -ProfileName 'CompanyCanonical' -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -SkipConnect

        $result.ProfileName | Should -Be 'CompanyCanonical'
        $result.ProfileWritten | Should -BeTrue
        $result.Profile.AzureDevOpsOrganization | Should -Be 'contoso'

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyCanonical'
        $profile.RepositoryName | Should -Be 'CompanyCanonical'
        $profile.Priority | Should -Be 40
    }

    It 'initializes from a managed profile file in one command without connecting when requested' {
        Set-ModuleRepositoryProfile -Name 'CompanyFile' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'CompanyFile.profile.json'
        Export-ModuleRepositoryProfile -Name 'CompanyFile' -Path $exportPath -Force
        Remove-ModuleRepositoryProfile -Name 'CompanyFile'

        $result = Initialize-ModuleRepository -Path $exportPath -ProfileName 'CompanyFile' -Overwrite -SkipConnect

        $result.ProfileName | Should -Be 'CompanyFile'
        $result.ProfileWritten | Should -BeTrue
        $result.ImportedFromPath | Should -Be $exportPath
        $result.ConnectAttempted | Should -BeFalse
        $result.ConnectSkipped | Should -BeTrue
        $result.Profile.AzureDevOpsProject | Should -Be 'Platform'

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyFile'
        $profile.AzureArtifactsFeed | Should -Be 'Modules'
    }

    It 'initializes a managed profile file with WhatIf without writing or probing' {
        Set-ModuleRepositoryProfile -Name 'CompanyWhatIf' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'CompanyWhatIf.profile.json'
        Export-ModuleRepositoryProfile -Name 'CompanyWhatIf' -Path $exportPath -Force
        Remove-ModuleRepositoryProfile -Name 'CompanyWhatIf'

        $result = Initialize-ModuleRepository -Path $exportPath -ProfileName 'CompanyWhatIf' -Overwrite -InstallPrerequisites -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.ProfileName | Should -Be 'CompanyWhatIf'
        $result.ProfileWritten | Should -BeFalse
        $result.ConnectAttempted | Should -BeFalse
        $result.ConnectSkipped | Should -BeTrue
        $result.Succeeded | Should -BeTrue
        $result.Connection | Should -BeNullOrEmpty
        Get-ModuleRepositoryProfile -Name 'CompanyWhatIf' -ErrorAction SilentlyContinue | Should -BeNullOrEmpty
    }

    It 'tests saved profile readiness without registering repositories' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $result = Test-ModuleRepositoryProfile -ProfileName 'Company'

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryProfileReadinessResult'
        $result.Name | Should -Be 'Company'
        $result.ProfileFound | Should -BeTrue
        $result.RepositoryName | Should -Be 'Modules'
        $result.PSResourceGetUri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $result.PowerShellGetSourceUri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2'
        $result.ProfileStorePath | Should -Be $script:PrivateGalleryProfilePath
        $result.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
        $result.RecommendedConnectCommand | Should -Match "Connect-ModuleRepository -ProfileName 'Company'"
        $result.RecommendedOnboardingCommand | Should -Match "Initialize-ModuleRepository -ProfileName 'Company'"
        $result.RecommendedInstallCommand | Should -Be "Install-PrivateModule -Name <ModuleName> -ProfileName 'Company'"
        $result.ReadinessMessages | Should -Not -BeNullOrEmpty
    }

    It 'returns a non-terminating readiness object for a missing profile' {
        $result = Test-ModuleRepositoryProfile -ProfileName 'MissingCompany'

        $result | Should -Not -BeNullOrEmpty
        $result.Name | Should -Be 'MissingCompany'
        $result.ProfileFound | Should -BeFalse
        $result.IsReady | Should -BeFalse
        $result.ProfileStorePath | Should -Be $script:PrivateGalleryProfilePath
        $result.ReadinessMessages | Should -Contain "Module repository profile 'MissingCompany' was not found. Create or import it with Initialize-ModuleRepository before installing, updating, or publishing."
    }

    It 'treats an ExistingSession profile as not ready when PSResourceGet is below the Entra bootstrap version' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryProfileReadinessResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.ProfileFound = $true
        $result.BootstrapMode = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $result.PSResourceGetSupportsExistingSessionBootstrap = $false
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.CredentialPromptBootstrapReady = $true

        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.ExistingSessionBootstrapReady | Should -BeFalse
        $result.IsReady | Should -BeFalse

        $result.BootstrapMode = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt
        $result.IsReady | Should -BeTrue
    }

    It 'uses saved profiles for WhatIf repository connection without prompting' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $result = Connect-ModuleRepository -ProfileName 'Company' -InstallPrerequisites -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.RepositoryName | Should -Be 'Modules'
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $result.RegistrationPerformed | Should -BeFalse
    }

    It 'uses saved profiles for Azure Artifacts publish configuration' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $publish = New-ConfigurationPublish -ProfileName 'Company' -Enabled

        $publish.Configuration.RepositoryName | Should -Be 'Modules'
        $publish.Configuration.Tool | Should -Be ([PowerForge.PublishTool]::PSResourceGet)
        $publish.Configuration.Repository.Uri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $publish.Configuration.Repository.Credential | Should -BeNullOrEmpty
    }

    It 'uses saved JFrog profiles with repository credentials for publish configuration' {
        Set-ModuleRepositoryProfile -Name 'JFrogCompany' -Provider JFrog -Repository 'powershell-virtual' -JFrogBaseUri 'https://company.jfrog.io/artifactory' | Out-Null

        $publish = New-ConfigurationPublish -ProfileName 'JFrogCompany' -RepositoryCredentialUserName 'publisher' -RepositoryCredentialSecret 'token' -Enabled

        $publish.Configuration.RepositoryName | Should -Be 'powershell-virtual'
        $publish.Configuration.Tool | Should -Be ([PowerForge.PublishTool]::PSResourceGet)
        $publish.Configuration.Repository.Uri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'
        $publish.Configuration.Repository.SourceUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $publish.Configuration.Repository.PublishUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $publish.Configuration.Repository.Credential.UserName | Should -Be 'publisher'
        $publish.Configuration.Repository.Credential.Secret | Should -Be 'token'
    }

    It 'uses direct JFrog parameters with a clear-text repository PAT without requiring FilePath' {
        $publish = New-ConfigurationPublish -Type PowerShellGallery -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryCredentialUserName 'publisher' -RepositoryCredentialSecret 'token' -Enabled

        $publish.Configuration.ApiKey | Should -Be ''
        $publish.Configuration.RepositoryName | Should -Be 'powershell-virtual'
        $publish.Configuration.Tool | Should -Be ([PowerForge.PublishTool]::Auto)
        $publish.Configuration.Repository.Uri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'
        $publish.Configuration.Repository.SourceUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $publish.Configuration.Repository.PublishUri | Should -Be 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $publish.Configuration.Repository.Credential.UserName | Should -Be 'publisher'
        $publish.Configuration.Repository.Credential.Secret | Should -Be 'token'
    }

    It 'keeps direct JFrog parameters composable with repository URI overrides' {
        $publish = New-ConfigurationPublish -Type PowerShellGallery -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryUri 'https://custom.example/v3/index.json' -RepositorySourceUri 'https://custom.example/v2/source' -RepositoryPublishUri 'https://custom.example/v2/publish' -RepositoryCredentialUserName 'publisher' -RepositoryCredentialSecret 'token' -Enabled

        $publish.Configuration.Repository.Uri | Should -Be 'https://custom.example/v3/index.json'
        $publish.Configuration.Repository.SourceUri | Should -Be 'https://custom.example/v2/source'
        $publish.Configuration.Repository.PublishUri | Should -Be 'https://custom.example/v2/publish'
        $publish.Configuration.Repository.Credential.UserName | Should -Be 'publisher'
        $publish.Configuration.Repository.Credential.Secret | Should -Be 'token'
    }

    It 'requires publish auth when enabling saved JFrog profiles for publish configuration' {
        Set-ModuleRepositoryProfile -Name 'JFrogCompanyNoAuth' -Provider JFrog -Repository 'powershell-virtual' -JFrogBaseUri 'https://company.jfrog.io/artifactory' | Out-Null

        { New-ConfigurationPublish -ProfileName 'JFrogCompanyNoAuth' -Enabled } | Should -Throw '*ApiKey, FilePath, or repository credentials are required*'
    }

    It 'uses saved profiles for Azure Artifacts NuGet package publishing' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $packageRoot = Join-Path $script:PrivateGalleryProfileRoot 'packages'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $packagePath = Join-Path $packageRoot 'Company.Tools.1.0.0.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'placeholder' -NoNewline

        $result = Publish-NugetPackage -Path $packageRoot -ProfileName 'Company' -InstallPrerequisites -SkipDuplicate -WhatIf

        $result | Should -Not -BeNullOrEmpty
        $result.Success | Should -BeTrue
        $result.ProfileName | Should -Be 'Company'
        $result.RepositoryName | Should -Be 'Modules'
        $result.Source | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $result.Pushed | Should -Contain $packagePath
        $result.Failed | Should -BeNullOrEmpty
    }

    It 'uses GitHub token environment variables for GitHub Packages NuGet publishing' {
        Set-ModuleRepositoryProfile -Name 'LicensingPackages' -Provider GitHubPackages -GitHubOwner 'EvotecIT' -RepositoryName 'github-evotec' | Out-Null
        $packageRoot = Join-Path $script:PrivateGalleryProfileRoot 'github-packages'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $packagePath = Join-Path $packageRoot 'Licensing.Verification.1.0.0.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'placeholder' -NoNewline
        $previousToken = $env:GITHUB_TOKEN
        $previousGhToken = $env:GH_TOKEN

        try {
            $env:GITHUB_TOKEN = 'test-token'
            Remove-Item Env:\GH_TOKEN -ErrorAction SilentlyContinue

            $result = Publish-NugetPackage -Path $packageRoot -ProfileName 'LicensingPackages' -SkipDuplicate -WhatIf

            $result.Success | Should -BeTrue
            $result.ProfileName | Should -Be 'LicensingPackages'
            $result.RepositoryName | Should -Be 'github-evotec'
            $result.Source | Should -Be 'https://nuget.pkg.github.com/EvotecIT/index.json'
            $result.Pushed | Should -Contain $packagePath
            $result.Failed | Should -BeNullOrEmpty
        } finally {
            if ($null -eq $previousToken) {
                Remove-Item Env:\GITHUB_TOKEN -ErrorAction SilentlyContinue
            } else {
                $env:GITHUB_TOKEN = $previousToken
            }

            if ($null -eq $previousGhToken) {
                Remove-Item Env:\GH_TOKEN -ErrorAction SilentlyContinue
            } else {
                $env:GH_TOKEN = $previousGhToken
            }
        }
    }

    It 'requires an API key when publishing packages to saved JFrog profiles' {
        Set-ModuleRepositoryProfile -Name 'JFrogPackagePublish' -Provider JFrog -Repository 'powershell-virtual' -JFrogBaseUri 'https://company.jfrog.io/artifactory' | Out-Null
        $packageRoot = Join-Path $script:PrivateGalleryProfileRoot 'jfrog-packages'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $packagePath = Join-Path $packageRoot 'Company.Tools.1.0.0.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'placeholder' -NoNewline

        $result = Publish-NugetPackage -Path $packageRoot -ProfileName 'JFrogPackagePublish' -SkipDuplicate -WhatIf

        $result.Success | Should -BeFalse
        $result.ErrorMessage | Should -BeLike '*ApiKey is required*JFrog*'
    }

    It 'reports readiness information on the registration result type' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureDevOpsProject = 'Platform'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetRegistered = $true
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.2.0'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $true)
        $result.PowerShellGetAvailable = $true
        $result.PowerShellGetVersion = '2.2.5'
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.AzureArtifactsCredentialProviderVersion = '2.0.312'
        $type.GetProperty('AccessProbePerformed').SetValue($result, $true)
        $type.GetProperty('AccessProbeSucceeded').SetValue($result, $true)
        $type.GetProperty('AccessProbeTool').SetValue($result, 'PSResourceGet')
        $type.GetProperty('AccessProbeMessage').SetValue($result, 'Repository access probe completed successfully via PSResourceGet.')
        $type.GetProperty('CredentialProviderSessionPrimeAttempted').SetValue($result, $true)
        $type.GetProperty('CredentialProviderSessionPrimeSucceeded').SetValue($result, $true)
        $type.GetProperty('CredentialProviderSessionPrimePath').SetValue($result, 'CredentialProvider.Microsoft.exe')
        $type.GetProperty('CredentialProviderSessionPrimeMessage').SetValue($result, 'Azure Artifacts Credential Provider session priming completed successfully.')
        $result.InstalledPrerequisites = @('PSResourceGet')
        $result.PrerequisiteInstallMessages = @('PSResourceGet prerequisite handled via PowerShellGet (Installed).')
        $result.ToolRequested = [PowerForge.RepositoryRegistrationTool]::Auto
        $result.ToolUsed = [PowerForge.RepositoryRegistrationTool]::PSResourceGet
        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::Auto
        $result.BootstrapModeUsed = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession
        $result.CredentialSource = [PowerForge.PrivateGalleryCredentialSource]::None
        $installPrerequisitesRecommended = $type.GetProperty('InstallPrerequisitesRecommended').GetValue($result)

        $result.ExistingSessionBootstrapReady | Should -BeTrue
        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $installPrerequisitesRecommended | Should -BeFalse
        $result.RecommendedBootstrapCommand | Should -Be "Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -Name 'Company' -BootstrapMode ExistingSession"
        $result.InstallPSResourceReady | Should -BeTrue
        $result.InstallModuleReady | Should -BeFalse
        $result.ReadyCommands | Should -Contain 'Install-PSResource'
        $result.PreferredInstallCommand | Should -Be 'Install-PSResource'
        $result.RecommendedWrapperInstallCommand | Should -Be "Install-PrivateModule -Name <ModuleName> -Repository 'Company'"
        $result.RecommendedNativeInstallCommand | Should -Be "Install-PSResource -Name <ModuleName> -Repository 'Company'"
        $result.ToolRequested | Should -Be ([PowerForge.RepositoryRegistrationTool]::Auto)
        $result.ToolUsed | Should -Be ([PowerForge.RepositoryRegistrationTool]::PSResourceGet)
        $result.PSResourceGetVersion | Should -Be '1.2.0'
        $result.PSResourceGetMeetsMinimumVersion | Should -BeTrue
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').GetValue($result) | Should -BeTrue
        $result.PowerShellGetVersion | Should -Be '2.2.5'
        $result.AzureArtifactsCredentialProviderVersion | Should -Be '2.0.312'
        $result.BootstrapModeRequested | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::Auto)
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $result.CredentialSource | Should -Be ([PowerForge.PrivateGalleryCredentialSource]::None)
        $type.GetProperty('AccessProbePerformed').GetValue($result) | Should -BeTrue
        $type.GetProperty('AccessProbeSucceeded').GetValue($result) | Should -BeTrue
        $type.GetProperty('AccessProbeTool').GetValue($result) | Should -Be 'PSResourceGet'
        $type.GetProperty('AccessProbeMessage').GetValue($result) | Should -Be 'Repository access probe completed successfully via PSResourceGet.'
        $type.GetProperty('CredentialProviderSessionPrimeAttempted').GetValue($result) | Should -BeTrue
        $type.GetProperty('CredentialProviderSessionPrimeSucceeded').GetValue($result) | Should -BeTrue
        $type.GetProperty('CredentialProviderSessionPrimePath').GetValue($result) | Should -Be 'CredentialProvider.Microsoft.exe'
        $type.GetProperty('CredentialProviderSessionPrimeMessage').GetValue($result) | Should -Be 'Azure Artifacts Credential Provider session priming completed successfully.'
        $result.InstalledPrerequisites | Should -Contain 'PSResourceGet'
        $result.PrerequisiteInstallMessages | Should -Contain 'PSResourceGet prerequisite handled via PowerShellGet (Installed).'
    }

    It 'includes PowerShellGet v2 URIs in private gallery bootstrap recommendations' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.Provider = 'JFrog'
        $result.RepositoryName = 'JFrogCompany'
        $result.AzureArtifactsFeed = 'powershell-virtual'
        $result.PSResourceGetUri = 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'
        $result.PowerShellGetSourceUri = 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $result.PowerShellGetPublishUri = 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetMeetsMinimumVersion = $true
        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt
        $result.BootstrapModeUsed = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt

        $result.RecommendedBootstrapCommand | Should -Match "-RepositoryUri 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'"
        $result.RecommendedBootstrapCommand | Should -Match "-RepositorySourceUri 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'"
        $result.RecommendedBootstrapCommand | Should -Not -Match '-RepositoryPublishUri'
    }

    It 'recommends prerequisite installation when bootstrap dependencies are missing or outdated' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureDevOpsProject = 'Platform'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.0.9'
        $result.PSResourceGetMeetsMinimumVersion = $false
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $false
        $result.ToolRequested = [PowerForge.RepositoryRegistrationTool]::PSResourceGet
        $installPrerequisitesRecommended = $type.GetProperty('InstallPrerequisitesRecommended').GetValue($result)

        $installPrerequisitesRecommended | Should -BeTrue
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
        $result.RecommendedBootstrapCommand | Should -Be "Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -Name 'Company' -InstallPrerequisites -BootstrapMode CredentialPrompt -Interactive"
    }

    It 'prefers Install-Module when PSResourceGet cannot support ExistingSession bootstrap' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetRegistered = $true
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetRegistered = $true
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true

        $result.ExistingSessionBootstrapReady | Should -BeFalse
        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.InstallPSResourceReady | Should -BeFalse
        $result.InstallModuleReady | Should -BeTrue
        $result.PreferredInstallCommand | Should -Be 'Install-Module'
        $result.RecommendedNativeInstallCommand | Should -Be "Install-Module -Name <ModuleName> -Repository 'Company'"
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }

    It 'recommends prerequisite installation for Entra-first bootstrap when PSResourceGet is too old' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession

        $result.InstallPrerequisitesRecommended | Should -BeTrue
        $result.RecommendedBootstrapCommand | Should -Match '-InstallPrerequisites'

        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt
        $result.InstallPrerequisitesRecommended | Should -BeFalse
    }

    It 'rejects PSGallery as an Azure Artifacts repository name' {
        {
            New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -RepositoryName 'PSGallery' -Enabled
        } | Should -Throw "*RepositoryName cannot be 'PSGallery' when using the Azure Artifacts preset.*"
    }

    It 'rejects Azure Artifacts feeds that resolve to PSGallery' {
        {
            New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'PSGallery' -Enabled
        } | Should -Throw "*RepositoryName cannot be 'PSGallery' when using the Azure Artifacts preset.*"
    }

    It 'returns a registration result for Connect-ModuleRepository -WhatIf' {
        $result = Connect-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode ExistingSession -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
    }

    It 'does not prompt for credentials during Register-ModuleRepository -WhatIf' {
        $result = Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode CredentialPrompt -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }

    It 'does not prompt for credentials during Update-ModuleRepository -WhatIf' {
        $result = Update-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode CredentialPrompt -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }
}
