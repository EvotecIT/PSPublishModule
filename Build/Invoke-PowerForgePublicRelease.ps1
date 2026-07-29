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
    $ReceiptPath = Join-Path $repositoryRoot 'release-receipts\powerforge-public-release.json'
}
$ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)
$receiptDirectory = Split-Path -Parent $ReceiptPath
New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null

$releaseStage = 'Preflight'
$actualCommit = $null
$releaseOutput = $null
$effectiveConfigPath = $null
[pscustomobject]@{
    Success       = $false
    Status        = 'Running'
    Stage         = $releaseStage
    Operation     = $Operation
    Version       = $Version
    ExpectedCommit = $ExpectedCommit
    StartedAtUtc  = [DateTime]::UtcNow
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8

try {
    if (-not $IsWindows) {
        throw 'The public PSPublishModule release must run on Windows because its signing certificate is stored in the Windows certificate store.'
    }

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = Join-Path $PSScriptRoot 'release.json'
    }
    $ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path

    $actualCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ine $ExpectedCommit) {
        throw "Expected release commit '$ExpectedCommit', received '$actualCommit'."
    }

    $trackedChanges = @(& git -C $repositoryRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the release checkout.'
    }
    if ($trackedChanges.Count -gt 0) {
        throw "The release checkout must start clean. Tracked changes: $($trackedChanges -join ', ')"
    }

    $releaseConfig = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json -Depth 100
    $moduleConfigPath = Join-Path $repositoryRoot 'powerforge.json'
    $moduleConfig = Get-Content -Raw -LiteralPath $moduleConfigPath | ConvertFrom-Json -Depth 100
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Set-PowerForgeAuthorizedReleaseVersion.ps1')
    $releaseConfig = Set-PowerForgeAuthorizedReleaseVersion `
        -ReleaseConfig $releaseConfig `
        -Version $Version `
        -DisableVersionUpdates:($Operation -eq 'Publish')
    $releaseConfig.GitHub | Add-Member -NotePropertyName Commitish -NotePropertyValue $ExpectedCommit -Force
    $effectiveConfigPath = Join-Path (Split-Path -Parent $ConfigPath) ".release.authorized.$PID.json"
    $releaseConfig | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $effectiveConfigPath -Encoding utf8

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

        $expectedConfirmation = "publish:$Version`:$ExpectedCommit"
        if ($Confirm -cne $expectedConfirmation) {
            throw "Publish confirmation must exactly equal '$expectedConfirmation'."
        }
    }

    $releaseStage = 'Build'
    $buildScript = Join-Path $PSScriptRoot 'Build-Project.ps1'
    $buildParameters = @{
        ModuleVersion = $Version
        ConfigPath    = $effectiveConfigPath
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
        ReceiptPath           = $ReceiptPath
    }
} catch {
    $outputTail = $releaseOutput
    if ($null -ne $outputTail -and $outputTail.Length -gt 20000) {
        $outputTail = $outputTail.Substring($outputTail.Length - 20000)
    }
    [pscustomobject]@{
        Success        = $false
        Status         = 'Failed'
        Stage          = $releaseStage
        Operation      = $Operation
        Version        = $Version
        ExpectedCommit = $ExpectedCommit
        ActualCommit   = $actualCommit
        ErrorMessage   = $_.Exception.Message
        OutputTail     = $outputTail
        FailedAtUtc    = [DateTime]::UtcNow
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    throw
} finally {
    if (-not [string]::IsNullOrWhiteSpace($effectiveConfigPath)) {
        Remove-Item -LiteralPath $effectiveConfigPath -Force -ErrorAction SilentlyContinue
    }
}
