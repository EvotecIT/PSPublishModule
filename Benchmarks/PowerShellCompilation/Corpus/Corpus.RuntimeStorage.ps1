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

    $tracePath = Join-Path $WorkingDirectory ('host-trace-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        $probe = Invoke-OwnedProcess -FileName $ArtifactPath -Arguments @() -WorkingDirectory $WorkingDirectory -Environment @{
            DOTNET_HOST_TRACE = '1'
            DOTNET_HOST_TRACEFILE = $tracePath
            COREHOST_TRACE = '1'
            COREHOST_TRACEFILE = $tracePath
            DOTNET_HOST_TRACE_VERBOSITY = '4'
            COREHOST_TRACE_VERBOSITY = '4'
        } -TimeoutSeconds 120
        if ($probe.ExitCode -ne 0) { throw 'Runtime storage probe failed to execute the artifact.' }
        $trace = [IO.File]::ReadAllText($tracePath)
        $pattern = if ($IsLinux) { '(?m)/[^\r\n\[\]''"=]*?/(?:libhostfxr|libhostpolicy|libcoreclr|libclrjit)\.so' }
            elseif ($IsWindows) { '(?im)[a-z]:[\\/][^\r\n\[\]''"=]*?[\\/](?:hostfxr|hostpolicy|coreclr|clrjit)\.dll' }
            else { throw 'Runtime storage inspection supports Windows and Linux.' }
        $paths = @([regex]::Matches($trace, $pattern) |
            ForEach-Object { $_.Value.Trim() } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Sort-Object -Unique)
        $coreClr = @($paths | Where-Object { [IO.Path]::GetFileName($_) -in @('libcoreclr.so', 'coreclr.dll') })
        if (-not $coreClr.Count) {
            throw 'Host trace did not resolve CoreCLR; timing budgets cannot be evaluated.'
        }
        $mounts = @($paths | ForEach-Object {
            $fileSystem = Get-CorpusRuntimeFileSystem -Path $_ -WorkingDirectory $WorkingDirectory
            [ordered]@{ path = $_; fileSystem = $fileSystem; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        return [ordered]@{ checked = $true; libraries = $mounts; coreClrVersion = [IO.Path]::GetFileName((Split-Path -Parent $coreClr[0])) }
    } finally {
        if (Test-Path -LiteralPath $tracePath) { Remove-Item -LiteralPath $tracePath -ErrorAction Stop }
    }
}

function Get-CorpusRuntimeFileSystem {
    param([string] $Path, [string] $WorkingDirectory)

    if ($IsLinux) {
        $mount = Invoke-OwnedProcess -FileName 'findmnt' -Arguments @('-n', '-o', 'FSTYPE', '--target', $Path) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 30
        if ($mount.ExitCode -ne 0) { throw "findmnt failed for runtime '$Path'." }
        $fileSystem = $mount.StandardOutput.Trim()
        Assert-CorpusRuntimeMount -Path $Path -FileSystem $fileSystem
        return $fileSystem
    }
    if ($IsWindows) { return [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Path)).DriveFormat }
    throw 'Runtime storage inspection supports Windows and Linux.'
}
