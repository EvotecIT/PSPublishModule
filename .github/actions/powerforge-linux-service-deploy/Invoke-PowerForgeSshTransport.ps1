function Format-PowerForgeSshDiagnostic {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string] $Text,
        [Parameter(Mandatory)][string] $Label,
        [int] $MaximumCharacters = 8192
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $safeText = [Text.RegularExpressions.Regex]::Replace(
        $Text.Replace("`r`n", "`n").Replace("`r", "`n"),
        '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]',
        '?'
    )
    if ($safeText.Length -gt $MaximumCharacters) {
        $safeText = "[earlier output truncated]`n" + $safeText.Substring($safeText.Length - $MaximumCharacters)
    }
    $indented = ($safeText.TrimEnd() -split "`n" | ForEach-Object { "  | $_" }) -join "`n"
    "${Label}:`n$indented"
}

function Invoke-PowerForgeSshTransport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $TransportPath,
        [Parameter(Mandatory)][string] $Target,
        [Parameter(Mandatory)][int] $Port,
        [Parameter(Mandatory)][string[]] $SshOptions,
        [Parameter(Mandatory)][string] $Service
    )

    $stdoutPath = "$TransportPath.stdout"
    $stderrPath = "$TransportPath.stderr"
    [IO.File]::Delete($stdoutPath)
    [IO.File]::Delete($stderrPath)
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'bash'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.Environment['POWERFORGE_STDOUT_PATH'] = $stdoutPath
    $startInfo.Environment['POWERFORGE_STDERR_PATH'] = $stderrPath
    # Each capture runs as its own process substitution on a numbered descriptor so both PIDs are known.
    # A bare `wait` only covers the last process substitution, which let the stdout capture lose the race
    # with the file read below. Closing the descriptors ends both captures; waiting on both PIDs guarantees
    # the files are complete before PowerShell reads them.
    $captureCommand = 'exec 3> >(tail --bytes=8193 > "$POWERFORGE_STDOUT_PATH"); out_pid=$!; exec 4> >(tail --bytes=8193 > "$POWERFORGE_STDERR_PATH"); err_pid=$!; ssh "$@" >&3 2>&4; status=$?; exec 3>&- 4>&-; wait "$out_pid" "$err_pid"; exit "$status"'
    foreach ($argument in @('-c', $captureCommand, 'powerforge-ssh') + @($SshOptions) + @(
        '-p', [string]$Port, $Target, "powerforge-service-deploy-v1 --service $Service"
    )) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start the restricted SSH deployment transport.'
    }
    $copyFailure = $null
    try {
        $stream = [IO.File]::OpenRead($TransportPath)
        try {
            $stream.CopyTo($process.StandardInput.BaseStream)
        } finally {
            $stream.Dispose()
            $process.StandardInput.Close()
        }
    } catch {
        $copyFailure = $_
        try { $process.StandardInput.Close() } catch { }
    }
    $process.WaitForExit()
    $stdoutText = if ([IO.File]::Exists($stdoutPath)) { [IO.File]::ReadAllText($stdoutPath) } else { '' }
    $stderrText = if ([IO.File]::Exists($stderrPath)) { [IO.File]::ReadAllText($stderrPath) } else { '' }
    if ($null -ne $copyFailure -or $process.ExitCode -ne 0) {
        $diagnostics = [Collections.Generic.List[string]]::new()
        if ($null -ne $copyFailure) {
            $diagnostics.Add((Format-PowerForgeSshDiagnostic -Text $copyFailure.Exception.Message -Label 'Local payload transfer error'))
        }
        foreach ($item in @(
            (Format-PowerForgeSshDiagnostic -Text $stderrText -Label 'Remote stderr'),
            (Format-PowerForgeSshDiagnostic -Text $stdoutText -Label 'Remote stdout')
        )) {
            if (-not [string]::IsNullOrWhiteSpace($item)) {
                $diagnostics.Add($item)
            }
        }
        $detail = if ($diagnostics.Count -gt 0) { "`n" + ($diagnostics -join "`n") } else { '' }
        throw "Restricted SSH service deployment failed with exit code $($process.ExitCode).$detail"
    }
}
