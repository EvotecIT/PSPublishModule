Set-StrictMode -Version 3.0

function Get-PathComparison {
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        return [StringComparison]::OrdinalIgnoreCase
    }
    return [StringComparison]::Ordinal
}

function Assert-ContainedPath {
    param([string] $Root, [string] $Path, [string] $Label)

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, (Get-PathComparison))) {
        throw "$Label escapes its declared root: $pathFull"
    }
    return $pathFull
}

function Test-LooksLikeRootedPath {
    param([string] $Path)

    return [IO.Path]::IsPathRooted($Path) -or
        $Path.StartsWith('\\', [StringComparison]::Ordinal) -or
        $Path.StartsWith('//', [StringComparison]::Ordinal) -or
        ($Path.Length -ge 2 -and [char]::IsLetter($Path[0]) -and $Path[1] -eq ':')
}

function Write-Utf8Json {
    param([string] $Path, [object] $Value, [int] $Depth = 100)

    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth $Depth) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
}

function ConvertTo-PortableRegionCandidate {
    param([object] $Candidate, [string] $SourceRoot)

    $rootFull = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $sourcePath = Assert-ContainedPath -Root $rootFull -Path ([string] $Candidate.sourcePath) -Label 'Region candidate source'
    $relativePath = $sourcePath.Substring($rootFull.Length).TrimStart([char[]] @('\', '/')).Replace('\', '/')
    $graphProperty = $Candidate.PSObject.Properties['regionGraph']
    $graph = if ($null -eq $graphProperty) { $null } else { $graphProperty.Value }
    $regions = if ($null -eq $graph) { @() } else { @($graph.regions | Where-Object { $null -ne $_ }) }
    $sourceLineSpan = if ([int] $Candidate.startLine -gt 0 -and [int] $Candidate.endLine -ge [int] $Candidate.startLine) {
        1 + [int] $Candidate.endLine - [int] $Candidate.startLine
    } else { 0 }

    return [pscustomobject]@{
        regionId = [string] $Candidate.regionId
        sourceSha256 = [string] $Candidate.sourceSha256
        sourceDocumentSha256 = [string] $Candidate.sourceDocumentSha256
        relativePath = $relativePath
        sourceName = [string] $Candidate.sourceName
        sourceLine = [int] $Candidate.sourceLine
        startOffset = [int] $Candidate.startOffset
        endOffset = [int] $Candidate.endOffset
        startLine = [int] $Candidate.startLine
        startColumn = [int] $Candidate.startColumn
        endLine = [int] $Candidate.endLine
        endColumn = [int] $Candidate.endColumn
        sourceLineSpan = $sourceLineSpan
        promoted = [bool] $Candidate.promoted
        decisionCode = [string] $Candidate.decisionCode
        reason = [string] $Candidate.reason
        generatedName = [string] $Candidate.generatedName
        graph = if ($null -eq $graph) { $null } else {
            [pscustomobject]@{
                schemaVersion = [int] $graph.schemaVersion
                regionCount = $regions.Count
                typedRegions = @($regions | Where-Object execution -eq 'Typed').Count
                hostedRegions = @($regions | Where-Object execution -eq 'Hosted').Count
                mixedRegions = @($regions | Where-Object execution -eq 'Mixed').Count
                hostedCommandBoundarySites = [int] $graph.hostedCommandBoundarySites
                moduleStateReadBoundarySites = [int] $graph.moduleStateReadBoundarySites
                moduleStateWriteBoundarySites = [int] $graph.moduleStateWriteBoundarySites
                staticBoundaryCrossings = [int] $graph.staticBoundaryCrossings
                staticBoundaryValueTransfers = [int] (($regions | ForEach-Object { [int] $_.staticBoundaryValueTransfers } | Measure-Object -Sum).Sum)
                staticBoundaryCostUnits = [int] $graph.staticBoundaryCostUnits
                regions = @($regions | ForEach-Object {
                    [pscustomobject]@{
                        regionId = [string] $_.regionId
                        ordinal = [int] $_.ordinal
                        execution = [string] $_.execution
                        startOffset = [int] $_.startOffset
                        endOffset = [int] $_.endOffset
                        startLine = [int] $_.startLine
                        startColumn = [int] $_.startColumn
                        endLine = [int] $_.endLine
                        endColumn = [int] $_.endColumn
                        inputs = @($_.inputs)
                        outputs = @($_.outputs)
                        mutations = @($_.mutations)
                        streams = @($_.streams)
                        errors = @($_.errors)
                        ordering = [string] $_.ordering
                        hostedCommandBoundarySites = [int] $_.hostedCommandBoundarySites
                        moduleStateReadBoundarySites = [int] $_.moduleStateReadBoundarySites
                        moduleStateWriteBoundarySites = [int] $_.moduleStateWriteBoundarySites
                        staticBoundaryCrossings = [int] $_.staticBoundaryCrossings
                        staticBoundaryValueTransfers = [int] $_.staticBoundaryValueTransfers
                        staticBoundaryCostUnits = [int] $_.staticBoundaryCostUnits
                    }
                })
            }
        }
    }
}

function Get-RegionCandidateDecisionSummary {
    param([object[]] $Candidates)

    return @($Candidates |
        Group-Object -Property decisionCode |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, Name |
        ForEach-Object {
            $group = @($_.Group)
            [ordered]@{
                decisionCode = $_.Name
                promoted = @($group | Where-Object promoted).Count
                retained = @($group | Where-Object { -not $_.promoted }).Count
                candidates = $group.Count
                candidatesWithLoweredGraph = @($group | Where-Object { $null -ne $_.graph }).Count
                totalStaticBoundaryCostUnits = [int] (($group | ForEach-Object { if ($null -eq $_.graph) { 0 } else { [int] $_.graph.staticBoundaryCostUnits } } | Measure-Object -Sum).Sum)
                exampleReason = [string] $group[0].reason
            }
        })
}

function Get-CrossWorkloadRetainedRegionFrontier {
    param([object[]] $Rows)

    return @($Rows |
        Where-Object { -not $_.Candidate.promoted } |
        Group-Object { $_.Candidate.decisionCode } |
        ForEach-Object {
            $group = @($_.Group)
            $costs = @($group | ForEach-Object { if ($null -eq $_.Candidate.graph) { 0 } else { [int] $_.Candidate.graph.staticBoundaryCostUnits } })
            [pscustomobject]@{
                decisionCode = $_.Name
                affectedScenarioFamilies = @($group.ScenarioFamily | Sort-Object -Unique).Count
                affectedWorkloads = @($group.WorkloadId | Sort-Object -Unique).Count
                candidates = $group.Count
                candidatesWithLoweredGraph = @($group | Where-Object { $null -ne $_.Candidate.graph }).Count
                totalStaticBoundaryCostUnits = [int] (($costs | Measure-Object -Sum).Sum)
                maximumStaticBoundaryCostUnits = [int] (($costs | Measure-Object -Maximum).Maximum)
                maximumSourceLineSpan = [int] (($group | ForEach-Object { [int] $_.Candidate.sourceLineSpan } | Measure-Object -Maximum).Maximum)
                exampleReason = [string] $group[0].Candidate.reason
            }
        } |
        Sort-Object -Property @{ Expression = 'affectedScenarioFamilies'; Descending = $true }, @{ Expression = 'affectedWorkloads'; Descending = $true }, @{ Expression = 'candidates'; Descending = $true }, decisionCode)
}

function Invoke-OwnedProcess {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [hashtable] $Environment = @{},
        [int] $TimeoutSeconds = 900
    )

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $WorkingDirectory
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { [void] $start.ArgumentList.Add($argument) }
    foreach ($key in @($Environment.Keys)) { $start.Environment[$key] = [string] $Environment[$key] }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { Write-Verbose "Failed to terminate timed-out process '$FileName': $($_.Exception.Message)" }
            throw "Owned process '$FileName' exceeded $TimeoutSeconds seconds."
        }
        $clock.Stop()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdout.GetAwaiter().GetResult()
            StandardError = $stderr.GetAwaiter().GetResult()
            DurationMilliseconds = $clock.ElapsedMilliseconds
        }
    } finally {
        $process.Dispose()
    }
}

