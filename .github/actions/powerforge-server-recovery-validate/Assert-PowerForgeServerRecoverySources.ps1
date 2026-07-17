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
    param(
        [Parameter(Mandatory)][string] $Url,
        [switch] $AllowUnsupported
    )

    $value = $Url.Trim()
    $patterns = @(
        '^https://github\.com/(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$',
        '^ssh://git@github\.com(?::22)?/(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$',
        '^git@github\.com(?:-[A-Za-z0-9_-]+)?:(?<slug>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?$'
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
    if ($AllowUnsupported) {
        return $null
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
        $treeEntry = @(& git -C $root ls-tree $repositoryRef -- $relativePath 2>$null)
        if ($LASTEXITCODE -ne 0 -or $treeEntry.Count -ne 1 -or
            $treeEntry[0] -notmatch '^(?<mode>[0-9]{6})\s+blob\s+(?<blob>[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\s+') {
            throw "Managed recovery source is missing from its pinned repository commit: $source"
        }
        if ($Matches['mode'] -notin @('100644', '100755')) {
            throw "Managed recovery source has unsupported Git mode $($Matches['mode']) in its pinned repository commit: $source"
        }
        $pinnedBlob = [string]$Matches['blob']
        $currentBlob = @(& git -C $root hash-object -- $candidate 2>$null)
        if ($LASTEXITCODE -ne 0 -or $currentBlob.Count -ne 1 -or
            -not [string]::Equals($pinnedBlob, $currentBlob[0], [StringComparison]::OrdinalIgnoreCase)) {
            throw "Managed recovery source differs from its pinned repository commit: $source"
        }
    }

    $candidate
}

function Get-ValidatedCaptureTarget {
    param(
        [Parameter(Mandatory)][object[]] $Files,
        [Parameter(Mandatory)][string] $Label
    )

    $targets = foreach ($file in $files) {
        $target = [string]$file.target
        $segments = @($target.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
        if ($target -notmatch '^/[A-Za-z0-9._/-]+$' -or
            $target -eq '/' -or
            $target.Contains('//', [StringComparison]::Ordinal) -or
            @($segments).Where({ $_ -in @('.', '..') }).Count -gt 0) {
            throw "$Label recovery capture path contains unsupported characters: $target"
        }
        $target
    }
    @($targets)
}

function Get-ExpectedPlainCaptureCommand {
    param([Parameter(Mandatory)][pscustomobject] $RecoveryManifest)

    $files = @(@($RecoveryManifest.capture.plainFiles) | Where-Object { $null -ne $_ })
    if ($files.Count -eq 0) {
        return ''
    }

    $targets = Get-ValidatedCaptureTarget -Files $files -Label 'Plain'
    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add('/usr/bin/tar')
    $parts.Add('-czf')
    $parts.Add('-')
    if (-not @($files).Where({ $_.required -eq $true }).Count) {
        $parts.Add('--ignore-failed-read')
    }
    $parts.AddRange([string[]]$targets)
    $parts -join ' '
}

function Get-ExpectedEncryptedCaptureCommand {
    param([Parameter(Mandatory)][pscustomobject] $RecoveryManifest)

    $files = @(@($RecoveryManifest.capture.encryptedFiles) | Where-Object { $null -ne $_ })
    if ($files.Count -eq 0) {
        return ''
    }

    $recipient = [string]$RecoveryManifest.backupTarget.recipient
    if ($recipient -cnotmatch '^age1[a-z0-9]+$') {
        throw 'Credential-free validation requires a stable age public recipient in backupTarget.recipient.'
    }

    $targets = Get-ValidatedCaptureTarget -Files $files -Label 'Encrypted'

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

function Get-ExpectedCaptureSudoersCommand {
    param([Parameter(Mandatory)][pscustomobject] $RecoveryManifest)

    $commands = [Collections.Generic.List[string]]::new()
    $plainCommand = Get-ExpectedPlainCaptureCommand -RecoveryManifest $RecoveryManifest
    if (-not [string]::IsNullOrWhiteSpace($plainCommand)) {
        $commands.Add($plainCommand)
    }
    $encryptedCommand = Get-ExpectedEncryptedCaptureCommand -RecoveryManifest $RecoveryManifest
    if (-not [string]::IsNullOrWhiteSpace($encryptedCommand)) {
        $commands.Add($encryptedCommand)
    }

    $captureCommands = @(@($RecoveryManifest.capture.commands) | Where-Object { $null -ne $_ })
    foreach ($captureCommand in $captureCommands.Where({ $_.sensitive -ne $true })) {
        $command = ([string]$captureCommand.command).Trim()
        if ($command -notmatch '^sudo -n (?<command>.+)$') {
            continue
        }
        $sudoersCommand = [string]$Matches['command']
        if ([string]::Equals($sudoersCommand, 'apachectl -S', [StringComparison]::Ordinal)) {
            $sudoersCommand = '/usr/sbin/apachectl -S'
        } elseif ($sudoersCommand -notmatch '^/[A-Za-z0-9._/-]+(?: [A-Za-z0-9._~:@%+=/-]+)*$') {
            throw "Privileged recovery capture command is not supported by credential-free validation: $command"
        }
        $commands.Add($sudoersCommand)
    }

    $uniqueCommands = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($command in $commands) {
        [void] $uniqueCommands.Add($command)
    }
    @($uniqueCommands)
}

$managedEntries = @(
    @($Manifest.paths)
    @($Manifest.apache.sites)
    @($Manifest.apache.conf)
    @($Manifest.systemd.services)
    @($Manifest.systemd.timers)
) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.source) }

