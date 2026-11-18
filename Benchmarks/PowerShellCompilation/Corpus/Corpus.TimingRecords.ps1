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
    param([string] $Path, [object] $Value)

    $full = Resolve-CorpusFileSystemPath -Path $Path
    $parent = Split-Path -Parent $full
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    # Exclusive creation also rejects hard links, symbolic links and aliases to
    # an existing input. Later checkpoints replace only this run's own output.
    $stream = [IO.File]::Open($full, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 100) + [Environment]::NewLine)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    return $full
}

function Get-CorpusTimingPowerPlan {
    if (-not $IsWindows) { return $null }
    $query = Invoke-OwnedProcess -FileName 'powercfg' -Arguments @('/getactivescheme') -WorkingDirectory $ExecutionContext.SessionState.Path.CurrentFileSystemLocation.Path -TimeoutSeconds 30
    if ($query.ExitCode -ne 0) { return "Unavailable (powercfg exit $($query.ExitCode))" }
    if ([string]::IsNullOrWhiteSpace($query.StandardOutput)) { return 'Unavailable (powercfg returned no description)' }
    return $query.StandardOutput.Trim()
}
