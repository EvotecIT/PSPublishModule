function Resolve-CorpusFileSystemPath {
    param([string] $Path)

    $provider = $null
    $drive = $null
    $full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path, [ref]$provider, [ref]$drive)
    if ($provider.Name -cne 'FileSystem') { throw 'Corpus paths must use the filesystem provider.' }
    return $full
}

function Read-CorpusTimingRecord {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes((Resolve-CorpusFileSystemPath -Path $Path))
    $reader = [IO.StreamReader]::new([IO.MemoryStream]::new($bytes), [Text.Encoding]::UTF8, $true)
    try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
    return [pscustomobject]@{
        value = ($json | ConvertFrom-Json)
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    }
}

function New-CorpusTimingEvidence {
    param([string] $Path, [object] $Value, [string[]] $WorkspacePath = @())

    $full = Resolve-CorpusFileSystemPath -Path $Path
    foreach ($workspace in $WorkspacePath) {
        foreach ($tree in @('extract', 'runs')) {
            $inputRoot = (Resolve-CorpusFileSystemPath -Path (Join-Path $workspace $tree)).TrimEnd('\', '/')
            $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
            if ($full.Equals($inputRoot, $comparison) -or $full.StartsWith($inputRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
                throw 'Timing evidence must be outside verified input payload trees.'
            }
        }
    }
    $parent = Split-Path -Parent $full
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    # Publish a complete file without replacing existing inputs or observations.
    $temporary = Join-Path $parent ('.timing-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 100) + [Environment]::NewLine)
        [IO.File]::WriteAllBytes($temporary, $bytes)
        [IO.File]::Move($temporary, $full, $false)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -ErrorAction Stop }
    }
    return $full
}

function Get-CorpusTimingPowerPlan {
    if (-not $IsWindows) { return $null }
    $query = Invoke-OwnedProcess -FileName 'powercfg' -Arguments @('/getactivescheme') -WorkingDirectory $ExecutionContext.SessionState.Path.CurrentFileSystemLocation.ProviderPath -TimeoutSeconds 30
    if ($query.ExitCode -ne 0) { return "Unavailable (powercfg exit $($query.ExitCode))" }
    if ([string]::IsNullOrWhiteSpace($query.StandardOutput)) { return 'Unavailable (powercfg returned no description)' }
    return $query.StandardOutput.Trim()
}
