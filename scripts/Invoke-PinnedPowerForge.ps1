param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $ExpectedCommit,

    [Parameter(Mandatory)]
    [string] $ConsumerRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $ExpectedConsumerRepository,

    [Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)]
    [string[]] $ArgumentList
)

$ErrorActionPreference = 'Stop'
$toolRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$consumer = [IO.Path]::GetFullPath($ConsumerRoot)
$requiredBranch = 'main'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("PowerForge.LocalOperator." + [guid]::NewGuid().ToString('N'))

function Invoke-GitText {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string[]] $Arguments)
    $output = @(& $script:gitPath -C $Root @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Git failed in '$Root': git $($Arguments -join ' ')" }
    return ($output -join [Environment]::NewLine).Trim()
}

function Assert-UnlinkedPath {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name,
        [switch] $AllowMissingLeaf
    )
    $full = [IO.Path]::GetFullPath($Path)
    $current = $full
    if ($AllowMissingLeaf -and -not (Test-Path -LiteralPath $current)) { $current = Split-Path -Parent $current }
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.LinkType -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "$Name must not traverse a symbolic link or reparse point: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-UnlinkedDirectory {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Name)
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer) { throw "$Name must be a directory: $Path" }
    Assert-UnlinkedPath -Path $Path -Name $Name
}

function Assert-GitHubOrigin {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string] $ExpectedRepository)
    $origin = Invoke-GitText -Root $Root -Arguments @('remote', 'get-url', 'origin')
    $match = [regex]::Match($origin, '^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)(?<repo>[^/]+/[^/]+?)(?:\.git)?$')
    if (-not $match.Success -or -not $match.Groups['repo'].Value.Equals($ExpectedRepository, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Consumer origin '$origin' does not match expected GitHub repository '$ExpectedRepository'."
    }
}

function Assert-CleanRepository {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [string] $RequiredCommit,
        [string] $RequiredBranch,
        [string] $ExpectedRepository
    )
    Assert-UnlinkedDirectory -Path $Root -Name $Name
    if ((Invoke-GitText -Root $Root -Arguments @('rev-parse', '--is-inside-work-tree')) -ne 'true') {
        throw "$Name is not a Git worktree: $Root"
    }
    $head = (Invoke-GitText -Root $Root -Arguments @('rev-parse', 'HEAD')).ToLowerInvariant()
    if ($RequiredCommit -and $head -ne $RequiredCommit.ToLowerInvariant()) {
        throw "$Name HEAD '$head' does not match reviewed commit '$RequiredCommit'."
    }
    if ($ExpectedRepository) { Assert-GitHubOrigin -Root $Root -ExpectedRepository $ExpectedRepository }
    if ($RequiredBranch) {
        $branch = Invoke-GitText -Root $Root -Arguments @('symbolic-ref', '--short', 'HEAD')
        if ($branch -ne $RequiredBranch) { throw "$Name must be on '$RequiredBranch', not '$branch'." }
        Invoke-GitText -Root $Root -Arguments @('fetch', '--quiet', 'origin', $RequiredBranch) | Out-Null
        $remote = (Invoke-GitText -Root $Root -Arguments @('rev-parse', "refs/remotes/origin/$RequiredBranch")).ToLowerInvariant()
        if ($head -ne $remote) { throw "$Name HEAD '$head' does not match origin/$RequiredBranch '$remote'." }
    }
    $status = Invoke-GitText -Root $Root -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
    if (-not [string]::IsNullOrWhiteSpace($status)) { throw "$Name must be clean before Apple release work." }
    return $head
}

function Resolve-FixedTool {
    param([Parameter(Mandatory)][string] $Name)
    $candidates = switch ($Name) {
        'dotnet' {
            if ($IsMacOS) { '/usr/local/share/dotnet/dotnet' }
            elseif ($IsWindows) { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
            else { '/usr/share/dotnet/dotnet' }
        }
        'gh' {
            if ($IsMacOS) { '/opt/homebrew/bin/gh' }
            elseif ($IsWindows) { Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe' }
            else { '/usr/bin/gh' }
        }
        'git' {
            if ($IsWindows) { Join-Path $env:ProgramFiles 'Git/cmd/git.exe' }
            else { '/usr/bin/git' }
        }
    }
    if (-not (Test-Path -LiteralPath $candidates -PathType Leaf)) { throw "Required fixed $Name executable was not found: $candidates" }
    return [IO.Path]::GetFullPath($candidates)
}

function Resolve-OptionPath {
    param([Parameter(Mandatory)][string] $Value)
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $consumer $Value))
}

