function Invoke-ImportValidation {
    param([string] $OutputRoot)

    if (-not $ValidateImport.IsPresent -or [string]::IsNullOrWhiteSpace($OutputRoot) -or -not (Test-Path -LiteralPath $OutputRoot)) {
        return $null
    }

    $childScript = Join-Path $PSScriptRoot 'Invoke-ManagedModuleImportChild.ps1'
    $resultPath = [IO.Path]::GetTempFileName()
    $timeoutMilliseconds = [Math]::Max(1, $ImportTimeoutSeconds) * 1000
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $output = @()
    $combinedOutput = ''
    $process = $null

    try {
        $arguments = @(
            '-NoLogo',
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $childScript,
            '-ModuleName',
            $ModuleName,
            '-ModuleRoot',
            $OutputRoot,
            '-ResultPath',
            $resultPath
        )
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = Get-BenchmarkHostPath
        $startInfo.Arguments = ($arguments | ForEach-Object {
                '"' + ([string]$_).Replace('"', '\"') + '"'
            }) -join ' '
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true

        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        [void]$process.Start()

        if (-not $process.WaitForExit($timeoutMilliseconds)) {
            try {
                $process.Kill()
            } catch {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
            return [pscustomobject]@{
                Status = 'TimedOut'
                Version = ''
                ManifestPath = ''
                ElapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
                Error = "Import validation exceeded $ImportTimeoutSeconds seconds."
            }
        }

        $stdoutText = $process.StandardOutput.ReadToEnd()
        $stderrText = $process.StandardError.ReadToEnd()
        $combinedOutput = ($stdoutText + [Environment]::NewLine + $stderrText).Trim()
        $stdout = if ([string]::IsNullOrWhiteSpace($stdoutText)) { @() } else { @($stdoutText -split '\r?\n') }
        $stderr = if ([string]::IsNullOrWhiteSpace($stderrText)) { @() } else { @($stderrText -split '\r?\n') }
        $output = @($stdout + $stderr)

        if ($process.ExitCode -ne 0) {
            Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
            return [pscustomobject]@{
                Status = 'Failed'
                Version = ''
                ManifestPath = ''
                ElapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
                Error = ($output -join [Environment]::NewLine)
            }
        }
    } finally {
        $timer.Stop()
        if ($process) {
            $process.Dispose()
        }
    }

    $normalizedOutput = $combinedOutput -replace "`0", ''
    $jsonText = ''
    try {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if ((Test-Path -LiteralPath $resultPath) -and (Get-Item -LiteralPath $resultPath).Length -gt 0) {
                break
            }

            Start-Sleep -Milliseconds 100
        }

        if (Test-Path -LiteralPath $resultPath) {
            $jsonText = [string](Get-Content -LiteralPath $resultPath -Raw -ErrorAction SilentlyContinue)
        }
    } finally {
        Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    }

    if ([string]::IsNullOrWhiteSpace($jsonText)) {
        $start = $normalizedOutput.IndexOf('{')
        $end = $normalizedOutput.LastIndexOf('}')
        if ($start -ge 0 -and $end -gt $start) {
            $jsonText = $normalizedOutput.Substring($start, $end - $start + 1)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($jsonText)) {
        try {
            return $jsonText | ConvertFrom-Json
        } catch {
            $normalizedOutput = $jsonText
        }
    }

    $statusMatch = [regex]::Match($normalizedOutput, '"Status"\s*:\s*"([^"]*)"')
    if ($statusMatch.Success) {
        $versionMatch = [regex]::Match($normalizedOutput, '"Version"\s*:\s*"([^"]*)"')
        $manifestMatch = [regex]::Match($normalizedOutput, '"ManifestPath"\s*:\s*"([^"]*)"')
        $elapsedMatch = [regex]::Match($normalizedOutput, '"ElapsedMilliseconds"\s*:\s*([0-9.]+)')
        $errorMatch = [regex]::Match($normalizedOutput, '"Error"\s*:\s*"([^"]*)"')
        return [pscustomobject]@{
            Status = $statusMatch.Groups[1].Value
            Version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { '' }
            ManifestPath = if ($manifestMatch.Success) { $manifestMatch.Groups[1].Value -replace '\\\\', '\' } else { '' }
            ElapsedMilliseconds = if ($elapsedMatch.Success) { [double]::Parse($elapsedMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 0 }
            Error = if ($errorMatch.Success) { $errorMatch.Groups[1].Value } else { '' }
        }
    }

    if ([string]::IsNullOrWhiteSpace($jsonText)) {
        return [pscustomobject]@{
            Status = 'Failed'
            Version = ''
            ManifestPath = ''
            ElapsedMilliseconds = 0
            Error = $normalizedOutput
        }
    }

    [pscustomobject]@{
        Status = 'Failed'
        Version = ''
        ManifestPath = ''
        ElapsedMilliseconds = 0
        Error = $normalizedOutput
    }
}
