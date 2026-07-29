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

if (-not $IsWindows) {
    throw 'The public PSPublishModule release must run on Windows because its signing certificate is stored in the Windows certificate store.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}
$ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path $repositoryRoot 'release-receipts\powerforge-public-release.json'
}
$ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)

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

$manifestPath = Join-Path $repositoryRoot 'Module\PSPublishModule.psd1'
$manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
if ($Operation -eq 'Publish' -and [string] $manifest.ModuleVersion -ne $Version) {
    throw "Publish requires committed module version '$Version'; the manifest contains '$($manifest.ModuleVersion)'."
}
if ($Operation -eq 'Publish') {
    $expectedConfirmation = "publish:$Version`:$ExpectedCommit"
    if ($Confirm -cne $expectedConfirmation) {
        throw "Publish confirmation must exactly equal '$expectedConfirmation'."
    }
}

$receiptDirectory = Split-Path -Parent $ReceiptPath
New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null

$buildScript = Join-Path $PSScriptRoot 'Build-Project.ps1'
$buildParameters = @{
    ModuleVersion = $Version
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
    Success              = $true
    Operation            = $Operation
    Version              = $Version
    Commit               = $actualCommit
    CertificateThumbprint = $certificateThumbprint
    CertificateExpiresUtc = $certificate.NotAfter.ToUniversalTime()
    ReceiptPath          = $ReceiptPath
}