$resolvedEntries = foreach ($entry in $managedEntries) {
    $target = if (-not [string]::IsNullOrWhiteSpace([string]$entry.path)) {
        [string]$entry.path
    } else {
        [string]$entry.target
    }
    [pscustomobject]@{
        Entry = $entry
        Path  = Resolve-ManagedSourcePath `
            -Entry $entry `
            -Repositories @($Manifest.repositories) `
            -LocalWorkspace $Workspace `
            -LocalEngineRoot $EngineRoot `
            -LocalCallerRepository $CallerRepository `
            -LocalEngineRepository $EngineRepository
        Target = $target
    }
}

$sudoersRootSources = @($resolvedEntries).Where({
    [string]::Equals([string]$_.Target, '/etc/sudoers', [StringComparison]::Ordinal) -or
    [string]::Equals([string]$_.Target, '/etc/sudoers.d', [StringComparison]::Ordinal)
})
if ($sudoersRootSources.Count -gt 0) {
    throw 'Repository-managed recovery sources must not replace /etc/sudoers or /etc/sudoers.d.'
}
$sudoersSources = @($resolvedEntries).Where({
    ([string]$_.Target).StartsWith('/etc/sudoers.d/', [StringComparison]::Ordinal)
})
foreach ($sudoersSource in $sudoersSources) {
    if (-not [string]::Equals([string]$sudoersSource.Entry.validation, 'sudoers', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Managed files below /etc/sudoers.d must declare sudoers validation.'
    }
    if (([string]$sudoersSource.Target) -notmatch '^/etc/sudoers\.d/[A-Za-z0-9_-]+$') {
        throw 'Managed sudoers targets must use a dot-free file name directly below /etc/sudoers.d.'
    }
}
$misplacedSudoersSources = @($resolvedEntries).Where({
    [string]::Equals([string]$_.Entry.validation, 'sudoers', [StringComparison]::OrdinalIgnoreCase) -and
    -not ([string]$_.Target).StartsWith('/etc/sudoers.d/', [StringComparison]::Ordinal)
})
if ($misplacedSudoersSources.Count -gt 0) {
    throw 'Managed sudoers validation is supported only for files below /etc/sudoers.d.'
}

$expectedEncryptedCommand = Get-ExpectedEncryptedCaptureCommand -RecoveryManifest $Manifest
if (-not [string]::IsNullOrWhiteSpace($expectedEncryptedCommand)) {
    $engineRepositories = @(
        foreach ($repository in @($Manifest.repositories)) {
            $repositoryUrl = [string]$repository.url
            if ([string]::IsNullOrWhiteSpace($repositoryUrl)) {
                continue
            }
            $repositorySlug = Get-GitHubRepositorySlug -Url $repositoryUrl -AllowUnsupported
            if (-not [string]::IsNullOrWhiteSpace($repositorySlug) -and
                [string]::Equals($repositorySlug, $EngineRepository, [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals([string]$repository.ref, $env:POWERFORGE_ENGINE_REF, [StringComparison]::OrdinalIgnoreCase)) {
                $repository
            }
        }
    )
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
            [string]$_.Target,
            '/usr/local/sbin/powerforge-server-encrypted-capture',
            [StringComparison]::Ordinal
        ) -and
        [string]::Equals([string]$_.Entry.source, $expectedHelperSource, [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.Path, $expectedHelperPath, [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.Entry.kind, 'file', [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$_.Entry.owner, 'root', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.Entry.group, 'root', [StringComparison]::Ordinal) -and
        ([string]$_.Entry.mode) -in @('755', '0755')
    })
    if ($helper.Count -ne 1) {
        throw 'Encrypted recovery capture requires the exact managed helper from the pinned PowerForge engine.'
    }

    $expectedSudoersCommands = @(Get-ExpectedCaptureSudoersCommand -RecoveryManifest $Manifest)
    $commandAliases = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $grantedAliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sudoersDocuments = [Collections.Generic.List[object]]::new()
    $captureGrantCount = 0
    foreach ($sudoersSource in $sudoersSources) {
        if (-not [string]::Equals([string]$sudoersSource.Entry.kind, 'file', [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$sudoersSource.Entry.owner, 'root', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$sudoersSource.Entry.group, 'root', [StringComparison]::Ordinal) -or
            ([string]$sudoersSource.Entry.mode) -notin @('440', '0440')) {
            throw 'Managed sudoers sources must be root-owned files with mode 440 or 0440.'
        }
        $lines = @(Get-Content -LiteralPath $sudoersSource.Path)
        $sudoersDocuments.Add([pscustomobject]@{
            Path  = $sudoersSource.Path
            Lines = $lines
        })
        foreach ($line in $lines) {
            $trimmedLine = $line.Trim()
            if ($trimmedLine -match '^(?:#|@)include(?:dir)?\b') {
                throw 'Managed sudoers sources must not include additional policy files.'
            }
            if ($trimmedLine -match '^#\d+(?:\s|,).*\bNOPASSWD\s*:') {
                throw 'Managed sudoers sources must not use numeric user principals for NOPASSWD grants.'
            }
            if ([string]::IsNullOrWhiteSpace($trimmedLine) -or $trimmedLine.StartsWith('#', [StringComparison]::Ordinal)) {
                continue
            }
            if ($trimmedLine.EndsWith('\', [StringComparison]::Ordinal)) {
                throw 'Managed sudoers sources must not use line continuations.'
            }
            if ($trimmedLine -match '^User_Alias\b') {
                throw 'Managed sudoers sources must not use User_Alias entries.'
            }
            $isDefaultsLine = $trimmedLine -cmatch '^Defaults(?:[:@>!]\S+)?\s+'
            if ($isDefaultsLine -and
                ($trimmedLine -cmatch '(?:^|[,\s])!authenticate(?:$|[,\s])' -or
                 $trimmedLine -cmatch '(?:^|[,\s])exempt_group\s*=')) {
                throw 'Managed sudoers sources must not disable authentication with Defaults !authenticate or exempt_group.'
            }
            if ($line -cnotmatch '^\s*Cmnd_Alias\b') {
                continue
            }
            if ($line -cnotmatch '^\s*Cmnd_Alias\s+(?<alias>\S+)\s*=\s*(?<commands>.+?)\s*$') {
                throw 'Managed sudoers source contains a malformed command alias.'
            }
            $commandAlias = [string]$Matches['alias']
            $commandText = [string]$Matches['commands']
            if ($commandAlias -cnotmatch '^[A-Z][A-Z0-9_]*$') {
                throw "Managed sudoers source contains an invalid command alias: $commandAlias"
            }
            if ($commandAliases.ContainsKey($commandAlias)) {
                throw "Managed sudoers source contains a duplicate command alias: $commandAlias"
            }
            $commands = [string[]]@($commandText -split '\s*,\s*')
            $commandAliases.Add($commandAlias, $commands)
        }
    }

    foreach ($sudoersDocument in $sudoersDocuments) {
        foreach ($line in $sudoersDocument.Lines) {
            $trimmedLine = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmedLine) -or $trimmedLine.StartsWith('#', [StringComparison]::Ordinal) -or
                $line -notmatch '\bNOPASSWD\s*:') {
                continue
            }
            if ($line -notmatch '^\s*(?<principal>\S+)\s+') {
                throw 'Managed sudoers source contains a malformed NOPASSWD grant.'
            }
            $principal = [string]$Matches['principal']
            if (-not [string]::Equals($principal, $CaptureUser, [StringComparison]::Ordinal)) {
                if ($principal -notmatch '^[a-z_][a-z0-9_-]{0,31}$') {
                    throw "Managed sudoers NOPASSWD grant uses an unsupported broad or aliased principal: $principal"
                }
                continue
            }
            if ($line -notmatch '^\s*\S+\s+ALL\s*=\s*\(\s*(?<runas>[^)]+?)\s*\)\s+NOPASSWD:\s*(?<commands>.+?)\s*$') {
                throw 'The recovery capture account NOPASSWD grant must use the exact ALL=(root) form.'
            }
            $runAs = [string]$Matches['runas']
            $grantText = [string]$Matches['commands']
            if (-not [string]::Equals($runAs, 'root', [StringComparison]::Ordinal)) {
                throw 'The recovery capture account must run its approved commands only as root.'
            }
            foreach ($commandAlias in @($grantText -split '\s*,\s*')) {
                if ($commandAlias -cnotmatch '^[A-Z][A-Z0-9_]*$' -or -not $commandAliases.ContainsKey($commandAlias)) {
                    throw "The recovery capture account grant references an invalid or undefined command alias: $commandAlias"
                }
                if (-not $grantedAliases.Add($commandAlias)) {
                    throw "The recovery capture account grant repeats command alias: $commandAlias"
                }
            }
            $captureGrantCount++
        }
    }

    $grantedCommands = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($commandAlias in $grantedAliases) {
        $commands = $commandAliases[$commandAlias]
        if ($commands.Count -ne 1) {
            throw "Recovery capture command alias must authorize exactly one command: $commandAlias"
        }
        if (-not $grantedCommands.Add($commands[0])) {
            throw "Recovery capture aliases must not authorize the same command more than once: $commandAlias"
        }
    }

    $expectedCommands = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($command in $expectedSudoersCommands) {
        [void] $expectedCommands.Add($command)
    }
    if ($captureGrantCount -eq 0 -or $grantedAliases.Count -ne $grantedCommands.Count -or
        $grantedCommands.Count -ne $expectedCommands.Count -or -not $expectedCommands.SetEquals($grantedCommands)) {
        throw 'Managed sudoers sources do not authorize the exact hardened encrypted-capture command.'
    }
}

@($resolvedEntries).Count
