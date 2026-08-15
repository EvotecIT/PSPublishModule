[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Plan', 'Prepare', 'Publish')]
    [string] $Operation,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCommit,

    [string] $Confirm,

    [string] $ConfigPath,

    [string] $ReceiptPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge.PublicRelease\powerforge-public-release.json'
}
$ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)
$releaseReceiptRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'release-receipts'))
$repositoryUri = [Uri] ($repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
$releaseReceiptUri = [Uri] ($releaseReceiptRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
$receiptUri = [Uri] $ReceiptPath
if ($repositoryUri.IsBaseOf($receiptUri) -and -not $releaseReceiptUri.IsBaseOf($receiptUri)) {
    throw 'ReceiptPath must stay outside the release checkout or under its dedicated release-receipts directory.'
}
$receiptDirectory = Split-Path -Parent $ReceiptPath

$releaseStage = 'Preflight'
$actualCommit = $null
$releaseOutput = $null
$effectiveConfigPath = $null
$releaseRecovery = $null
$moduleProvenancePath = $null
$moduleProvenanceCreated = $false
$moduleSignedProvenancePath = $null
$moduleSignedProvenanceCreated = $false
$sourceDirty = $true
$receiptInitialized = $false

try {
    if (-not $IsWindows) {
        throw 'The public PSPublishModule release must run on Windows because its signing certificate is stored in the Windows certificate store.'
    }

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = Join-Path $PSScriptRoot 'release.json'
    }
    $ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
    if ([IO.Path]::GetFileName($ConfigPath) -match '^\.release\.authorized\.') {
        throw 'ConfigPath must identify a caller-owned source configuration, not a generated authorized configuration.'
    }
    $retainedCheckoutConfigPath = Join-Path `
        (Split-Path -Parent $ConfigPath) `
        ".release.authorized.$Version.$($ExpectedCommit.ToLowerInvariant()).json"
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'New-PowerForgeReleaseEvidenceWorkspace.ps1')
    $effectiveConfigDirectory = New-PowerForgeReleaseEvidenceWorkspace -RepositoryRoot $repositoryRoot
    $effectiveConfigPath = Join-Path $effectiveConfigDirectory ".release.authorized.$Version.$($ExpectedCommit.ToLowerInvariant()).json"

    $moduleProvenancePath = Join-Path $repositoryRoot 'Module\PowerForge.ReleaseProvenance.json'
    $moduleSignedProvenancePath = Join-Path $repositoryRoot 'Module\PowerForge.ReleaseProvenance.psd1'
    $generatedProvenancePaths = @(if ($Operation -eq 'Publish') {
        $moduleProvenancePath
        $moduleSignedProvenancePath
    })
    $sourceReleaseConfig = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json -Depth 100
    $explicitInputPaths = @($ConfigPath)
    $sourceTools = $sourceReleaseConfig.PSObject.Properties['Tools']
    if ($null -ne $sourceTools -and $null -ne $sourceTools.Value) {
        $sourcePublishConfig = $sourceTools.Value.PSObject.Properties['DotNetPublishConfigPath']
        if ($null -ne $sourcePublishConfig -and
            -not [string]::IsNullOrWhiteSpace([string] $sourcePublishConfig.Value)) {
            $sourcePublishConfigPath = [string] $sourcePublishConfig.Value
            if (-not [IO.Path]::IsPathRooted($sourcePublishConfigPath)) {
                $sourcePublishConfigPath = Join-Path (Split-Path -Parent $ConfigPath) $sourcePublishConfigPath
            }
            $explicitInputPaths += [IO.Path]::GetFullPath($sourcePublishConfigPath)
        }
    }
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Get-PowerForgeReleaseSourceState.ps1')
    $sourceState = Get-PowerForgeReleaseSourceState `
        -RepositoryRoot $repositoryRoot `
        -GeneratedProvenancePath $generatedProvenancePaths `
        -ReceiptPath $ReceiptPath `
        -GeneratedConfigurationPath $retainedCheckoutConfigPath `
        -ExplicitInputPath $explicitInputPaths
    $sourceDirty = [bool] $sourceState.SourceDirty
    if ($sourceDirty) {
        throw "The release checkout must start clean. Tracked or untracked changes: $(@($sourceState.Changes) -join ', ')"
    }

    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Test-PowerForgeTrackedReleaseReceipt.ps1')
    $receiptIsTracked = Test-PowerForgeTrackedReleaseReceipt `
        -RepositoryRoot $repositoryRoot `
        -ReceiptPath $ReceiptPath
    if ($receiptIsTracked) {
        throw 'ReceiptPath must not identify a tracked repository file.'
    }
    if (Test-Path -LiteralPath $ReceiptPath) {
        Remove-Item -LiteralPath $ReceiptPath -Force
    }
    New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
    $receiptInitialized = $true

    $actualCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ine $ExpectedCommit) {
        throw "Expected release commit '$ExpectedCommit', received '$actualCommit'."
    }

    $releaseConfig = $sourceReleaseConfig
    $moduleConfigPath = Join-Path $repositoryRoot 'powerforge.json'
    $moduleConfig = Get-Content -Raw -LiteralPath $moduleConfigPath | ConvertFrom-Json -Depth 100
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Set-PowerForgeAuthorizedReleaseVersion.ps1')
    $releaseConfig = Set-PowerForgeAuthorizedReleaseVersion `
        -ReleaseConfig $releaseConfig `
        -Version $Version `
        -DisableVersionUpdates:($Operation -eq 'Publish')
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Resolve-PowerForgeEffectiveConfigurationReferences.ps1')
    $releaseConfig = Resolve-PowerForgeEffectiveConfigurationReferences `
        -ReleaseConfig $releaseConfig `
        -SourceConfigurationPath $ConfigPath `
        -EvidenceDirectory $effectiveConfigDirectory
    $releaseConfig.GitHub | Add-Member -NotePropertyName Commitish -NotePropertyValue $ExpectedCommit -Force
    $certificateThumbprint = [string] $releaseConfig.Packages.CertificateThumbprint
    $certificateStore = [string] $releaseConfig.Packages.CertificateStore
    if ([string]::IsNullOrWhiteSpace($certificateThumbprint) -or [string]::IsNullOrWhiteSpace($certificateStore)) {
        throw 'Build/release.json must configure the package signing certificate thumbprint and store.'
    }
    $certificatePath = "Cert:\$certificateStore\My\$certificateThumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw "Signing certificate '$certificateThumbprint' with a private key was not found in $certificateStore\My."
    }
    if ($certificate.NotAfter -le [DateTime]::UtcNow.AddDays(7)) {
        throw "Signing certificate '$certificateThumbprint' expires on $($certificate.NotAfter.ToUniversalTime().ToString('O'))."
    }

    $moduleGalleryPublish = @($moduleConfig.Segments) |
        Where-Object { $_.Type -eq 'GalleryNuget' -and $_.Configuration.Enabled -eq $true } |
        Select-Object -First 1
    $credentialPaths = @(
        [string] $releaseConfig.Packages.PublishApiKeyFilePath
        [string] $releaseConfig.Packages.GitHubAccessTokenFilePath
        [string] $releaseConfig.GitHub.TokenFilePath
        [string] $moduleGalleryPublish.Configuration.ApiKeyFilePath
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($credentialPath in $credentialPaths) {
        $credentialFile = Get-Item -LiteralPath $credentialPath -ErrorAction SilentlyContinue
        if ($null -eq $credentialFile -or $credentialFile.Length -le 0) {
            throw "Required release credential file is missing or empty: $credentialPath"
        }
    }

    if ($Operation -eq 'Publish') {
        . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Assert-PowerForgeCommittedReleaseVersion.ps1')
        Assert-PowerForgeCommittedReleaseVersion -RepositoryRoot $repositoryRoot -Version $Version -ReleaseConfig $releaseConfig

        $gitHubTokenPath = [string] $releaseConfig.GitHub.TokenFilePath
        if ([string]::IsNullOrWhiteSpace($gitHubTokenPath)) {
            throw 'Build/release.json must configure the unified GitHub release token file.'
        }
        $gitHubToken = (Get-Content -Raw -LiteralPath $gitHubTokenPath).Trim()
        . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Get-PowerForgeReleasePackageIds.ps1')
        $packageIds = Get-PowerForgeReleasePackageIds `
            -ReleaseConfig $releaseConfig `
            -RepositoryRoot $repositoryRoot
        . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Enable-PowerForgeVerifiedGitHubReleaseRecovery.ps1')
        $releaseRecovery = Enable-PowerForgeVerifiedGitHubReleaseRecovery `
            -ReleaseConfig $releaseConfig `
            -Version $Version `
            -ExpectedCommit $ExpectedCommit `
            -Token $gitHubToken `
            -PackageIds $packageIds `
            -NuGetSource ([string] $releaseConfig.Packages.PublishSource) `
            -ModuleName ([string] $releaseConfig.Module.ModuleName)

        $expectedConfirmation = "publish:$Version`:$ExpectedCommit"
        if ($Confirm -cne $expectedConfirmation) {
            throw "Publish confirmation must exactly equal '$expectedConfirmation'."
        }

        if (Test-Path -LiteralPath $moduleProvenancePath) {
            throw "Refusing to overwrite existing module release provenance: $moduleProvenancePath"
        }
        [ordered]@{
            schemaVersion = 1
            moduleName    = [string] $releaseConfig.Module.ModuleName
            version       = $Version
            repository    = "https://github.com/$([string] $releaseConfig.GitHub.Owner)/$([string] $releaseConfig.GitHub.Repository)"
            commit        = $ExpectedCommit
            sourceDirty   = $sourceDirty
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $moduleProvenancePath -Encoding utf8BOM
        $moduleProvenanceCreated = $true
        if (Test-Path -LiteralPath $moduleSignedProvenancePath) {
            throw "Refusing to overwrite existing signed module release provenance: $moduleSignedProvenancePath"
        }
        @"
@{
    SchemaVersion = '1'
    ModuleName = '$([string] $releaseConfig.Module.ModuleName)'
    Version = '$Version'
    SourceRevision = '$($ExpectedCommit.ToLowerInvariant())'
    SourceDirty = 'false'
}
"@ | Set-Content -LiteralPath $moduleSignedProvenancePath -Encoding utf8
        $moduleSignedProvenanceCreated = $true
    }

    New-Item -ItemType Directory -Path $effectiveConfigDirectory -Force | Out-Null
    $releaseConfig | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $effectiveConfigPath -Encoding utf8
    $effectiveConfigSha256 = (Get-FileHash -LiteralPath $effectiveConfigPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $releaseStage = 'Build'
    $buildScript = Join-Path $PSScriptRoot 'Build-Project.ps1'
    $buildParameters = @{
        ModuleVersion = $Version
        ConfigPath    = $ConfigPath
        EffectiveConfigurationPath = $effectiveConfigPath
        Json          = $true
    }
    switch ($Operation) {
        'Plan' {
            $buildParameters.Plan = $true
        }
        'Prepare' {
            $buildParameters.RunMode = 'Build'
            $buildParameters.ModuleSignModule = $true
        }
        'Publish' {
            $buildParameters.Publish = $true
            $buildParameters.Confirm = $false
            $buildParameters.SourceRepositoryRoot = $repositoryRoot
            $buildParameters.ExpectedSourceRevision = $ExpectedCommit
            $buildParameters.SourceInputPath = [string[]] $explicitInputPaths
        }
    }

    $output = @(& $buildScript @buildParameters 2>&1)
    $exitCode = $LASTEXITCODE
    $json = ($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    $releaseOutput = $json
    $json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    if ($exitCode -ne 0) {
        throw "PowerForge $Operation failed with exit code $exitCode. Receipt: $ReceiptPath"
    }

    try {
        $receipt = $json | ConvertFrom-Json -Depth 100
    } catch {
        throw "PowerForge $Operation did not return a valid JSON receipt. Receipt: $ReceiptPath"
    }
    if ($receipt.Success -ne $true) {
        throw "PowerForge $Operation failed: $($receipt.ErrorMessage)"
    }

    [pscustomobject]@{
        Success               = $true
        Operation             = $Operation
        Version               = $Version
        Commit                = $actualCommit
        CertificateThumbprint = $certificateThumbprint
        CertificateExpiresUtc = $certificate.NotAfter.ToUniversalTime()
        GitHubRecovery         = $releaseRecovery
        EffectiveConfigPath    = $effectiveConfigPath
        EffectiveConfigSha256  = $effectiveConfigSha256
        ReceiptPath           = $ReceiptPath
    }
} catch {
    $outputTail = $releaseOutput
    if ($null -ne $outputTail -and $outputTail.Length -gt 20000) {
        $outputTail = $outputTail.Substring($outputTail.Length - 20000)
    }
    if ($receiptInitialized) {
        [pscustomobject]@{
            Success        = $false
            Status         = 'Failed'
            Stage          = $releaseStage
            Operation      = $Operation
            Version        = $Version
            ExpectedCommit = $ExpectedCommit
            ActualCommit   = $actualCommit
            EffectiveConfigPath = $effectiveConfigPath
            ErrorMessage   = $_.Exception.Message
            OutputTail     = $outputTail
            FailedAtUtc    = [DateTime]::UtcNow
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    }
    throw
} finally {
    if ($moduleProvenanceCreated -and -not [string]::IsNullOrWhiteSpace($moduleProvenancePath)) {
        Remove-Item -LiteralPath $moduleProvenancePath -Force -ErrorAction SilentlyContinue
    }
    if ($moduleSignedProvenanceCreated -and -not [string]::IsNullOrWhiteSpace($moduleSignedProvenancePath)) {
        Remove-Item -LiteralPath $moduleSignedProvenancePath -Force -ErrorAction SilentlyContinue
    }
}
