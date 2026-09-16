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

    [string] $ReceiptPath,

    [string] $ReleaseSourceRoot,

    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedToolCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-PowerForgeGitRepositoryRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $requestedRoot = (Resolve-Path -LiteralPath $Path).Path.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $gitRoot = (& git -C $requestedRoot rev-parse --show-toplevel 2>$null)
    $gitExitCode = $LASTEXITCODE
    if ($gitExitCode -ne 0 -or [string]::IsNullOrWhiteSpace([string] $gitRoot)) {
        throw "$Name is not a Git checkout: $requestedRoot"
    }
    $resolvedGitRoot = (Resolve-Path -LiteralPath ([string] $gitRoot).Trim()).Path.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals($requestedRoot, $resolvedGitRoot, [StringComparison]::Ordinal)) {
        throw "$Name must identify its exact Git top-level directory. Received '$requestedRoot'; Git reported '$resolvedGitRoot'."
    }
    $resolvedGitRoot
}

function New-PowerForgeReleaseToolSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $Commit
    )

    $temporaryDriveRoot = [IO.Path]::GetPathRoot([IO.Path]::GetTempPath())
    $snapshotParent = Join-Path $temporaryDriveRoot 'PowerForgeReleaseTool'
    $snapshotRoot = Join-Path $snapshotParent ([Guid]::NewGuid().ToString('N'))
    $archivePath = "$snapshotRoot.zip"
    New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
    try {
        & git -C $RepositoryRoot archive --format=zip --output=$archivePath $Commit
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            throw "Unable to create an immutable PowerForge tool snapshot for commit '$Commit'."
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $snapshotRoot
        $buildScript = Join-Path $snapshotRoot 'Build\Build-Project.ps1'
        if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
            throw 'The immutable PowerForge tool snapshot does not contain Build/Build-Project.ps1.'
        }
        $snapshotRoot
    } catch {
        if (Test-Path -LiteralPath $snapshotRoot) {
            Remove-Item -LiteralPath $snapshotRoot -Recurse -ErrorAction SilentlyContinue
        }
        throw
    } finally {
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -ErrorAction SilentlyContinue
        }
    }
}

function Remove-PowerForgeReleaseToolSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }
        try {
            Remove-Item -LiteralPath $Path -Recurse -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq 5) {
                Write-Warning "Unable to remove the temporary PowerForge tool snapshot '$Path': $($_.Exception.Message)"
                return
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Invoke-PowerForgeReleaseBuildProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RunnerScript,

        [Parameter(Mandatory)]
        [string] $BuildScript,

        [Parameter(Mandatory)]
        [string] $RequestPath
    )

    $hostProcess = Get-Process -Id $PID
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostProcess.Path
    $startInfo.Arguments = "-NoProfile -NonInteractive -File `"$RunnerScript`""
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables['POWERFORGE_RELEASE_BUILD_SCRIPT'] = $BuildScript
    $startInfo.EnvironmentVariables['POWERFORGE_RELEASE_BUILD_REQUEST'] = $RequestPath

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Unable to start the isolated PowerForge public-release build process.'
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut   = $stdout.GetAwaiter().GetResult()
            StdErr   = $stderr.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

$toolRepositoryRoot = Resolve-PowerForgeGitRepositoryRoot `
    -Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) `
    -Name 'PowerForge tool checkout'
$repositoryRoot = if ([string]::IsNullOrWhiteSpace($ReleaseSourceRoot)) {
    $toolRepositoryRoot
} else {
    Resolve-PowerForgeGitRepositoryRoot -Path $ReleaseSourceRoot -Name 'Release source checkout'
}
$separateReleaseSource = -not [string]::Equals(
    $repositoryRoot,
    $toolRepositoryRoot,
    [StringComparison]::Ordinal)
if ([string]::IsNullOrWhiteSpace($ExpectedToolCommit)) {
    if ($separateReleaseSource) {
        throw 'ExpectedToolCommit is required when ReleaseSourceRoot differs from the PowerForge tool checkout.'
    }
    $ExpectedToolCommit = $ExpectedCommit
} elseif (-not $separateReleaseSource -and $ExpectedToolCommit -ine $ExpectedCommit) {
    throw 'ExpectedToolCommit must match ExpectedCommit when the tool and release source use the same checkout.'
}
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge.PublicRelease\powerforge-public-release.json'
}
$ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)
$wrapperFailureReceiptPath = Join-Path `
    (Join-Path ([IO.Path]::GetTempPath()) 'PowerForge.PublicRelease') `
    "powerforge-public-release.$Version.$($ExpectedCommit.ToLowerInvariant()).$($ExpectedToolCommit.ToLowerInvariant()).wrapper-failure.json"
$releaseReceiptRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'release-receipts'))
$repositoryUri = [Uri] ($repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
$toolRepositoryUri = [Uri] ($toolRepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
$releaseReceiptUri = [Uri] ($releaseReceiptRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
$receiptUri = [Uri] $ReceiptPath
if ($repositoryUri.IsBaseOf($receiptUri) -and -not $releaseReceiptUri.IsBaseOf($receiptUri)) {
    throw 'ReceiptPath must stay outside the release checkout or under its dedicated release-receipts directory.'
}
if ($separateReleaseSource -and $toolRepositoryUri.IsBaseOf($receiptUri)) {
    throw 'ReceiptPath must stay outside the PowerForge tool checkout when ReleaseSourceRoot is separate.'
}
$receiptDirectory = Split-Path -Parent $ReceiptPath

$releaseStage = 'Preflight'
$actualCommit = $null
$actualToolCommit = $null
$releaseOutput = $null
$effectiveConfigPath = $null
$releaseRecovery = $null
$moduleProvenancePath = $null
$moduleProvenanceCreated = $false
$moduleSignedProvenancePath = $null
$moduleSignedProvenanceCreated = $false
$sourceDirty = $true
$receiptInitialized = $false
$toolSnapshotRoot = $null
$toolBuildRoot = $null
$savedMsBuildDisableNodeReuse = $null
$savedDotNetCliUseMsBuildServer = $null
$dotNetLifetimeConfigured = $false
$preserveSuccessfulReceipt = $false

try {
    if (-not $IsWindows) {
        throw 'The public PSPublishModule release must run on Windows because its signing certificate is stored in the Windows certificate store.'
    }

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = Join-Path $repositoryRoot 'Build\release.json'
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
    . (Join-Path (Join-Path $PSScriptRoot 'Private') 'Test-PowerForgeTrackedReleaseReceipt.ps1')
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

    if ($separateReleaseSource) {
        $toolState = Get-PowerForgeReleaseSourceState `
            -RepositoryRoot $toolRepositoryRoot `
            -GeneratedProvenancePath @() `
            -ReceiptPath $ReceiptPath
        if ([bool] $toolState.SourceDirty) {
            throw "The PowerForge tool checkout must start clean. Tracked or untracked changes: $(@($toolState.Changes) -join ', ')"
        }
    }

    $receiptIsTracked = Test-PowerForgeTrackedReleaseReceipt `
        -RepositoryRoot $repositoryRoot `
        -ReceiptPath $ReceiptPath
    if ($receiptIsTracked) {
        throw 'ReceiptPath must not identify a tracked repository file.'
    }
    if (Test-Path -LiteralPath $ReceiptPath) {
        Remove-Item -LiteralPath $ReceiptPath -Force
    }
    if (Test-Path -LiteralPath $wrapperFailureReceiptPath) {
        Remove-Item -LiteralPath $wrapperFailureReceiptPath -Force
    }
    New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
    $receiptInitialized = $true

    $actualCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ine $ExpectedCommit) {
        throw "Expected release commit '$ExpectedCommit', received '$actualCommit'."
    }
    $actualToolCommit = if ($separateReleaseSource) {
        (& git -C $toolRepositoryRoot rev-parse HEAD).Trim()
    } else {
        $actualCommit
    }
    if ($LASTEXITCODE -ne 0 -or $actualToolCommit -ine $ExpectedToolCommit) {
        throw "Expected PowerForge tool commit '$ExpectedToolCommit', received '$actualToolCommit'."
    }
    $toolSnapshotRoot = New-PowerForgeReleaseToolSnapshot `
        -RepositoryRoot $toolRepositoryRoot `
        -Commit $actualToolCommit
    $toolBuildRoot = Join-Path $toolSnapshotRoot 'Build'

    $releaseConfig = $sourceReleaseConfig
    $moduleConfigPath = Join-Path $repositoryRoot 'powerforge.json'
    $moduleConfig = Get-Content -Raw -LiteralPath $moduleConfigPath | ConvertFrom-Json -Depth 100
    . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Set-PowerForgeAuthorizedReleaseVersion.ps1')
    $releaseConfig = Set-PowerForgeAuthorizedReleaseVersion `
        -ReleaseConfig $releaseConfig `
        -Version $Version `
        -DisableVersionUpdates:($Operation -eq 'Publish')
    . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Resolve-PowerForgeEffectiveConfigurationReferences.ps1')
    $releaseConfig = Resolve-PowerForgeEffectiveConfigurationReferences `
        -ReleaseConfig $releaseConfig `
        -SourceConfigurationPath $ConfigPath `
        -EvidenceDirectory $effectiveConfigDirectory
    . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Set-PowerForgeAuthorizedReleaseCommitish.ps1')
    $releaseConfig = Set-PowerForgeAuthorizedReleaseCommitish `
        -ReleaseConfig $releaseConfig `
        -Operation $Operation `
        -ExpectedCommit $ExpectedCommit
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
        . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Assert-PowerForgeCommittedReleaseVersion.ps1')
        Assert-PowerForgeCommittedReleaseVersion -RepositoryRoot $repositoryRoot -Version $Version -ReleaseConfig $releaseConfig

        $gitHubTokenPath = [string] $releaseConfig.GitHub.TokenFilePath
        if ([string]::IsNullOrWhiteSpace($gitHubTokenPath)) {
            throw 'Build/release.json must configure the unified GitHub release token file.'
        }
        $gitHubToken = (Get-Content -Raw -LiteralPath $gitHubTokenPath).Trim()
        . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Get-PowerForgeReleasePackageIds.ps1')
        $packageIds = Get-PowerForgeReleasePackageIds `
            -ReleaseConfig $releaseConfig `
            -RepositoryRoot $repositoryRoot
        . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Enable-PowerForgeVerifiedGitHubReleaseRecovery.ps1')
        $releaseRecovery = Enable-PowerForgeVerifiedGitHubReleaseRecovery `
            -ReleaseConfig $releaseConfig `
            -Version $Version `
            -ExpectedCommit $ExpectedCommit `
            -Token $gitHubToken `
            -PackageIds $packageIds `
            -NuGetSource ([string] $releaseConfig.Packages.PublishSource) `
            -ModuleName ([string] $releaseConfig.Module.ModuleName)

        $expectedConfirmation = if ($separateReleaseSource) {
            "publish:$Version`:$ExpectedCommit`:tool:$ExpectedToolCommit"
        } else {
            "publish:$Version`:$ExpectedCommit"
        }
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

    $savedMsBuildDisableNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE', 'Process')
    $savedDotNetCliUseMsBuildServer = [Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', 'Process')
    [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', '0', 'Process')
    $dotNetLifetimeConfigured = $true

    $snapshotModuleProject = Join-Path $toolSnapshotRoot 'PSPublishModule\PSPublishModule.csproj'
    $restoreOutput = @(& dotnet restore $snapshotModuleProject --locked-mode --disable-parallel --nologo --verbosity quiet 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $restoreDetails = ($restoreOutput | Out-String).Trim()
        throw "Failed to restore the immutable PowerForge tool snapshot (exit code $LASTEXITCODE).`n$restoreDetails"
    }

    $releaseStage = 'Build'
    $buildScript = Join-Path $toolBuildRoot 'Build-Project.ps1'
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

    $buildRequestPath = Join-Path $toolSnapshotRoot '.release-build-request.clixml'
    $buildParameters | Export-Clixml -LiteralPath $buildRequestPath -Depth 20
    $buildRunner = Join-Path $toolBuildRoot 'Private\Invoke-PowerForgePublicReleaseBuild.ps1'
    $buildProcess = Invoke-PowerForgeReleaseBuildProcess `
        -RunnerScript $buildRunner `
        -BuildScript $buildScript `
        -RequestPath $buildRequestPath
    $exitCode = $buildProcess.ExitCode
    $json = [string] $buildProcess.StdOut
    $releaseOutput = $json
    $json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
    if ($exitCode -ne 0) {
        $standardError = ([string] $buildProcess.StdErr).Trim()
        if (-not [string]::IsNullOrWhiteSpace($standardError)) {
            $releaseOutput = "$json$([Environment]::NewLine)$standardError"
        }
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
    $preserveSuccessfulReceipt = $true
    . (Join-Path (Join-Path $toolBuildRoot 'Private') 'Set-PowerForgePublicReleaseAuthorizationReceipt.ps1')
    Set-PowerForgePublicReleaseAuthorizationReceipt `
        -ReceiptPath $ReceiptPath `
        -ReleaseCommit $actualCommit `
        -ToolCommit $actualToolCommit `
        -ReleaseSourceRoot $repositoryRoot `
        -EffectiveConfigPath $effectiveConfigPath `
        -EffectiveConfigSha256 $effectiveConfigSha256

    [pscustomobject]@{
        Success               = $true
        Operation             = $Operation
        Version               = $Version
        Commit                = $actualCommit
        ToolCommit            = $actualToolCommit
        ReleaseSourceRoot     = $repositoryRoot
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
        $failureReceiptPath = if ($preserveSuccessfulReceipt) {
            $wrapperFailureReceiptPath
        } else {
            $ReceiptPath
        }
        $failureReceiptDirectory = Split-Path -Parent $failureReceiptPath
        New-Item -ItemType Directory -Path $failureReceiptDirectory -Force | Out-Null
        [pscustomobject]@{
            Success        = $false
            Status         = 'Failed'
            Stage          = $releaseStage
            Operation      = $Operation
            Version        = $Version
            ExpectedCommit = $ExpectedCommit
            ActualCommit   = $actualCommit
            ExpectedToolCommit = $ExpectedToolCommit
            ActualToolCommit = $actualToolCommit
            ReleaseSourceRoot = $repositoryRoot
            EffectiveConfigPath = $effectiveConfigPath
            ErrorMessage   = $_.Exception.Message
            OutputTail     = $outputTail
            SuccessfulReleaseReceiptPath = if ($preserveSuccessfulReceipt) { $ReceiptPath } else { $null }
            FailedAtUtc    = [DateTime]::UtcNow
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $failureReceiptPath -Encoding utf8
        if ($preserveSuccessfulReceipt) {
            Write-Warning "The successful release receipt was preserved at '$ReceiptPath'. Wrapper failure details were written to '$failureReceiptPath'."
        }
    }
    throw
} finally {
    if ($moduleProvenanceCreated -and -not [string]::IsNullOrWhiteSpace($moduleProvenancePath)) {
        Remove-Item -LiteralPath $moduleProvenancePath -Force -ErrorAction SilentlyContinue
    }
    if ($moduleSignedProvenanceCreated -and -not [string]::IsNullOrWhiteSpace($moduleSignedProvenancePath)) {
        Remove-Item -LiteralPath $moduleSignedProvenancePath -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($toolSnapshotRoot) -and
        (Test-Path -LiteralPath $toolSnapshotRoot)) {
        Remove-PowerForgeReleaseToolSnapshot -Path $toolSnapshotRoot
    }
    if ($dotNetLifetimeConfigured) {
        [Environment]::SetEnvironmentVariable(
            'MSBUILDDISABLENODEREUSE',
            $savedMsBuildDisableNodeReuse,
            'Process')
        [Environment]::SetEnvironmentVariable(
            'DOTNET_CLI_USE_MSBUILD_SERVER',
            $savedDotNetCliUseMsBuildServer,
            'Process')
    }
}
