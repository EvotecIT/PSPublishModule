function New-ManagedBenchmarkTemporaryPath {
    param([Parameter(Mandatory)][string] $Path)

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -Path $parent -ItemType Directory -Force | Out-Null
    }

    $leaf = Split-Path -Path $Path -Leaf
    Join-Path $parent ('.{0}.{1}.{2}.tmp' -f $leaf, $PID, [Guid]::NewGuid().ToString('N'))
}

function Complete-ManagedBenchmarkArtifactWrite {
    param(
        [Parameter(Mandatory)][string] $TemporaryPath,
        [Parameter(Mandatory)][string] $Path
    )

    Move-Item -LiteralPath $TemporaryPath -Destination $Path -Force
}

function Write-ManagedBenchmarkCsv {
    param(
        [AllowNull()][object[]] $InputObject,
        [Parameter(Mandatory)][string] $Path
    )

    $temporaryPath = New-ManagedBenchmarkTemporaryPath -Path $Path
    try {
        if ($null -eq $InputObject -or $InputObject.Count -eq 0) {
            Set-Content -LiteralPath $temporaryPath -Value '' -Encoding UTF8
        } else {
            $InputObject | Export-Csv -LiteralPath $temporaryPath -NoTypeInformation
        }

        Complete-ManagedBenchmarkArtifactWrite -TemporaryPath $temporaryPath -Path $Path
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-ManagedBenchmarkJson {
    param(
        [AllowNull()][object] $InputObject,
        [Parameter(Mandatory)][string] $Path,
        [int] $Depth = 8
    )

    $temporaryPath = New-ManagedBenchmarkTemporaryPath -Path $Path
    try {
        $InputObject | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Complete-ManagedBenchmarkArtifactWrite -TemporaryPath $temporaryPath -Path $Path
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}
