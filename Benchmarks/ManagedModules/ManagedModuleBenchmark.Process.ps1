function Join-ManagedBenchmarkCommandLineArgument {
    param([string] $Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    '"' + ($Value -replace '"', '\"') + '"'
}

function Stop-ManagedBenchmarkProcessTree {
    param([Diagnostics.Process] $Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
        return
    }

    $Process.Kill()
}

function Invoke-ManagedBenchmarkProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = '',

        [hashtable] $Environment = @{},

        [int] $TimeoutSeconds = 0,

        [string] $TimeoutMessage = 'Benchmark child process timed out.'
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = ($Arguments | ForEach-Object { Join-ManagedBenchmarkCommandLineArgument $_ }) -join ' '
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = $WorkingDirectory
    }

    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = $entry.Value
    }

    $process = $null
    $timedOut = $false
    $exitCode = -1
    $stdout = ''
    $stderr = ''
    try {
        $process = [Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timeoutMilliseconds = [Math]::Max(0, $TimeoutSeconds) * 1000
        $finished = if ($timeoutMilliseconds -gt 0) {
            $process.WaitForExit($timeoutMilliseconds)
        } else {
            $process.WaitForExit()
            $true
        }

        if (-not $finished) {
            $timedOut = $true
            Stop-ManagedBenchmarkProcessTree -Process $process
            $null = $process.WaitForExit(5000)
        } else {
            $process.WaitForExit()
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not $timedOut) {
            $exitCode = $process.ExitCode
        }
    } finally {
        if ($process) {
            $process.Dispose()
        }
    }

    [pscustomobject]@{
        FileName = $FileName
        Arguments = $startInfo.Arguments
        CommandLine = @(($FileName), $startInfo.Arguments) -join ' '
        ExitCode = $exitCode
        TimedOut = $timedOut
        TimeoutSeconds = $TimeoutSeconds
        TimeoutMessage = if ($timedOut) { $TimeoutMessage } else { '' }
        StandardOutput = $stdout
        StandardError = $stderr
    }
}
