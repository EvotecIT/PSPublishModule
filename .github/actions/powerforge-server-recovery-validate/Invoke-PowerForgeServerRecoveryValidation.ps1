[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'GitHub Actions workflow commands require host output.')]
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Assert-ProcessSucceeded {
    param(
        [Parameter(Mandatory)][pscustomobject] $Result,
        [Parameter(Mandatory)][string] $Operation
    )

    if ($Result.ExitCode -ne 0) {
        throw "$Operation failed with exit code $($Result.ExitCode). Run the pinned PowerForge CLI locally for detailed diagnostics."
    }
}

function Assert-PathHasNoSymbolicLink {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )

    $relativePath = [IO.Path]::GetRelativePath($Root, $Path)
    $currentPath = $Root
    foreach ($segment in $relativePath.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
        $currentPath = Join-Path $currentPath $segment
        $item = Get-Item -LiteralPath $currentPath -Force
        if ($item.LinkType -eq 'SymbolicLink' -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'manifest-path and its parent directories must not be symbolic links.'
        }
    }
}

function Invoke-ProcessCapture {
    param(
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new($FileName)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        [void] $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout   = $stdoutTask.GetAwaiter().GetResult()
            Stderr   = $stderrTask.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-PowerForgeJson {
    param(
        [Parameter(Mandatory)][string] $CliPath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Operation
    )

    $result = Invoke-ProcessCapture -FileName 'dotnet' -Arguments (@($CliPath) + $Arguments)
    Assert-ProcessSucceeded -Result $result -Operation $Operation
    try {
        $envelope = $result.Stdout | ConvertFrom-Json -Depth 100
    } catch {
        throw "$Operation did not return a valid PowerForge JSON envelope."
    }
    if (-not $envelope.success) {
        throw "$Operation reported failure. Run the pinned PowerForge CLI locally for detailed diagnostics."
    }
    $envelope
}

function Write-ActionOutput {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Value
    )

    "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

function Write-ActionWarning {
    param([Parameter(Mandatory)][string] $Message)

    $escaped = $Message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Host "::warning::$escaped"
}

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge server recovery validation requires a Linux runner so generated scripts can be checked with Bash and ShellCheck.'
}
if ($env:POWERFORGE_FAIL_ON_WARNINGS -notin @('true', 'false')) {
    throw 'fail-on-warnings must be true or false.'
}
if ($env:POWERFORGE_ENGINE_REF -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'The PowerForge recovery validation action must be pinned to an exact 40-character commit.'
}
if ($env:POWERFORGE_ENGINE_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'The action repository must resolve to an owner/repository name.'
}

$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
if ([IO.Path]::IsPathRooted($env:POWERFORGE_MANIFEST_PATH)) {
    throw 'manifest-path must be repository-relative.'
}
$manifestPath = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_MANIFEST_PATH))
if (-not $manifestPath.StartsWith($workspacePrefix, [StringComparison]::Ordinal) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'manifest-path must identify a file inside the caller repository.'
}
Assert-PathHasNoSymbolicLink -Root $workspace -Path $manifestPath

$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$runnerTempPrefix = $runnerTemp + [IO.Path]::DirectorySeparatorChar
$validationRoot = [IO.Path]::GetFullPath((Join-Path $runnerTemp ('powerforge-server-recovery-validate-' + [Guid]::NewGuid().ToString('N'))))
if (-not $validationRoot.StartsWith($runnerTempPrefix, [StringComparison]::Ordinal)) {
    throw 'Validation output escaped the runner temporary directory.'
}

