function Invoke-PowerForgeSshTransport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $TransportPath,
        [Parameter(Mandatory)][string] $Target,
        [Parameter(Mandatory)][int] $Port,
        [Parameter(Mandatory)][string[]] $SshOptions,
        [Parameter(Mandatory)][string] $Service
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'ssh'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($SshOptions) + @(
        '-p', [string]$Port, $Target, "powerforge-service-deploy-v1 --service $Service"
    )) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start the restricted SSH deployment transport.'
    }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
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
    [void]$stdout.GetAwaiter().GetResult()
    [void]$stderr.GetAwaiter().GetResult()
    if ($null -ne $copyFailure -or $process.ExitCode -ne 0) {
        throw "Restricted SSH service deployment failed with exit code $($process.ExitCode)."
    }
}
