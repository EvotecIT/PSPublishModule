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
$script:validatedCredentialEnvironment = @{}
$script:allowedConsumerEvidencePaths = [Collections.Generic.HashSet[string]]::new(
    $(if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }))

function Invoke-GitText {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string[]] $Arguments)
    $output = @(& $script:gitPath -c core.fsmonitor=false -c core.hooksPath=/dev/null -c core.quotePath=false -C $Root @Arguments 2>&1)
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
        [string] $ExpectedRepository,
        [switch] $DeferContentCheck
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
    if (-not $DeferContentCheck) {
        $status = Invoke-GitText -Root $Root -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
        if (-not [string]::IsNullOrWhiteSpace($status)) { throw "$Name must be clean before Apple release work." }
    }
    return $head
}

function Assert-NoReplaceRefs {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string] $Name)
    $replaceRefs = Invoke-GitText -Root $Root -Arguments @('for-each-ref', '--format=%(refname)', 'refs/replace')
    if (-not [string]::IsNullOrWhiteSpace($replaceRefs)) {
        throw "$Name must not contain Git replacement refs."
    }
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
        'tar' {
            if ($IsWindows) { Join-Path $env:SystemRoot 'System32/tar.exe' }
            else { '/usr/bin/tar' }
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

function Resolve-PathFromBase {
    param([Parameter(Mandatory)][string] $BasePath, [Parameter(Mandatory)][string] $Value)
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $BasePath $Value))
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

function Get-ForwardedArgumentList {
    if ($ArgumentList[0] -ne 'apple-release') { return @($ArgumentList) }
    $result = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $ArgumentList.Count; $index++) {
        $argument = $ArgumentList[$index]
        if ($argument -eq '--capture-provenance') {
            $index++
            continue
        }
        if ($argument.StartsWith('--capture-provenance=', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $result.Add($argument)
    }
    return @($result)
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
    if ($command -eq 'apple-release' -and $operation -ieq 'UploadExisting') {
        throw 'UploadExisting is forbidden at the pinned local operator boundary because existing archive bytes lack reviewed provenance.'
    }
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
            if ($option -eq '--reviewed-plan') { Add-AllowedConsumerEvidencePath -Path $path -Name $option }
        }
    }
    foreach ($option in @('--allowed-root', '--out', '--write-root', '--receipt')) {
        $value = Get-OptionValue -Option $option
        if ($value) { Assert-UnlinkedPath -Path (Resolve-OptionPath -Value $value) -Name $option -AllowMissingLeaf }
    }
}

function Assert-FixedAppleToolConfiguration {
    $allowed = @{
        XcodeBuildExecutable = @('xcodebuild', '/usr/bin/xcodebuild')
        XcodeGenExecutable = @('xcodegen', '/opt/homebrew/bin/xcodegen')
        XcrunExecutable = @('xcrun', '/usr/bin/xcrun')
        DittoExecutable = @('ditto', '/usr/bin/ditto')
        SpctlExecutable = @('spctl', '/usr/sbin/spctl')
    }
    function Test-Node {
        param($Node)
        if ($null -eq $Node -or $Node -is [string] -or $Node.GetType().IsPrimitive) { return }
        if ($Node -is [Collections.IEnumerable] -and $Node -isnot [Management.Automation.PSCustomObject]) {
            foreach ($item in $Node) { Test-Node -Node $item }
            return
        }
        foreach ($property in $Node.PSObject.Properties) {
            if ($allowed.ContainsKey($property.Name)) {
                $value = [string]$property.Value
                if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Trim() -notin $allowed[$property.Name]) {
                    throw "$($property.Name) must use the fixed trusted Apple tool at the pinned local operator boundary."
                }
            }
            Test-Node -Node $property.Value
        }
    }
    foreach ($configPath in @($script:validatedConfigPaths | Sort-Object -Unique)) {
        Test-Node -Node (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json)
    }
}

