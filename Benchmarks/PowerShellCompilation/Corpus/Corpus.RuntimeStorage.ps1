# A framework-dependent apphost can load .NET from a different filesystem than
# its executable. Inspect host resolution before accepting Linux timing samples.
function Assert-CorpusRuntimeMount {
    param([string] $Path, [string] $FileSystem)

    if ([string]::IsNullOrWhiteSpace($FileSystem)) {
        throw "Cannot establish runtime filesystem for '$Path'; timing budgets cannot be evaluated."
    }
    if ($FileSystem.Trim() -in @('9p', 'drvfs')) {
        throw "Runtime '$Path' is on $FileSystem; move .NET to a native Linux filesystem before evaluating timing budgets."
    }
}

function Get-CorpusRuntimeStorage {
    param([string] $ArtifactPath, [string] $WorkingDirectory)

    if (-not $IsLinux) {
        return [ordered]@{ checked = $false; reason = 'Linux mount check only' }
    }
    $tracePath = Join-Path $WorkingDirectory ('host-trace-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        $probe = Invoke-OwnedProcess -FileName $ArtifactPath -Arguments @() -WorkingDirectory $WorkingDirectory -Environment @{
            DOTNET_HOST_TRACE = '1'
            DOTNET_HOST_TRACEFILE = $tracePath
            COREHOST_TRACE = '1'
            COREHOST_TRACEFILE = $tracePath
        } -TimeoutSeconds 120
        if ($probe.ExitCode -ne 0) { throw 'Runtime storage probe failed to execute the artifact.' }
        $trace = [IO.File]::ReadAllText($tracePath)
        $paths = @([regex]::Matches($trace, '(?m)/[^\r\n\[\]''"=]*?/(?:libhostfxr|libhostpolicy|libcoreclr|libclrjit)\.so') |
            ForEach-Object { $_.Value.Trim() } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Sort-Object -Unique)
        if (-not @($paths | Where-Object { $_.EndsWith('/libcoreclr.so', [StringComparison]::Ordinal) }).Count) {
            throw 'Host trace did not resolve CoreCLR; timing budgets cannot be evaluated.'
        }
        $mounts = @($paths | ForEach-Object {
            $mount = Invoke-OwnedProcess -FileName 'findmnt' -Arguments @('-n', '-o', 'FSTYPE', '--target', $_) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 30
            if ($mount.ExitCode -ne 0) { throw "findmnt failed for runtime '$_'." }
            $fileSystem = $mount.StandardOutput.Trim()
            Assert-CorpusRuntimeMount -Path $_ -FileSystem $fileSystem
            [ordered]@{ path = $_; fileSystem = $fileSystem }
        })
        return [ordered]@{ checked = $true; libraries = $mounts }
    } finally {
        if (Test-Path -LiteralPath $tracePath) { Remove-Item -LiteralPath $tracePath -ErrorAction Stop }
    }
}
