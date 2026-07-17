[CmdletBinding()]
param(
    [Parameter(Mandatory)][pscustomobject] $Manifest,
    [Parameter(Mandatory)][string] $Workspace,
    [Parameter(Mandatory)][string] $EngineRoot,
    [Parameter(Mandatory)][string] $CallerRepository,
    [Parameter(Mandatory)][string] $EngineRepository,
    [Parameter(Mandatory)][string] $CaptureUser
)

$ErrorActionPreference = 'Stop'

function Get-GitHubRepositorySlug {
    param([Parameter(Mandatory)][string] $Url)

    $value = $Url.Trim()
    $patterns = @(
        '^https://github\.com/(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$',
        '^ssh://git@github\.com(?::22)?/(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$',
        '^git@github\.com(?:-[A-Za-z0-9_.-]+)?:(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?$'
    )
    foreach ($pattern in $patterns) {
        $match = [regex]::Match(
            $value,
            $pattern,
            [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                [Text.RegularExpressions.RegexOptions]::CultureInvariant
        )
        if ($match.Success) {
            return $match.Groups['slug'].Value
        }
    }
    throw "Recovery repository URL is not a supported GitHub URL: $Url"
}

function Assert-PathHasNoReparsePoint {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )

    $relativePath = [IO.Path]::GetRelativePath($Root, $Path)
    $currentPath = $Root
    foreach ($segment in $relativePath.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
        $currentPath = Join-Path $currentPath $segment
        $item = Get-Item -LiteralPath $currentPath -Force
        if ($item.LinkType -eq 'SymbolicLink' -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Managed recovery source must not traverse a symbolic link: $Path"
        }
    }
}

function Resolve-ManagedSourcePath {
    param(
        [Parameter(Mandatory)][pscustomobject] $Entry,
        [Parameter(Mandatory)][object[]] $Repositories,
        [Parameter(Mandatory)][string] $LocalWorkspace,
        [Parameter(Mandatory)][string] $LocalEngineRoot,
        [Parameter(Mandatory)][string] $LocalCallerRepository,
        [Parameter(Mandatory)][string] $LocalEngineRepository
    )

    $source = [string]$Entry.source
    $repository = @($Repositories) |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.path) -and
            $source.StartsWith(([string]$_.path).TrimEnd('/') + '/', [StringComparison]::Ordinal)
        } |
        Sort-Object { ([string]$_.path).Length } -Descending |
        Select-Object -First 1
    if ($null -eq $repository) {
        throw "Managed recovery source is outside every pinned repository path: $source"
    }

    $repositorySlug = Get-GitHubRepositorySlug -Url ([string]$repository.url)
    $localRoot = if ([string]::Equals($repositorySlug, $LocalCallerRepository, [StringComparison]::OrdinalIgnoreCase)) {
        $LocalWorkspace
    } elseif ([string]::Equals($repositorySlug, $LocalEngineRepository, [StringComparison]::OrdinalIgnoreCase)) {
        $LocalEngineRoot
    } else {
        throw "Managed recovery source repository is not available to credential-free validation: $repositorySlug"
    }

    $repositoryPath = ([string]$repository.path).TrimEnd('/')
    $relativePath = $source.Substring($repositoryPath.Length + 1)
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Managed recovery source has an invalid repository-relative path: $source"
    }

    $root = [IO.Path]::GetFullPath($localRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
    if (-not $candidate.StartsWith($rootPrefix, [StringComparison]::Ordinal) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Managed recovery source does not resolve to a file in its pinned checkout: $source"
    }
    Assert-PathHasNoReparsePoint -Root $root -Path $candidate
    $repositoryRef = [string]$repository.ref
    if ($repositoryRef -notmatch '^([a-fA-F0-9]{40}|[a-fA-F0-9]{64})$') {
        throw "Managed recovery source repository is not pinned to an exact commit: $repositorySlug"
    }
    if ([string]::Equals($repositorySlug, $LocalEngineRepository, [StringComparison]::OrdinalIgnoreCase)) {
        if (-not [string]::Equals($repositoryRef, $env:POWERFORGE_ENGINE_REF, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Deployment-engine managed sources must use the exact commit pinned by this action.'
        }
    } else {
        $sourceObject = "${repositoryRef}:$relativePath"
        $pinnedBlob = @(& git -C $root rev-parse --verify $sourceObject 2>$null)
        if ($LASTEXITCODE -ne 0 -or $pinnedBlob.Count -ne 1 -or $pinnedBlob[0] -notmatch '^([a-fA-F0-9]{40}|[a-fA-F0-9]{64})$') {
            throw "Managed recovery source is missing from its pinned repository commit: $source"
        }
        $currentBlob = @(& git -C $root hash-object -- $candidate 2>$null)
        if ($LASTEXITCODE -ne 0 -or $currentBlob.Count -ne 1 -or
            -not [string]::Equals($pinnedBlob[0], $currentBlob[0], [StringComparison]::OrdinalIgnoreCase)) {
            throw "Managed recovery source differs from its pinned repository commit: $source"
        }
    }

    $candidate
}

function Get-ExpectedEncryptedCaptureCommand {
    param([Parameter(Mandatory)][pscustomobject] $RecoveryManifest)

    $files = @($RecoveryManifest.capture.encryptedFiles)
    if ($files.Count -eq 0) {
        return ''
    }

    $recipient = [string]$RecoveryManifest.backupTarget.recipient
    if ($recipient -notmatch '^age1[a-z0-9]+$') {
        throw 'Credential-free validation requires a stable age public recipient in backupTarget.recipient.'
    }

    $targets = foreach ($file in $files) {
        $target = [string]$file.target
        $segments = @($target.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
        if ($target -notmatch '^/[A-Za-z0-9._/-]+$' -or
            $target -eq '/' -or
            $target.Contains('//', [StringComparison]::Ordinal) -or
            @($segments).Where({ $_ -in @('.', '..') }).Count -gt 0) {
            throw "Encrypted recovery capture path contains unsupported characters: $target"
        }
        $target
    }

    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add('/usr/local/sbin/powerforge-server-encrypted-capture')
    $parts.Add('--recipient')
    $parts.Add($recipient)
    if (-not @($files).Where({ $_.required -eq $true }).Count) {
        $parts.Add('--ignore-failed-read')
    }
    $parts.Add('--')
    $parts.AddRange([string[]]$targets)
    $parts -join ' '
}

$managedEntries = @(
    @($Manifest.paths)
    @($Manifest.apache.sites)
    @($Manifest.apache.conf)
    @($Manifest.systemd.services)
    @($Manifest.systemd.timers)
) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.source) }