function Get-VerifiedCorpusPayload {
    param(
        [Uri] $Uri,
        [string] $Sha256,
        [string] $CacheRoot,
        [string] $CacheExtension,
        [string] $Label,
        [switch] $OfflineMode,
        [string] $AllowedUrlPattern
    )

    if ($Uri.Scheme -ne 'https') { throw "$Label URL must use HTTPS: $Uri" }
    if ($AllowedUrlPattern -and $Uri.AbsoluteUri -notmatch $AllowedUrlPattern) { throw "$Label URL is outside its allowed endpoint: $Uri" }
    if ($Sha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid SHA-256 for ${Label}: $Sha256" }
    New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
    $payloadPath = Join-Path $CacheRoot ($Sha256 + $CacheExtension)
    Assert-ContainedPath -Root $CacheRoot -Path $payloadPath -Label "$Label cache" | Out-Null
    if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) {
        if ($OfflineMode) { throw "Offline cache miss for ${Label}." }
        $temporaryPath = $payloadPath + '.' + [guid]::NewGuid().ToString('N') + '.download'
        Assert-ContainedPath -Root $CacheRoot -Path $temporaryPath -Label "$Label download" | Out-Null
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $temporaryPath
            $actual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $Sha256) { throw "$Label hash mismatch: expected $Sha256, received $actual." }
            Move-Item -LiteralPath $temporaryPath -Destination $payloadPath
        } finally {
            if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
        }
    }
    $cachedHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($cachedHash -ne $Sha256) { throw "Cached $Label hash mismatch: $payloadPath" }
    return $payloadPath
}