function Assert-TrackedConsumerInput {
    param([Parameter(Mandatory)][string] $Value, [Parameter(Mandatory)][string] $Option)
    $path = Resolve-OptionPath -Value $Value
    $relative = [IO.Path]::GetRelativePath($consumer, $path).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('../')) {
        throw "$Option must stay inside the exact consumer checkout."
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$Option input was not found: $path" }
    Assert-UnlinkedPath -Path $path -Name $Option
    Invoke-GitText -Root $consumer -Arguments @('ls-files', '--error-unmatch', '--', $relative) | Out-Null
    Invoke-GitText -Root $consumer -Arguments @('diff', '--quiet', 'HEAD', '--', $relative) | Out-Null
}

function Get-OptionValue {
    param([Parameter(Mandatory)][string] $Option)
    for ($index = 0; $index -lt $ArgumentList.Count; $index++) {
        if ($ArgumentList[$index] -eq $Option) {
            if ($index + 1 -ge $ArgumentList.Count) { throw "Missing value for $Option." }
            return $ArgumentList[$index + 1]
        }
        if ($ArgumentList[$index].StartsWith("$Option=", [StringComparison]::OrdinalIgnoreCase)) {
            return $ArgumentList[$index].Substring($Option.Length + 1)
        }
    }
    return $null
}

function Assert-SafeArguments {
    if ($ArgumentList.Count -lt 1 -or $ArgumentList[0] -notin @('apple-release', 'apple-screenshots', 'apple-governance')) {
        throw 'Pinned local operator accepts only Apple PowerForge commands.'
    }
    foreach ($forbidden in @('--key-path', '--key-id', '--issuer-id')) {
        if ($null -ne (Get-OptionValue -Option $forbidden)) { throw "$forbidden is forbidden at the local operator boundary." }
    }
    $command = $ArgumentList[0]
    $operation = if ($ArgumentList.Count -gt 1) { $ArgumentList[1] } else { '' }
    $config = Get-OptionValue -Option '--config'
    if (($command -eq 'apple-release' -or $command -eq 'apple-screenshots' -or
        ($command -eq 'apple-governance' -and $operation -ne 'snapshot')) -and -not $config) {
        throw "$command requires an explicit --config at the pinned local operator boundary."
    }
    $script:validatedConfigPaths = @()
    foreach ($option in @('--config', '--release-config')) {
        $value = Get-OptionValue -Option $option
        if ($value) {
            Assert-TrackedConsumerInput -Value $value -Option $option
            $script:validatedConfigPaths += Resolve-OptionPath -Value $value
        }
    }
    foreach ($option in @('--capture-provenance', '--reviewed-plan')) {
        $value = Get-OptionValue -Option $option
        if ($value) {
            $path = Resolve-OptionPath -Value $value
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$option input was not found: $path" }
            Assert-UnlinkedPath -Path $path -Name $option
        }
    }
    foreach ($option in @('--allowed-root', '--out', '--write-root', '--receipt')) {
        $value = Get-OptionValue -Option $option
        if ($value) { Assert-UnlinkedPath -Path (Resolve-OptionPath -Value $value) -Name $option -AllowMissingLeaf }
    }
}