$resolvedEntries = foreach ($entry in $managedEntries) {
    [pscustomobject]@{
        Entry = $entry
        Path  = Resolve-ManagedSourcePath `
            -Entry $entry `
            -Repositories @($Manifest.repositories) `
            -LocalWorkspace $Workspace `
            -LocalEngineRoot $EngineRoot `
            -LocalCallerRepository $CallerRepository `
            -LocalEngineRepository $EngineRepository
    }
}

$expectedEncryptedCommand = Get-ExpectedEncryptedCaptureCommand -RecoveryManifest $Manifest
if (-not [string]::IsNullOrWhiteSpace($expectedEncryptedCommand)) {
    $engineRepositories = @($Manifest.repositories).Where({
        [string]::Equals(
            (Get-GitHubRepositorySlug -Url ([string]$_.url)),
            $EngineRepository,
            [StringComparison]::OrdinalIgnoreCase
        ) -and
        [string]::Equals([string]$_.ref, $env:POWERFORGE_ENGINE_REF, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($engineRepositories.Count -ne 1) {
        throw 'Encrypted recovery capture requires one repository pinned to the validation action engine.'
    }
    $expectedHelperSource = ([string]$engineRepositories[0].path).TrimEnd('/') +
        '/Deployment/Linux/powerforge-server-encrypted-capture.sh'
    $expectedHelperPath = [IO.Path]::GetFullPath(
        (Join-Path $EngineRoot 'Deployment/Linux/powerforge-server-encrypted-capture.sh')
    )
    $helper = @($resolvedEntries).Where({
        [string]::Equals(
            [string]$_.Entry.path,
            '/usr/local/sbin/powerforge-server-encrypted-capture',
            [StringComparison]::Ordinal
        ) -and
        [string]::Equals([string]$_.Entry.source, $expectedHelperSource, [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.Path, $expectedHelperPath, [StringComparison]::Ordinal)
    })
    if ($helper.Count -ne 1) {
        throw 'Encrypted recovery capture requires the exact managed helper from the pinned PowerForge engine.'
    }

    $sudoersSources = @($resolvedEntries).Where({
        [string]::Equals([string]$_.Entry.validation, 'sudoers', [StringComparison]::OrdinalIgnoreCase)
    })
    $authorized = $false
    foreach ($sudoersSource in $sudoersSources) {
        $lines = @(Get-Content -LiteralPath $sudoersSource.Path)
        $commandAliases = [Collections.Generic.List[string]]::new()
        foreach ($line in $lines) {
            if ($line -notmatch '^\s*Cmnd_Alias\s+(?<alias>\S+)\s*=\s*(?<commands>.+?)\s*$') {
                continue
            }
            $commands = @([string]$Matches['commands'] -split '\s*,\s*')
            $matchingCommands = @($commands).Where({
                [string]::Equals($_, $expectedEncryptedCommand, [StringComparison]::Ordinal)
            })
            if ($matchingCommands.Count -eq 0) {
                continue
            }
            if ($commands.Count -ne 1) {
                throw 'The encrypted-capture command alias must not authorize additional commands.'
            }
            $commandAliases.Add([string]$Matches['alias'])
        }
        if ($commandAliases.Count -eq 1) {
            $commandAlias = $commandAliases[0]
            foreach ($line in $lines) {
                if ($line -notmatch '^\s*(?<principal>\S+)\s+ALL\s*=\s*\(\s*(?<runas>[^)]+?)\s*\)\s+NOPASSWD:\s*(?<commands>.+?)\s*$' -or
                    -not [string]::Equals([string]$Matches['principal'], $CaptureUser, [StringComparison]::Ordinal) -or
                    -not [string]::Equals([string]$Matches['runas'], 'root', [StringComparison]::Ordinal)) {
                    continue
                }
                $authorized = @([string]$Matches['commands'] -split '\s*,\s*').Where({
                    [string]::Equals($_, $commandAlias, [StringComparison]::Ordinal)
                }).Count -eq 1
                if ($authorized) {
                    break
                }
            }
        }
        if ($authorized) {
            break
        }
    }
    if (-not $authorized) {
        throw 'Managed sudoers sources do not authorize the exact hardened encrypted-capture command.'
    }
}

@($resolvedEntries).Count