function Expand-VerifiedCorpusArchive {
    param(
        [string] $PayloadPath,
        [string] $Target,
        [string] $ContainmentRoot,
        [string] $Label,
        [int] $EntryLimit = 20000,
        [long] $EntryByteLimit = 268435456,
        [long] $TotalByteLimit = 1073741824,
        [double] $CompressionRatioLimit = 200.0
    )

    Assert-ContainedPath -Root $ContainmentRoot -Path $Target -Label "$Label extraction" | Out-Null
    if (Test-Path -LiteralPath $Target) {
        $targetItem = Get-Item -LiteralPath $Target -Force
        if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label extraction is a symbolic link or junction: $Target" }
        Remove-Item -LiteralPath $Target -Recurse -Force
    }
    $staging = $Target + '.' + [guid]::NewGuid().ToString('N') + '.extracting'
    Assert-ContainedPath -Root $ContainmentRoot -Path $staging -Label "$Label staging" | Out-Null
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($PayloadPath)
        try {
            if ($archive.Entries.Count -gt $EntryLimit) { throw "$Label contains $($archive.Entries.Count) entries; limit is $EntryLimit." }
            $destinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            [long] $declaredTotalBytes = 0
            [long] $actualTotalBytes = 0
            foreach ($archiveEntry in $archive.Entries) {
                $unixType = (($archiveEntry.ExternalAttributes -shr 16) -band 0xF000)
                if ($unixType -eq 0xA000) { throw "$Label contains a symbolic link: $($archiveEntry.FullName)" }
                $portableName = $archiveEntry.FullName.Replace('\', '/')
                if (Test-LooksLikeRootedPath $portableName) { throw "$Label contains a rooted entry: $portableName" }
                $segments = @($portableName.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
                if ($segments.Count -eq 0 -or @($segments | Where-Object { $_ -in @('.', '..') -or $_.Contains(':') }).Count -gt 0) { throw "$Label contains an invalid portable path: $portableName" }
                $portableRelative = $segments -join '/'
                $portableCollisionKey = @($segments | ForEach-Object { $_.Normalize([Text.NormalizationForm]::FormC).TrimEnd([char[]] @(' ', '.')) }) -join '/'
                if ($portableCollisionKey.Split('/') -contains '') { throw "$Label contains a non-portable path: $portableName" }
                if (-not $destinations.Add($portableCollisionKey)) { throw "$Label contains a portable path collision: $portableName" }
                $destination = Assert-ContainedPath -Root $staging -Path (Join-Path $staging $portableRelative.Replace('/', [IO.Path]::DirectorySeparatorChar)) -Label "$Label entry"
                if ($portableName.EndsWith('/', [StringComparison]::Ordinal)) { New-Item -ItemType Directory -Path $destination -Force | Out-Null; continue }
                if ($archiveEntry.Length -gt $EntryByteLimit) { throw "$Label entry '$portableName' exceeds the per-entry limit." }
                if ($archiveEntry.Length -gt $TotalByteLimit - $declaredTotalBytes) { throw "$Label declares more than the total expansion limit." }
                $declaredTotalBytes += $archiveEntry.Length
                if ($archiveEntry.Length -gt 0) {
                    if ($archiveEntry.CompressedLength -le 0) { throw "$Label entry '$portableName' has an invalid compressed length." }
                    if ([double] $archiveEntry.Length / [double] $archiveEntry.CompressedLength -gt $CompressionRatioLimit) { throw "$Label entry '$portableName' exceeds the compression-ratio limit." }
                }
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                $source = $archiveEntry.Open()
                $destinationStream = [IO.File]::Create($destination)
                try {
                    $buffer = [byte[]]::new(81920)
                    [long] $entryBytes = 0
                    while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        if ($read -gt $EntryByteLimit - $entryBytes -or $read -gt $TotalByteLimit - $actualTotalBytes) { throw "$Label exceeded its expansion limits." }
                        $destinationStream.Write($buffer, 0, $read)
                        $entryBytes += $read
                        $actualTotalBytes += $read
                    }
                    if ($entryBytes -ne $archiveEntry.Length) { throw "$Label entry '$portableName' length disagrees with its archive metadata." }
                } finally { $destinationStream.Dispose(); $source.Dispose() }
            }
        } finally { $archive.Dispose() }
        Move-Item -LiteralPath $staging -Destination $Target
    } catch {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
        throw
    }
}
