function Get-CorpusSourceHost {
    param([string] $WorkingDirectory)

    $launcher = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $script = @'
[Console]::Out.Write(([ordered]@{
    executable = [Environment]::ProcessPath
    powerShell = $PSVersionTable.PSVersion.ToString()
    runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    runtimeRoot = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
    sma = [System.Management.Automation.PSObject].Assembly.Location
    architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
} | ConvertTo-Json -Compress))
'@
    $probe = Invoke-OwnedProcess -FileName $launcher -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand',
        [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 60
    if ($probe.ExitCode -ne 0) { throw 'Cannot identify the source PowerShell host before timing.' }
    $identity = $probe.StandardOutput | ConvertFrom-Json
    $coreClr = Join-Path $identity.runtimeRoot $(if ($IsWindows) { 'coreclr.dll' } else { 'libcoreclr.so' })
    $files = @(
        @{ role = 'Executable'; path = $identity.executable },
        @{ role = 'CoreCLR'; path = $coreClr },
        @{ role = 'PowerShellAssembly'; path = $identity.sma }
    )
    foreach ($file in $files) { $file.sha256 = (Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash.ToLowerInvariant() }
    return [pscustomobject]@{
        executablePath = $identity.executable
        files = $files
        metadata = [ordered]@{
            capturedBeforeTimingUtc = [DateTimeOffset]::UtcNow.ToString('o')
            powerShell = $identity.powerShell
            runtime = $identity.runtime
            architecture = $identity.architecture
            operatingSystem = $identity.operatingSystem
            executablePath = '<PowerShellHostRoot>/' + [IO.Path]::GetFileName($identity.executable)
            runtimeLocation = '<SourceRuntimeRoot>'
            runtimeLayout = 'Installation prefixes omitted; actual executable and loaded runtime identified before timing.'
            runtimeFileSystem = Get-CorpusRuntimeFileSystem -Path $coreClr -WorkingDirectory $WorkingDirectory
            files = @($files | ForEach-Object { [ordered]@{ role = $_.role; name = [IO.Path]::GetFileName($_.path); sha256 = $_.sha256 } })
        }
    }
}

function Assert-CorpusSourceHostUnchanged {
    param([object] $SourceHost)

    foreach ($file in $SourceHost.files) {
        if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw 'The source PowerShell host changed during timing; samples cannot be accepted.'
        }
    }
}
