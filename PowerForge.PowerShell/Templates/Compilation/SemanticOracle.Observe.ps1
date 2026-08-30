param(
    [Parameter(Mandatory)]
    [string] $ConfigPath
)

$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$culture = [System.Globalization.CultureInfo]::GetCultureInfo([string] $config.Culture)
[System.Globalization.CultureInfo]::CurrentCulture = $culture
[System.Globalization.CultureInfo]::CurrentUICulture = $culture

function Get-FileSnapshot {
    param([string] $Root)

    $snapshot = @{}
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) {
        return $snapshot
    }

    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart([char[]] @('\', '/')).Replace('\', '/')
        $snapshot[$relative] = Get-Sha256 -Path $file.FullName
    }
    return $snapshot
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function Get-OperatingSystemFamily {
    if ($PSVersionTable.PSVersion.Major -le 5 -or $IsWindows) { return 'Windows' }
    if ($IsLinux) { return 'Linux' }
    if ($IsMacOS) { return 'macOS' }
    return 'Unknown'
}

function Get-Architecture {
    if ($PSVersionTable.PSVersion.Major -le 5) {
        switch ([string] $env:PROCESSOR_ARCHITECTURE) {
            'AMD64' { return 'x64' }
            'x86' { return 'x86' }
            'ARM64' { return 'Arm64' }
            default { return [string] $env:PROCESSOR_ARCHITECTURE }
        }
    }
    return [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
}

function Get-OperatingSystemVersion {
    if ($PSVersionTable.PSVersion.Major -le 5) {
        return [Environment]::OSVersion.VersionString
    }
    return [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
}

function Get-PropertyObservation {
    param(
        [object] $Value,
        [string[]] $ObservedPropertyNames
    )

    if ($null -eq $Value -or $ObservedPropertyNames.Count -eq 0) {
        return @()
    }

    $properties = foreach ($property in $Value.PSObject.Properties) {
        if ($ObservedPropertyNames -notcontains $property.Name) { continue }
        $propertyValue = $property.Value
        [ordered] @{
            Name = [string] $property.Name
            Value = if ($null -eq $propertyValue) { '' } else { [string] $propertyValue }
            TypeName = if ($null -eq $propertyValue) { '' } else { $propertyValue.GetType().FullName }
            IsNull = $null -eq $propertyValue
        }
    }
    return @($properties)
}

$before = Get-FileSnapshot ([string] $config.FileSystemRoot)
$success = [System.Collections.Generic.List[object]]::new()
$information = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$verbose = [System.Collections.Generic.List[string]]::new()
$debug = [System.Collections.Generic.List[string]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$streamRecords = [System.Collections.Generic.List[object]]::new()
$errorRecords = [System.Collections.Generic.List[object]]::new()
$sequence = 0
$scriptExitCode = $null
$pipelineState = $null
$pipelineReason = $null
$powerShell = $null
$runspace = $null

try {
    $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
    $runspace.Open()
    $runspace.SessionStateProxy.SetVariable('PowerForgeOracleScriptPath', [string] $config.ScriptPath)
    $runspace.SessionStateProxy.SetVariable('PowerForgeOracleArguments', [object[]] @($config.Arguments))
    $powerShell = [System.Management.Automation.PowerShell]::Create()
    $powerShell.Runspace = $runspace
    [void] $powerShell.AddScript(@'
$ErrorActionPreference = 'Continue'
$WarningPreference = 'Continue'
$VerbosePreference = 'Continue'
$DebugPreference = 'Continue'
$InformationPreference = 'Continue'
$global:LASTEXITCODE = $null
& $PowerForgeOracleScriptPath @PowerForgeOracleArguments *>&1
'@)
    $output = @()
    try {
        $output = @($powerShell.Invoke())
    } catch {
        $pipelineState = 'Failed'
        $pipelineReason = if ($null -ne $_.Exception.InnerException) { $_.Exception.InnerException } else { $_.Exception }
    }
    $pipelineState = $powerShell.InvocationStateInfo.State.ToString()
    if ($null -ne $powerShell.InvocationStateInfo.Reason) {
        $pipelineReason = $powerShell.InvocationStateInfo.Reason
    }
    $lastExitCode = $runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($lastExitCode -is [int]) { $scriptExitCode = [int] $lastExitCode }

    foreach ($item in $output) {
        $sequence++
        if ($item -is [System.Management.Automation.ErrorRecord]) {
            $message = [string] $item.Exception.Message
            $errors.Add($message)
            $errorRecords.Add([ordered] @{
                Sequence = $sequence
                Message = $message
                FullyQualifiedErrorId = [string] $item.FullyQualifiedErrorId
                Category = [string] $item.CategoryInfo.Category
                ExceptionTypeName = if ($null -eq $item.Exception) { '' } else { $item.Exception.GetType().FullName }
                TargetTypeName = if ($null -eq $item.TargetObject) { '' } else { $item.TargetObject.GetType().FullName }
                IsTerminating = $false
            })
            continue
        }

        $stream = $null
        $message = $null
        $tags = @()
        if ($item -is [System.Management.Automation.InformationRecord]) {
            $stream = 'Information'
            $message = [string] $item.MessageData
            $tags = @($item.Tags | ForEach-Object { [string] $_ })
            $information.Add($message)
        } elseif ($item -is [System.Management.Automation.WarningRecord]) {
            $stream = 'Warning'
            $message = [string] $item.Message
            $warnings.Add($message)
        } elseif ($item -is [System.Management.Automation.VerboseRecord]) {
            $stream = 'Verbose'
            $message = [string] $item.Message
            $verbose.Add($message)
        } elseif ($item -is [System.Management.Automation.DebugRecord]) {
            $stream = 'Debug'
            $message = [string] $item.Message
            $debug.Add($message)
        }

        if ($null -ne $stream) {
            $streamRecords.Add([ordered] @{
                Sequence = $sequence
                Stream = $stream
                Message = $message
                TypeName = $item.GetType().FullName
                Tags = @($tags)
            })
            continue
        }

        $isNull = $null -eq $item
        $isAutomationNull = $false
        if (-not $isNull) {
            try {
                $isAutomationNull = [object]::ReferenceEquals($item, [System.Management.Automation.Internal.AutomationNull]::Value)
            } catch {
                $isAutomationNull = $false
            }
        }
        $baseValue = if ($isNull) { $null } else { $item.PSObject.BaseObject }
        $success.Add([ordered] @{
            Sequence = $sequence
            Value = if ($isNull) { '' } else { [string] $baseValue }
            TypeName = if ($isNull) { '' } else { $baseValue.GetType().FullName }
            IsNull = $isNull
            IsAutomationNull = $isAutomationNull
            EnumerationState = 'PipelineItem'
            Properties = @(Get-PropertyObservation -Value $item -ObservedPropertyNames @($config.ObservedPropertyNames))
        })
    }

    if ($pipelineState -eq 'Failed' -and $null -ne $pipelineReason) {
        $sequence++
        $message = [string] $pipelineReason.Message
        $reasonError = $pipelineReason.ErrorRecord
        $errors.Add($message)
        $errorRecords.Add([ordered] @{
            Sequence = $sequence
            Message = $message
            FullyQualifiedErrorId = if ($null -eq $reasonError) { '' } else { [string] $reasonError.FullyQualifiedErrorId }
            Category = if ($null -eq $reasonError) { '' } else { [string] $reasonError.CategoryInfo.Category }
            ExceptionTypeName = $pipelineReason.GetType().FullName
            TargetTypeName = if ($null -eq $reasonError -or $null -eq $reasonError.TargetObject) { '' } else { $reasonError.TargetObject.GetType().FullName }
            IsTerminating = $true
        })
    }
} finally {
    if ($null -ne $powerShell) { $powerShell.Dispose() }
    if ($null -ne $runspace) { $runspace.Dispose() }
}

$after = Get-FileSnapshot ([string] $config.FileSystemRoot)
$effects = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($before.Keys + $after.Keys | Sort-Object -Unique)) {
    if (-not $before.ContainsKey($path)) { $effects.Add("Added:${path}:$($after[$path])"); continue }
    if (-not $after.ContainsKey($path)) { $effects.Add("Removed:${path}:$($before[$path])"); continue }
    if ($before[$path] -ne $after[$path]) { $effects.Add("Modified:${path}:$($after[$path])") }
}

$executablePath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$executable = Get-Item -LiteralPath $executablePath
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
$operatingSystem = Get-OperatingSystemFamily
$architecture = Get-Architecture
$hostArtifact = [ordered] @{
    SchemaVersion = 1
    ExecutableName = $executable.Name
    ExecutableSha256 = Get-Sha256 -Path $executablePath
    ExecutableLength = $executable.Length
    ExecutableFileVersion = [string] $fileVersion.FileVersion
    ExecutableProductVersion = [string] $fileVersion.ProductVersion
    HostVersion = $PSVersionTable.PSVersion.ToString()
    BuildVersion = if ($null -eq $PSVersionTable.BuildVersion) { '' } else { $PSVersionTable.BuildVersion.ToString() }
    GitCommitId = if ($null -eq $PSVersionTable.GitCommitId) { '' } else { [string] $PSVersionTable.GitCommitId }
    PowerShellEdition = [string] $PSVersionTable.PSEdition
    OperatingSystem = $operatingSystem
    OperatingSystemVersion = Get-OperatingSystemVersion
    Architecture = $architecture
    Culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
    UICulture = [System.Globalization.CultureInfo]::CurrentUICulture.Name
    FeatureSwitches = @($config.FeatureSwitches)
    IdentitySha256 = ''
}

$envelope = [ordered] @{
    SchemaVersion = 2
    ProfileId = [string] $config.ProfileId
    ExecutionSurface = [string] $config.ExecutionSurface
    HostVersion = $hostArtifact.HostVersion
    PowerShellEdition = $hostArtifact.PowerShellEdition
    OperatingSystem = $hostArtifact.OperatingSystem
    Architecture = $hostArtifact.Architecture
    Culture = $hostArtifact.Culture
    HostArtifact = $hostArtifact
    Success = @($success)
    Information = @($information)
    Warnings = @($warnings)
    Verbose = @($verbose)
    Debug = @($debug)
    StreamRecords = @($streamRecords)
    Errors = @($errors)
    ErrorRecords = @($errorRecords)
    ExitCode = $scriptExitCode
    FileSystemEffects = @($effects)
    ProcessEffects = @()
}
$envelope | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath ([string] $config.OutputPath) -Encoding utf8
