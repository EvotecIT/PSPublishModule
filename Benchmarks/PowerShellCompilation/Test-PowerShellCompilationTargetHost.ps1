[CmdletBinding()]
param(
    [string] $ModuleAssemblyPath,
    [string] $OutputDirectory,
    [string] $TargetFramework = 'net10.0',
    [string] $ExistingManagedArtifactPath,
    [string] $ExistingNativeAotArtifactPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'Ignore\Benchmarks\PowerShellCompilation\target-host'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$useExistingArtifacts = -not [string]::IsNullOrWhiteSpace($ExistingManagedArtifactPath) -and
    -not [string]::IsNullOrWhiteSpace($ExistingNativeAotArtifactPath)
if (-not $useExistingArtifacts -and [string]::IsNullOrWhiteSpace($ModuleAssemblyPath)) {
    $framework = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'net472' } else { 'net10.0' }
    $ModuleAssemblyPath = Join-Path $repositoryRoot "PSPublishModule\bin\Release\$framework\PSPublishModule.dll"
}
if (-not $useExistingArtifacts) {
    $ModuleAssemblyPath = [System.IO.Path]::GetFullPath($ModuleAssemblyPath)
    if (-not [System.IO.File]::Exists($ModuleAssemblyPath)) {
        throw "Built PSPublishModule assembly was not found: $ModuleAssemblyPath"
    }
}

function Get-CurrentRuntimeIdentifier {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { return "win-$architecture" }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { return "osx-$architecture" }
    return "linux-$architecture"
}