function Invoke-TrackedInputValidator {
    param([Parameter(Mandatory)][string] $SourceCommit)
    $validator = Join-Path $toolRoot '.github/actions/apple-release/Assert-TrackedAppleReleaseInputs.ps1'
    Assert-UnlinkedPath -Path $validator -Name 'Tracked-input validator'
    $relative = [IO.Path]::GetRelativePath($toolRoot, $validator).Replace('\', '/')
    Invoke-GitText -Root $toolRoot -Arguments @('ls-files', '--error-unmatch', '--', $relative) | Out-Null
    Invoke-GitText -Root $toolRoot -Arguments @('diff', '--quiet', 'HEAD', '--', $relative) | Out-Null
    $allowMissingProject = $ArgumentList[0] -eq 'apple-release' -and $ArgumentList.Count -gt 1 -and $ArgumentList[1] -ieq 'Cleanup'
    foreach ($configPath in @($script:validatedConfigPaths | Select-Object -Unique)) {
        & $validator `
            -ConfigPath $configPath `
            -SourceCommit $SourceCommit `
            -GitPath $script:gitPath `
            -SkipToolManifest `
            -RejectCredentialOverrides `
            -AllowMissingProject:$allowMissingProject
    }
}

function Get-SavedEnvironmentValue {
    param([Parameter(Mandatory)][hashtable] $Saved, [Parameter(Mandatory)][string[]] $Names)
    foreach ($name in $Names) {
        $value = $Saved[$name]
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
    }
    return $null
}

function Suspend-AppleCredentialEnvironment {
    $saved = @{}
    foreach ($name in @(
        'APP_STORE_CONNECT_KEY_ID', 'APP_STORE_CONNECT_ISSUER_ID', 'APP_STORE_CONNECT_PRIVATE_KEY_PATH', 'APP_STORE_CONNECT_PRIVATE_KEY',
        'ASC_KEY_ID', 'ASC_ISSUER_ID', 'ASC_PRIVATE_KEY_PATH', 'ASC_PRIVATE_KEY')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    return $saved
}

function Set-SafeGitEnvironment {
    foreach ($entry in [Environment]::GetEnvironmentVariables().Keys) {
        $name = [string]$entry
        if ($name.StartsWith('GIT_', [StringComparison]::OrdinalIgnoreCase)) {
            [Environment]::SetEnvironmentVariable($name, $null)
        }
    }
    [Environment]::SetEnvironmentVariable('GIT_NO_REPLACE_OBJECTS', '1')
}

function Assert-PrivateUnixPath {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][bool] $Directory,
        [Parameter(Mandatory)][string] $Description
    )
    if (-not $IsMacOS) { throw 'The fixed private App Store Connect profile is supported only on the trusted macOS operator host.' }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { throw "$Description is missing." }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Description must not be a symbolic link or reparse point." }
    if ($Directory -and -not $item.PSIsContainer) { throw "$Description must be a directory." }
    if (-not $Directory -and $item.PSIsContainer) { throw "$Description must be a regular file." }
    $stat = @(& /usr/bin/stat -f '%u|%l|%HT' $item.FullName 2>$null)
    $statExitCode = $LASTEXITCODE
    $currentUid = [string](& /usr/bin/id -u)
    $idExitCode = $LASTEXITCODE
    if ($statExitCode -ne 0 -or $idExitCode -ne 0 -or $stat.Count -ne 1 -or [string]::IsNullOrWhiteSpace($currentUid)) { throw "$Description metadata could not be verified." }
    $parts = $stat[0].Split('|')
    $expectedType = if ($Directory) { 'Directory' } else { 'Regular File' }
    if ($parts.Count -ne 3 -or $parts[0] -ne $currentUid -or $parts[2] -ne $expectedType) {
        throw "$Description must be a $($expectedType.ToLowerInvariant()) owned by the operator user."
    }
    if (-not $Directory -and $parts[1] -ne '1') { throw "$Description must not have hard links." }
    $listing = @(& /bin/ls -lde $item.FullName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $listing.Count -eq 0) { throw "$Description access controls could not be verified." }
    if ((($listing[0] -split '\s+', 2)[0]).Contains('+')) { throw "$Description must not grant access through a POSIX ACL." }
    $shared = [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupWrite -bor [IO.UnixFileMode]::GroupExecute -bor
        [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherWrite -bor [IO.UnixFileMode]::OtherExecute
    if (([IO.File]::GetUnixFileMode($item.FullName) -band $shared) -ne 0) {
        throw "$Description permissions must not grant group or other access."
    }
}

function Assert-FixedLocalCredentialProfile {
    param([Parameter(Mandatory)][hashtable] $Saved)
    $keyPath = Get-SavedEnvironmentValue -Saved $Saved -Names @('APP_STORE_CONNECT_PRIVATE_KEY_PATH', 'ASC_PRIVATE_KEY_PATH')
    $keyId = Get-SavedEnvironmentValue -Saved $Saved -Names @('APP_STORE_CONNECT_KEY_ID', 'ASC_KEY_ID')
    $issuerId = Get-SavedEnvironmentValue -Saved $Saved -Names @('APP_STORE_CONNECT_ISSUER_ID', 'ASC_ISSUER_ID')
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
    Assert-PrivateUnixPath -Path $profileRoot -Directory $true -Description 'App Store Connect profile directory'
    $current = $fullKeyPath
    while (-not $current.Equals($profileRoot, $comparison)) {
        Assert-PrivateUnixPath -Path $current -Directory:$($current -ne $fullKeyPath) -Description 'App Store Connect private-key path'
        $current = [IO.Directory]::GetParent($current)?.FullName
        if ([string]::IsNullOrWhiteSpace($current)) { throw 'App Store Connect private-key path escaped the private profile directory.' }
    }
    $script:validatedCredentialKeyPath = $fullKeyPath
    $script:validatedCredentialEnvironment = @{
        APP_STORE_CONNECT_PRIVATE_KEY_PATH = $fullKeyPath
        APP_STORE_CONNECT_KEY_ID = $keyId
        APP_STORE_CONNECT_ISSUER_ID = $issuerId
    }
}

function New-TrackedToolSnapshot {
    param([Parameter(Mandatory)][string] $TarPath)
    $snapshotRoot = Join-Path $temporaryRoot 'tool-source'
    $archivePath = Join-Path $temporaryRoot 'tool-source.tar'
    New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
    & $script:gitPath -C $toolRoot archive --format=tar --output=$archivePath HEAD
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw 'Unable to materialize the exact tracked PSPublishModule source snapshot.'
    }
    & $TarPath -xf $archivePath -C $snapshotRoot
    if ($LASTEXITCODE -ne 0) { throw 'Unable to extract the exact tracked PSPublishModule source snapshot.' }
    $snapshotItem = Get-Item -LiteralPath $snapshotRoot -Force
    if (-not $snapshotItem.PSIsContainer -or $snapshotItem.LinkType -or
        ($snapshotItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Tracked PSPublishModule build snapshot must be an unlinked directory.'
    }
    $linkedEntries = @(Get-ChildItem -LiteralPath $snapshotRoot -Recurse -Force | Where-Object {
        $_.LinkType -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
    })
    if ($linkedEntries.Count -gt 0) { throw 'Tracked PSPublishModule build snapshot must not contain symbolic links or reparse points.' }
    return $snapshotRoot
}

function Assert-AuthoritativeCaptureProvenance {
    param(
        [Parameter(Mandatory)][string] $GhPath,
        [Parameter(Mandatory)][string] $SourceCommit
    )
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
    if ($sourceCommit -ne $SourceCommit.ToLowerInvariant()) {
        throw "Capture provenance source commit '$sourceCommit' does not match the exact consumer HEAD '$SourceCommit'."
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
    $script:validatedCaptureProvenance = $provenance
    Add-AllowedConsumerEvidencePath -Path $path -Name '--capture-provenance'
}

function Get-RedactedToolText {
    param([AllowEmptyString()][string] $Text)
    $sensitiveValues = [Collections.Generic.List[string]]::new()
    foreach ($name in @(
        'APP_STORE_CONNECT_KEY_ID', 'APP_STORE_CONNECT_ISSUER_ID', 'APP_STORE_CONNECT_PRIVATE_KEY_PATH',
        'APP_STORE_CONNECT_PRIVATE_KEY', 'ASC_KEY_ID', 'ASC_ISSUER_ID', 'ASC_PRIVATE_KEY_PATH', 'ASC_PRIVATE_KEY')) {
        $value = $script:validatedCredentialEnvironment[$name]
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

function Invoke-RedactedProcess {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $NuGetPackagesPath,
        [switch] $IncludeAppleCredentials
    )
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment.Clear()
    $safeEnvironment = @{
        HOME = [Environment]::GetFolderPath('UserProfile')
        LANG = 'en_US.UTF-8'
        LC_ALL = 'en_US.UTF-8'
        PATH = $(if ($IsMacOS) { '/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin' } elseif ($IsWindows) { "$env:SystemRoot\System32;$env:SystemRoot" } else { '/usr/bin:/bin:/usr/sbin:/sbin' })
        TMPDIR = [IO.Path]::GetTempPath()
    }
    foreach ($name in @('USER', 'LOGNAME', 'SHELL', 'SSH_AUTH_SOCK', 'HTTPS_PROXY', 'HTTP_PROXY', 'NO_PROXY', 'SSL_CERT_FILE', 'SSL_CERT_DIR')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $safeEnvironment[$name] = $value }
    }
    foreach ($entry in $safeEnvironment.GetEnumerator()) { $start.Environment[$entry.Key] = [string]$entry.Value }
    $start.Environment['NUGET_PACKAGES'] = $NuGetPackagesPath
    if ($IncludeAppleCredentials) {
        foreach ($entry in $script:validatedCredentialEnvironment.GetEnumerator()) {
            $start.Environment[$entry.Key] = [string]$entry.Value
        }
    }
    foreach ($argument in $Arguments) {
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
    $savedCredentialEnvironment = Suspend-AppleCredentialEnvironment
    Set-SafeGitEnvironment
    $script:gitPath = Resolve-FixedTool -Name git
    $toolHead = Assert-CleanRepository -Root $toolRoot -Name 'PSPublishModule source' -RequiredCommit $ExpectedCommit -ExpectedRepository 'EvotecIT/PSPublishModule'
    $consumerHead = Assert-CleanRepository -Root $consumer -Name 'Consumer source' -RequiredBranch $requiredBranch -ExpectedRepository $ExpectedConsumerRepository -DeferContentCheck
    Assert-NoReplaceRefs -Root $toolRoot -Name 'PSPublishModule source'
    Assert-NoReplaceRefs -Root $consumer -Name 'Consumer source'
    $scriptRelative = [IO.Path]::GetRelativePath($toolRoot, $PSCommandPath).Replace('\', '/')
    Invoke-GitText -Root $toolRoot -Arguments @('ls-files', '--error-unmatch', '--', $scriptRelative) | Out-Null
    Invoke-GitText -Root $toolRoot -Arguments @('diff', '--quiet', 'HEAD', '--', $scriptRelative) | Out-Null
    $evidenceSupport = Join-Path $PSScriptRoot 'Invoke-PinnedPowerForge.Evidence.ps1'
    $evidenceRelative = [IO.Path]::GetRelativePath($toolRoot, $evidenceSupport).Replace('\', '/')
    Invoke-GitText -Root $toolRoot -Arguments @('ls-files', '--error-unmatch', '--', $evidenceRelative) | Out-Null
    Invoke-GitText -Root $toolRoot -Arguments @('diff', '--quiet', 'HEAD', '--', $evidenceRelative) | Out-Null
    . $evidenceSupport
    Assert-SafeArguments
    Assert-FixedAppleToolConfiguration
    Invoke-TrackedInputValidator -SourceCommit $consumerHead
    Assert-FixedLocalCredentialProfile -Saved $savedCredentialEnvironment
    $configuredSourceCommit = Get-OptionValue -Option '--apple-source-commit'
    if ($configuredSourceCommit -and $configuredSourceCommit.ToLowerInvariant() -ne $consumerHead) {
        throw "--apple-source-commit must match the exact consumer HEAD '$consumerHead'."
    }

    $dotnet = Resolve-FixedTool -Name dotnet
    if ($null -ne (Get-OptionValue -Option '--capture-provenance')) {
        $gh = Resolve-FixedTool -Name gh
        Assert-AuthoritativeCaptureProvenance -GhPath $gh -SourceCommit $consumerHead
    }
    Register-StandaloneScreenshotEvidence
    Assert-ScreenshotPublicationBinding -SourceCommit $consumerHead
    Assert-ConsumerRepositoryContent
    $tar = Resolve-FixedTool -Name tar
    $buildToolRoot = New-TrackedToolSnapshot -TarPath $tar
    $cliProject = Join-Path $buildToolRoot 'PowerForge.Cli/PowerForge.Cli.csproj'
    if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) { throw "PowerForge CLI project is missing: $cliProject" }

    $nugetPackages = Join-Path $temporaryRoot 'nuget-packages'
    $artifactsRoot = Join-Path $temporaryRoot 'artifacts'
    $cliOutput = Join-Path $temporaryRoot 'cli'
    New-Item -ItemType Directory -Path $nugetPackages, $artifactsRoot, $cliOutput -Force | Out-Null
    $restoreExitCode = Invoke-RedactedProcess `
        -FilePath $dotnet `
        -WorkingDirectory $buildToolRoot `
        -Arguments @('restore', $cliProject, '--locked-mode', '--packages', $nugetPackages, '--artifacts-path', $artifactsRoot, '--verbosity', 'minimal') `
        -NuGetPackagesPath $nugetPackages
    if ($restoreExitCode -ne 0) { exit $restoreExitCode }
    $buildExitCode = Invoke-RedactedProcess `
        -FilePath $dotnet `
        -WorkingDirectory $buildToolRoot `
        -Arguments @('build', $cliProject, '--configuration', 'Release', '--framework', 'net10.0', '--no-restore', '--artifacts-path', $artifactsRoot, '--output', $cliOutput, "--property:RestorePackagesPath=$nugetPackages", '--verbosity', 'minimal') `
        -NuGetPackagesPath $nugetPackages
    if ($buildExitCode -ne 0) { exit $buildExitCode }
    $cliAssembly = Join-Path $cliOutput 'PowerForge.Cli.dll'
    if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) { throw "PowerForge CLI build output is missing: $cliAssembly" }

    Write-Host "PowerForge source: $toolHead"
    Write-Host "Consumer source: $consumerHead"
    $toolExitCode = Invoke-RedactedProcess `
        -FilePath $dotnet `
        -WorkingDirectory $consumer `
        -Arguments (@($cliAssembly) + (Get-ForwardedArgumentList)) `
        -NuGetPackagesPath $nugetPackages `
        -IncludeAppleCredentials
    if ($toolExitCode -ne 0) { exit $toolExitCode }
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