function Invoke-TrackedInputValidator {
    param([Parameter(Mandatory)][string] $SourceCommit)
    $validator = Join-Path $toolRoot '.github/actions/apple-release/Assert-TrackedAppleReleaseInputs.ps1'
    Assert-UnlinkedPath -Path $validator -Name 'Tracked-input validator'
    $relative = [IO.Path]::GetRelativePath($toolRoot, $validator).Replace('\', '/')
    Invoke-GitText -Root $toolRoot -Arguments @('ls-files', '--error-unmatch', '--', $relative) | Out-Null
    Invoke-GitText -Root $toolRoot -Arguments @('diff', '--quiet', 'HEAD', '--', $relative) | Out-Null
    foreach ($configPath in @($script:validatedConfigPaths | Select-Object -Unique)) {
        & $validator `
            -ConfigPath $configPath `
            -SourceCommit $SourceCommit `
            -GitPath $script:gitPath `
            -SkipToolManifest `
            -RejectCredentialOverrides
    }
}

function Get-FirstEnvironmentValue {
    param([Parameter(Mandatory)][string[]] $Names)
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
    }
    return $null
}

function Assert-FixedLocalCredentialProfile {
    $keyPath = Get-FirstEnvironmentValue -Names @('APP_STORE_CONNECT_PRIVATE_KEY_PATH', 'ASC_PRIVATE_KEY_PATH')
    $keyId = Get-FirstEnvironmentValue -Names @('APP_STORE_CONNECT_KEY_ID', 'ASC_KEY_ID')
    $issuerId = Get-FirstEnvironmentValue -Names @('APP_STORE_CONNECT_ISSUER_ID', 'ASC_ISSUER_ID')
    $configuredCount = @($keyPath, $keyId, $issuerId).Where({ -not [string]::IsNullOrWhiteSpace($_) }).Count
    if ($configuredCount -eq 0) {
        $script:validatedCredentialKeyPath = $null
        return
    }
    if ($configuredCount -ne 3) {
        throw 'The fixed local App Store Connect profile must provide a complete key path, key id, and issuer id tuple.'
    }
    if (-not [IO.Path]::IsPathRooted($keyPath)) {
        throw 'The fixed local App Store Connect private-key path must be absolute.'
    }
    $fullKeyPath = [IO.Path]::GetFullPath($keyPath)
    $profileRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) '.appstoreconnect'))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $profilePrefix = $profileRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullKeyPath.StartsWith($profilePrefix, $comparison)) {
        throw 'Local Apple credentials must remain inside the fixed private ~/.appstoreconnect profile.'
    }
    if (-not (Test-Path -LiteralPath $fullKeyPath -PathType Leaf)) {
        throw "The fixed local App Store Connect private key was not found: $fullKeyPath"
    }
    Assert-UnlinkedPath -Path $fullKeyPath -Name 'App Store Connect private key'
    $script:validatedCredentialKeyPath = $fullKeyPath
}

function Assert-AuthoritativeCaptureProvenance {
    param([Parameter(Mandatory)][string] $GhPath)
    $configured = Get-OptionValue -Option '--capture-provenance'
    if (-not $configured) { return }
    $path = Resolve-OptionPath -Value $configured
    $provenance = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $repository = [string]$provenance.repository
    $runId = [string]$provenance.captureRunId
    $sourceCommit = ([string]$provenance.sourceCommit).ToLowerInvariant()
    $workflowRef = [string]$provenance.workflowRef
    $workflowPattern = '^' + [regex]::Escape($ExpectedConsumerRepository) + '/(?<path>\.github/workflows/[A-Za-z0-9._/-]+\.ya?ml)@refs/heads/' + [regex]::Escape($requiredBranch) + '$'
    $workflowMatch = [regex]::Match($workflowRef, $workflowPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $repository.Equals($ExpectedConsumerRepository, [StringComparison]::OrdinalIgnoreCase) -or
        $runId -notmatch '^\d+$' -or $sourceCommit -notmatch '^[0-9a-f]{40}$' -or -not $workflowMatch.Success) {
        throw 'Capture provenance repository, run id, source commit, or workflow identity is invalid.'
    }
    $run = & $GhPath api "repos/$repository/actions/runs/$runId" 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $run.status -ne 'completed' -or $run.conclusion -ne 'success' -or
        $run.event -ne 'workflow_dispatch' -or $run.path -ne $workflowMatch.Groups['path'].Value -or
        $run.head_repository.full_name -ne $repository -or $run.head_branch -ne $requiredBranch -or
        ([string]$run.head_sha).ToLowerInvariant() -ne $sourceCommit) {
        throw 'Capture provenance does not identify the dedicated successful default-branch capture workflow at the exact source commit.'
    }
    $artifactName = "powerforge-apple-screenshot-provenance-$sourceCommit"
    $downloadRoot = Join-Path $temporaryRoot 'authoritative-provenance'
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    & $GhPath run download $runId --repo $repository --name $artifactName --dir $downloadRoot 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Unable to download authoritative capture provenance artifact '$artifactName'." }
    $downloaded = @(Get-ChildItem -LiteralPath $downloadRoot -File -Recurse | Where-Object Name -eq 'powerforge-apple-screenshot-provenance.json')
    if ($downloaded.Count -ne 1) { throw 'Authoritative capture provenance artifact must contain exactly one provenance document.' }
    $expectedHash = (Get-FileHash -LiteralPath $downloaded[0].FullName -Algorithm SHA256).Hash
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($expectedHash -ne $actualHash) { throw 'Local capture provenance differs from the retained GitHub Actions artifact.' }
}

function Get-RedactedToolText {
    param([AllowEmptyString()][string] $Text)
    $sensitiveValues = [Collections.Generic.List[string]]::new()
    foreach ($name in @(
        'APP_STORE_CONNECT_KEY_ID', 'APP_STORE_CONNECT_ISSUER_ID', 'APP_STORE_CONNECT_PRIVATE_KEY_PATH',
        'APP_STORE_CONNECT_PRIVATE_KEY', 'ASC_KEY_ID', 'ASC_ISSUER_ID', 'ASC_PRIVATE_KEY_PATH', 'ASC_PRIVATE_KEY')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $sensitiveValues.Add($value) }
    }
    $keyPath = $script:validatedCredentialKeyPath
    if (-not [string]::IsNullOrWhiteSpace($keyPath) -and (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
        $keyPath = [IO.Path]::GetFullPath($keyPath)
        $profileRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) '.appstoreconnect'))
        $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        if (-not $keyPath.StartsWith($profileRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
            throw 'Local Apple credentials must remain inside the fixed private ~/.appstoreconnect profile.'
        }
        Assert-UnlinkedPath -Path $keyPath -Name 'App Store Connect private key'
        $privateKey = Get-Content -LiteralPath $keyPath -Raw
        if (-not [string]::IsNullOrWhiteSpace($privateKey)) {
            $sensitiveValues.Add($privateKey)
            foreach ($line in $privateKey -split '\r?\n') {
                if ($line.Length -ge 16 -and -not $line.StartsWith('-----', [StringComparison]::Ordinal)) {
                    $sensitiveValues.Add($line)
                }
            }
        }
    }
    foreach ($value in @($sensitiveValues | Sort-Object Length -Descending -Unique)) {
        $Text = [regex]::Replace($Text, [regex]::Escape($value), '[REDACTED]', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $encoded = [Text.Json.JsonEncodedText]::Encode($value).ToString()
        if ($encoded -ne $value) {
            $Text = [regex]::Replace($Text, [regex]::Escape($encoded), '[REDACTED]', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }
    $Text = [regex]::Replace(
        $Text,
        '(?i)("appStoreConnectApi(?:KeyPath|KeyId|IssuerId)"\s*:\s*)"[^"]*"',
        '$1"[REDACTED]"')
    $Text = [regex]::Replace(
        $Text,
        '(?i)(?:[A-Za-z]:)?[^\s"'']*\.appstoreconnect[/\\][^\s"'']+',
        '[REDACTED_PROFILE_PATH]')
    $Text = [regex]::Replace(
        $Text,
        '-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----.*?-----END(?: [A-Z0-9]+)? PRIVATE KEY-----',
        '[REDACTED_PRIVATE_KEY]',
        [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $Text
}

function Invoke-PowerForgeProcess {
    param(
        [Parameter(Mandatory)][string] $DotNetPath,
        [Parameter(Mandatory)][string] $ProjectPath,
        [Parameter(Mandatory)][string] $ArtifactsPath
    )
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $DotNetPath
    $start.WorkingDirectory = $consumer
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('run', '--framework', 'net10.0', '--artifacts-path', $ArtifactsPath, '--project', $ProjectPath, '--') + $ArgumentList) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = $start
        if (-not $process.Start()) { throw 'Unable to start the exact PowerForge CLI process.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $safeStdOut = Get-RedactedToolText -Text ($stdout.GetAwaiter().GetResult())
        $safeStdErr = Get-RedactedToolText -Text ($stderr.GetAwaiter().GetResult())
        if ($safeStdOut.Length -gt 0) { [Console]::Out.Write($safeStdOut) }
        if ($safeStdErr.Length -gt 0) { [Console]::Error.Write($safeStdErr) }
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $script:gitPath = Resolve-FixedTool -Name git
    $toolHead = Assert-CleanRepository -Root $toolRoot -Name 'PSPublishModule source' -RequiredCommit $ExpectedCommit -ExpectedRepository 'EvotecIT/PSPublishModule'
    $consumerHead = Assert-CleanRepository -Root $consumer -Name 'Consumer source' -RequiredBranch $requiredBranch -ExpectedRepository $ExpectedConsumerRepository
    $scriptRelative = [IO.Path]::GetRelativePath($toolRoot, $PSCommandPath).Replace('\', '/')
    Invoke-GitText -Root $toolRoot -Arguments @('ls-files', '--error-unmatch', '--', $scriptRelative) | Out-Null
    Invoke-GitText -Root $toolRoot -Arguments @('diff', '--quiet', 'HEAD', '--', $scriptRelative) | Out-Null
    Assert-SafeArguments
    Invoke-TrackedInputValidator -SourceCommit $consumerHead
    Assert-FixedLocalCredentialProfile

    $dotnet = Resolve-FixedTool -Name dotnet
    $gh = Resolve-FixedTool -Name gh
    Assert-AuthoritativeCaptureProvenance -GhPath $gh
    $cliProject = Join-Path $toolRoot 'PowerForge.Cli/PowerForge.Cli.csproj'
    if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) { throw "PowerForge CLI project is missing: $cliProject" }

    Write-Host "PowerForge source: $toolHead"
    Write-Host "Consumer source: $consumerHead"
    Push-Location $consumer
    try {
        $toolExitCode = Invoke-PowerForgeProcess `
            -DotNetPath $dotnet `
            -ProjectPath $cliProject `
            -ArtifactsPath (Join-Path $temporaryRoot 'artifacts')
        if ($toolExitCode -ne 0) { exit $toolExitCode }
    } finally { Pop-Location }
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