function ConvertTo-ProcessArgument {
    param([AllowEmptyString()] [string] $Value)

    if ($Value -notmatch '[\s"]') { return $Value }
    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append('"')
    [int] $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void] $builder.Append(('\' * ($slashes * 2 + 1)))
            [void] $builder.Append('"')
        } else {
            if ($slashes -gt 0) { [void] $builder.Append(('\' * $slashes)) }
            [void] $builder.Append($character)
        }
        $slashes = 0
    }
    if ($slashes -gt 0) { [void] $builder.Append(('\' * ($slashes * 2))) }
    [void] $builder.Append('"')
    return $builder.ToString()
}

function Start-ContractProcess {
    param([string] $FileName, [string[]] $Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    return [System.Diagnostics.Process]::Start($startInfo)
}

function Complete-ContractProcess {
    param([System.Diagnostics.Process] $Process, [int] $TimeoutMilliseconds = 60000)

    $stdout = $Process.StandardOutput.ReadToEndAsync()
    $stderr = $Process.StandardError.ReadToEndAsync()
    if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
        try { $Process.Kill() } catch { }
        throw "Owned target-host process exceeded $TimeoutMilliseconds milliseconds."
    }
    return [pscustomobject]@{
        ExitCode = $Process.ExitCode
        StandardOutput = $stdout.GetAwaiter().GetResult()
        StandardError = $stderr.GetAwaiter().GetResult()
    }
}

function Invoke-ContractProcess {
    param([string] $FileName, [string[]] $Arguments, [int] $TimeoutMilliseconds = 60000)

    $process = Start-ContractProcess -FileName $FileName -Arguments $Arguments
    try { return Complete-ContractProcess -Process $process -TimeoutMilliseconds $TimeoutMilliseconds }
    finally { $process.Dispose() }
}

function New-ReviewedArtifact {
    param(
        [string] $Name,
        [PowerForge.PowerShellCompilationExecutableOptimization] $Optimization,
        [bool] $SelfContained,
        [string] $RuntimeIdentifier,
        [string] $SourcePath
    )

    $spec = [PowerForge.PowerShellCompilationBuildSpec]::new(
        $SourcePath,
        $OutputDirectory,
        $Name,
        [PowerForge.PowerShellCompilationArtifactKind]::Executable,
        [PowerForge.PowerShellCompilationMode]::Strict)
    $spec.TargetFramework = $TargetFramework
    $spec.RuntimeIdentifier = $RuntimeIdentifier
    $spec.SingleFile = $true
    $spec.SelfContained = $SelfContained
    $spec.Optimization = $Optimization
    $spec.EmitSource = $true
    $planner = [PowerForge.PowerShellCompilationDependencyPlanner]::new()
    $spec.ExpectedDependencyLock = $planner.AnalyzeGraph($spec)
    $result = [PowerForge.PowerShellCompilationArtifactBuilder]::new().Build($spec)
    if (-not $result.Succeeded) {
        throw "$Name build failed: $($result.Error)`n$($result.BuildOutput)"
    }
    return $result
}

function Test-ArtifactContract {
    param([string] $Name, $Build, [string] $RuntimeIdentifier, [string] $ResourcePath)

    $unicode = [string] ('Za' + [char] 0x017c + [char] 0x00f3 + [char] 0x0142 + [char] 0x0107 + '-' + [char] 0x6771 + [char] 0x4eac)
    $expectedResource = [string] ('r' + [char] 0x00e9 + 'source-' + [char] 0x0141 + [char] 0x00f3 + 'd' + [char] 0x017a + '-' + [char] 0x6771 + [char] 0x4eac)
    $normal = Invoke-ContractProcess -FileName $Build.ArtifactPath -Arguments @(
        "--Text=$unicode", "--ResourcePath=$ResourcePath", '--Iterations=5')
    if ($normal.ExitCode -ne 0 -or $normal.StandardOutput.Trim() -ne "$unicode|$expectedResource|10" -or
        -not [string]::IsNullOrWhiteSpace($normal.StandardError)) {
        throw "$Name normal execution contract failed. Exit=$($normal.ExitCode); stdout=$($normal.StandardOutput); stderr=$($normal.StandardError)"
    }

    $invalid = Invoke-ContractProcess -FileName $Build.ArtifactPath -Arguments @("--Text=$unicode")
    if ($invalid.ExitCode -eq 0 -or -not [string]::IsNullOrWhiteSpace($invalid.StandardOutput) -or
        [string]::IsNullOrWhiteSpace($invalid.StandardError)) {
        throw "$Name failure routing contract failed. Exit=$($invalid.ExitCode); stdout=$($invalid.StandardOutput); stderr=$($invalid.StandardError)"
    }

    $longRunning = Start-ContractProcess -FileName $Build.ArtifactPath -Arguments @(
        "--Text=$unicode", "--ResourcePath=$ResourcePath", '--Iterations=30000')
    try {
        Start-Sleep -Milliseconds 300
        if ($longRunning.HasExited) { throw "$Name cancellation fixture exited before interruption." }
        if ($RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
            $longRunning.Kill()
            $signal = 'Kill'
        } else {
            $kill = Invoke-ContractProcess -FileName '/bin/kill' -Arguments @('-TERM', $longRunning.Id.ToString())
            if ($kill.ExitCode -ne 0) { throw "$Name SIGTERM dispatch failed: $($kill.StandardError)" }
            $signal = 'SIGTERM'
        }
        $cancelled = Complete-ContractProcess -Process $longRunning -TimeoutMilliseconds 5000
        if ($cancelled.ExitCode -eq 0) { throw "$Name interruption returned a successful exit code." }
    } finally {
        if (-not $longRunning.HasExited) { try { $longRunning.Kill() } catch { } }
        $longRunning.Dispose()
    }

    $executablePermission = $true
    if (-not $RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
        $permission = Invoke-ContractProcess -FileName '/usr/bin/test' -Arguments @('-x', $Build.ArtifactPath)
        $executablePermission = $permission.ExitCode -eq 0
        if (-not $executablePermission) { throw "$Name does not have the Unix executable bit." }
    }

    return [pscustomobject]@{
        Name = $Name
        ArtifactPath = $Build.ArtifactPath
        Sha256 = $Build.Manifest.ArtifactSha256
        Bytes = $Build.Manifest.ArtifactSizeBytes
        ArtifactFormat = $Build.Manifest.DependencyClosure.ArtifactFormat
        DependencyClosureVerified = $Build.Manifest.DependencyClosureVerified
        NativeExecutable = $Build.Manifest.DependencyClosure.NativeExecutable
        NormalExitCode = $normal.ExitCode
        InvalidArgumentsExitCode = $invalid.ExitCode
        Interruption = $signal
        InterruptedExitCode = $cancelled.ExitCode
        ExecutablePermission = $executablePermission
        UnicodeAndResourceOutput = $normal.StandardOutput.Trim()
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$runtimeIdentifier = Get-CurrentRuntimeIdentifier
$sourcePath = Join-Path $PSScriptRoot 'target-host-contract.ps1'
$resourceDirectory = Join-Path $OutputDirectory 'resource path'
New-Item -ItemType Directory -Path $resourceDirectory -Force | Out-Null
$resourcePath = Join-Path $resourceDirectory 'unicode.txt'
$resourceText = [string] ('r' + [char] 0x00e9 + 'source-' + [char] 0x0141 + [char] 0x00f3 + 'd' + [char] 0x017a + '-' + [char] 0x6771 + [char] 0x4eac)
[System.IO.File]::WriteAllText($resourcePath, $resourceText, [System.Text.UTF8Encoding]::new($false))

if ($useExistingArtifacts) {
    $managedPath = [System.IO.Path]::GetFullPath($ExistingManagedArtifactPath)
    $nativePath = [System.IO.Path]::GetFullPath($ExistingNativeAotArtifactPath)
    $managedManifestPath = [System.IO.Path]::Combine(
        [System.IO.Path]::GetDirectoryName($managedPath),
        [System.IO.Path]::GetFileNameWithoutExtension($managedPath) + '.powerforge-compilation.json')
    $nativeManifestPath = [System.IO.Path]::Combine(
        [System.IO.Path]::GetDirectoryName($nativePath),
        [System.IO.Path]::GetFileNameWithoutExtension($nativePath) + '.powerforge-compilation.json')
    foreach ($requiredPath in @($managedPath, $nativePath, $managedManifestPath, $nativeManifestPath)) {
        if (-not [System.IO.File]::Exists($requiredPath)) { throw "Existing target-host artifact evidence was not found: $requiredPath" }
    }
    $managed = [pscustomobject]@{
        ArtifactPath = $managedPath
        Manifest = (ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($managedManifestPath)))
    }
    $native = [pscustomobject]@{
        ArtifactPath = $nativePath
        Manifest = (ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($nativeManifestPath)))
    }
} else {
    Import-Module -Name $ModuleAssemblyPath -Force
    $managed = New-ReviewedArtifact -Name "PowerForge.TargetHost.Managed.$runtimeIdentifier" `
        -Optimization ([PowerForge.PowerShellCompilationExecutableOptimization]::None) `
        -SelfContained $false -RuntimeIdentifier $runtimeIdentifier -SourcePath $sourcePath
    $native = New-ReviewedArtifact -Name "PowerForge.TargetHost.NativeAot.$runtimeIdentifier" `
        -Optimization ([PowerForge.PowerShellCompilationExecutableOptimization]::NativeAot) `
        -SelfContained $true -RuntimeIdentifier $runtimeIdentifier -SourcePath $sourcePath
}

$checks = @(
    Test-ArtifactContract -Name 'StrictManaged' -Build $managed -RuntimeIdentifier $runtimeIdentifier -ResourcePath $resourcePath
    Test-ArtifactContract -Name 'StrictNativeAot' -Build $native -RuntimeIdentifier $runtimeIdentifier -ResourcePath $resourcePath
)
$evidence = [pscustomobject]@{
    SchemaVersion = 1
    RuntimeIdentifier = $runtimeIdentifier
    OperatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    Architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    TargetFramework = $TargetFramework
    Checks = $checks
}
$evidencePath = Join-Path $OutputDirectory "target-host-$runtimeIdentifier.json"
[System.IO.File]::WriteAllText(
    $evidencePath,
    ($evidence | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))
$evidence