try {
    New-Item -ItemType Directory -Path $validationRoot | Out-Null
    $artifactsRoot = Join-Path $validationRoot 'artifacts'
    $bootstrapRoot = Join-Path $validationRoot 'bootstrap'
    $restoreRoot = Join-Path $validationRoot 'restore-secrets'

    $project = Join-Path $engineRoot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
    $build = Invoke-ProcessCapture -FileName 'dotnet' -Arguments @(
        'build', $project, '-c', 'Release', '-f', 'net10.0', '--nologo', '--artifacts-path', $artifactsRoot
    )
    Assert-ProcessSucceeded -Result $build -Operation 'Building the pinned PowerForge recovery CLI'
    $cli = Join-Path $artifactsRoot 'bin/PowerForge.Web.Cli/release/PowerForge.Web.Cli.dll'
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        throw "PowerForge recovery CLI was not produced: $cli"
    }

    $manifestJson = Get-Content -LiteralPath $manifestPath -Raw
    $schemaPath = Join-Path $engineRoot 'Schemas/powerforge.web.serverrecovery.schema.json'
    try {
        $schemaValid = $manifestJson | Test-Json -SchemaFile $schemaPath -ErrorAction Stop
    } catch {
        throw 'The recovery manifest does not satisfy the pinned PowerForge server recovery schema.'
    }
    if (-not $schemaValid) {
        throw 'The recovery manifest does not satisfy the pinned PowerForge server recovery schema.'
    }
    $manifest = $manifestJson | ConvertFrom-Json -Depth 100
    $expectedSchemaUrl = "https://raw.githubusercontent.com/$($env:POWERFORGE_ENGINE_REPOSITORY)/$($env:POWERFORGE_ENGINE_REF)/Schemas/powerforge.web.serverrecovery.schema.json"
    if (-not [string]::Equals([string]$manifest.'$schema', $expectedSchemaUrl, [StringComparison]::Ordinal)) {
        throw 'The recovery manifest schema URL must match the exact repository and commit pinned by this action.'
    }
    $bootstrap = Invoke-PowerForgeJson -CliPath $cli -Operation 'Generating the bootstrap plan' -Arguments @(
        'server', 'bootstrap-plan', '--manifest', $manifestPath, '--out', $bootstrapRoot, '--output', 'json'
    )
    $bootstrapScript = Join-Path $bootstrapRoot 'bootstrap-plan.sh'
    $bashResult = Invoke-ProcessCapture -FileName 'bash' -Arguments @('-n', '--', $bootstrapScript)
    Assert-ProcessSucceeded -Result $bashResult -Operation 'Parsing the generated bootstrap script with Bash'
    $shellCheckResult = Invoke-ProcessCapture -FileName 'shellcheck' -Arguments @('-S', 'warning', '--', $bootstrapScript)
    Assert-ProcessSucceeded -Result $shellCheckResult -Operation 'Checking the generated bootstrap script with ShellCheck'

    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($warning in @($bootstrap.result.warnings)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$warning)) {
            $warnings.Add([string]$warning)
        }
    }

    $secretCount = @($manifest.secrets).Count
    $encryptedCaptureCount = @($manifest.capture.encryptedFiles).Count
    if ($secretCount -gt 0 -or $encryptedCaptureCount -gt 0) {
        $restore = Invoke-PowerForgeJson -CliPath $cli -Operation 'Generating the secret-restore plan' -Arguments @(
            'server', 'restore-secrets-plan', '--manifest', $manifestPath, '--out', $restoreRoot,
            '--archive', 'encrypted-secrets.tar.gz.age', '--output', 'json'
        )
        $restoreScript = Join-Path $restoreRoot 'restore-secrets.sh'
        $bashResult = Invoke-ProcessCapture -FileName 'bash' -Arguments @('-n', '--', $restoreScript)
        Assert-ProcessSucceeded -Result $bashResult -Operation 'Parsing the generated secret-restore script with Bash'
        $shellCheckResult = Invoke-ProcessCapture -FileName 'shellcheck' -Arguments @('-S', 'warning', '--', $restoreScript)
        Assert-ProcessSucceeded -Result $shellCheckResult -Operation 'Checking the generated secret-restore script with ShellCheck'
        foreach ($warning in @($restore.result.warnings)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$warning)) {
                $warnings.Add([string]$warning)
            }
        }
    }

    foreach ($warning in $warnings) {
        Write-ActionWarning -Message $warning
    }
    if ($warnings.Count -gt 0 -and $env:POWERFORGE_FAIL_ON_WARNINGS -eq 'true') {
        throw "Recovery plan generation emitted $($warnings.Count) warning(s)."
    }

    $bootstrapStepCount = @($bootstrap.result.steps).Count
    $managedSourceCount = @(
        @($manifest.paths)
        @($manifest.apache.sites)
        @($manifest.apache.conf)
        @($manifest.systemd.services)
        @($manifest.systemd.timers)
    ).Where({ -not [string]::IsNullOrWhiteSpace([string]$_.source) }).Count
    Write-ActionOutput -Name 'bootstrap-step-count' -Value ([string]$bootstrapStepCount)
    Write-ActionOutput -Name 'managed-source-count' -Value ([string]$managedSourceCount)
    Write-ActionOutput -Name 'secret-count' -Value ([string]$secretCount)
    Write-ActionOutput -Name 'warning-count' -Value ([string]$warnings.Count)

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        @"
## PowerForge server recovery validation

- Bootstrap steps: $bootstrapStepCount
- Managed repository sources: $managedSourceCount
- Declared secrets: $secretCount
- Generation warnings: $($warnings.Count)

Generated shell plans were parsed by Bash and checked by ShellCheck. No generated command was executed.
"@ | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
} finally {
    if ($validationRoot.StartsWith($runnerTempPrefix, [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $validationRoot)) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}
