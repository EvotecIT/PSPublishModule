param(
    [Parameter(Mandatory)]
    [string] $ConfigPath
)

$ErrorActionPreference = 'Stop'
trap {
    [Console]::Error.WriteLine("Semantic oracle wrapper failure: $($_.Exception.Message)")
    [Console]::Error.WriteLine([string] $_.InvocationInfo.PositionMessage)
    [Console]::Error.WriteLine([string] $_.ScriptStackTrace)
    exit 99
}
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$maximumObservationItems = 1024
if ($null -eq ('PowerForge.SemanticOraclePropertyObserver' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
namespace PowerForge {
    public sealed class SemanticOraclePropertyObservation {
        public string Name { get; set; }
        public string Value { get; set; }
        public string TypeName { get; set; }
        public bool IsNull { get; set; }
        public bool IsAutomationNull { get; set; }
        public string ValueState { get; set; }
        public string EnumerationState { get; set; }
        public int? CollectionCardinality { get; set; }
        public string[] ElementTypeNames { get; set; }

        public SemanticOraclePropertyObservation() {
            Name = String.Empty;
            Value = String.Empty;
            TypeName = String.Empty;
            ValueState = "Value";
            EnumerationState = "Scalar";
            ElementTypeNames = new string[0];
        }
    }

    public static class SemanticOraclePropertyObserver {
        public static SemanticOraclePropertyObservation[] Observe(PSObject input, string[] observedNames, int maximumItems) {
            if (input == null) return new SemanticOraclePropertyObservation[0];
            if (observedNames == null) observedNames = new string[0];
            var names = new HashSet<string>(observedNames, StringComparer.OrdinalIgnoreCase);
            var result = new List<SemanticOraclePropertyObservation>();
            foreach (PSPropertyInfo property in input.Properties) {
                if (!names.Contains(property.Name)) continue;
                if (result.Count >= maximumItems) throw new InvalidOperationException("Observed property count exceeds the semantic observation limit.");
                result.Add(ObserveProperty(property, maximumItems));
            }
            return result.ToArray();
        }

        private static SemanticOraclePropertyObservation ObserveProperty(PSPropertyInfo property, int maximumItems) {
            var result = new SemanticOraclePropertyObservation();
            result.Name = property.Name;
            var value = property.Value;
            if (Object.ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value)) {
                result.IsAutomationNull = true;
                result.ValueState = "AutomationNull";
                result.TypeName = "System.Management.Automation.Internal.AutomationNull";
                return result;
            }
            if (value == null) {
                result.IsNull = true;
                result.ValueState = "Null";
                return result;
            }
            result.Value = (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.CurrentCulture);
            result.TypeName = value.GetType().FullName;
            var dictionary = value as IDictionary;
            if (dictionary != null) {
                EnsureCount(dictionary.Count, maximumItems);
                result.EnumerationState = "Dictionary";
                result.CollectionCardinality = dictionary.Count;
                result.ElementTypeNames = GetElementTypeNames(dictionary.Values, maximumItems);
                return result;
            }
            var collection = value as ICollection;
            if (collection != null && !(value is string)) {
                EnsureCount(collection.Count, maximumItems);
                result.EnumerationState = "Collection";
                result.CollectionCardinality = collection.Count;
                result.ElementTypeNames = GetElementTypeNames(collection, maximumItems);
                return result;
            }
            if (value is IEnumerable && !(value is string)) result.EnumerationState = "Collection";
            return result;
        }

        private static string[] GetElementTypeNames(IEnumerable values, int maximumItems) {
            var result = new List<string>();
            var count = 0;
            foreach (var item in values) {
                count++;
                EnsureCount(count, maximumItems);
                var name = item == null ? "Null" : item.GetType().FullName;
                if (!result.Contains(name, StringComparer.Ordinal)) result.Add(name);
            }
            return result.ToArray();
        }

        private static void EnsureCount(int count, int maximumItems) {
            if (count > maximumItems) throw new InvalidOperationException("Semantic observation exceeds the bounded collection limit.");
        }
    }
}
'@
}
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

function Get-ValueShape {
    param([object] $Value)

    if ($null -eq $Value) {
        return [ordered] @{
            ValueState = 'Null'
            EnumerationState = 'Scalar'
            CollectionCardinality = $null
            ElementTypeNames = @()
        }
    }
    $enumerationState = 'Scalar'
    $cardinality = $null
    $elementTypes = @()
    if ($Value -is [System.Collections.IDictionary]) {
        $enumerationState = 'Dictionary'
        $cardinality = [int] $Value.Count
        $elementTypes = @(Get-ElementTypeNames -Values $Value.Values)
    } elseif ($Value -is [System.Collections.ICollection] -and -not ($Value -is [string])) {
        $enumerationState = 'Collection'
        $cardinality = [int] $Value.Count
        $elementTypes = @(Get-ElementTypeNames -Values $Value)
    } elseif ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        $enumerationState = 'Collection'
    }
    return [ordered] @{
        ValueState = 'Value'
        EnumerationState = $enumerationState
        CollectionCardinality = $cardinality
        ElementTypeNames = @($elementTypes)
    }
}

function Get-ElementTypeNames {
    param([System.Collections.IEnumerable] $Values)

    $types = [System.Collections.Generic.List[string]]::new()
    $count = 0
    foreach ($item in $Values) {
        $count++
        if ($count -gt $maximumObservationItems) {
            throw "Semantic observation exceeds the $maximumObservationItems-item collection limit."
        }
        $typeName = if ($null -eq $item) { 'Null' } else { $item.GetType().FullName }
        if (-not $types.Contains($typeName)) { $types.Add($typeName) }
    }
    return @($types)
}

function Get-EncodingWebName {
    param([scriptblock] $Factory)

    try {
        $encoding = & $Factory
        if ($null -eq $encoding) { return '' }
        $webName = [string] $encoding.WebName
        if ([string]::IsNullOrEmpty($webName)) { return '' }
        return $webName.ToLowerInvariant()
    } catch {
        return ''
    }
}

function Get-NativeArgumentPassing {
    $variable = Get-Variable -Name PSNativeCommandArgumentPassing -ErrorAction SilentlyContinue
    if ($null -eq $variable) { return '' }
    return [string] $variable.Value
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
$lastExitCode = $null
$pipelineState = $null
$pipelineReason = $null
$powerShell = $null
$runspace = $null

try {
    $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
    $runspace.Open()
    $runspace.SessionStateProxy.SetVariable('PowerForgeOracleScriptPath', [string] $config.ScriptPath)
    $runspace.SessionStateProxy.SetVariable('PowerForgeOracleArguments', [object[]] @($config.Arguments))
    $runspace.SessionStateProxy.SetVariable('PowerForgeOracleObservedPropertyNames', [string[]] @($config.ObservedPropertyNames))
    $powerShell = [System.Management.Automation.PowerShell]::Create()
    $powerShell.Runspace = $runspace
    [void] $powerShell.AddScript(@'
$ErrorActionPreference = 'Continue'
$WarningPreference = 'Continue'
$VerbosePreference = 'Continue'
$DebugPreference = 'Continue'
$InformationPreference = 'Continue'
$global:LASTEXITCODE = $null
$powerForgeObservedPropertyNamesSnapshot = [string[]] @($PowerForgeOracleObservedPropertyNames)
if ($powerForgeObservedPropertyNamesSnapshot.Count -eq 0) {
    & $PowerForgeOracleScriptPath @PowerForgeOracleArguments *>&1
} else {
    & $PowerForgeOracleScriptPath @PowerForgeOracleArguments *>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord] -or
            $_ -is [System.Management.Automation.InformationRecord] -or
            $_ -is [System.Management.Automation.WarningRecord] -or
            $_ -is [System.Management.Automation.VerboseRecord] -or
            $_ -is [System.Management.Automation.DebugRecord]) {
            $_
        } else {
            [pscustomobject] [ordered] @{
                PowerForgeSemanticPreBoundary = $true
                Value = $_
                Properties = @([PowerForge.SemanticOraclePropertyObserver]::Observe(
                    [System.Management.Automation.PSObject]::AsPSObject($_),
                    $powerForgeObservedPropertyNamesSnapshot,
                    1024))
            }
        }
    }
}
'@)
    $output = @()
    try {
        $output = @($powerShell.Invoke())
        if ($output.Count -gt $maximumObservationItems) {
            throw "Semantic observation exceeds the $maximumObservationItems-item success/stream limit."
        }
    } catch {
        $pipelineState = 'Failed'
        $pipelineReason = if ($null -ne $_.Exception.InnerException) { $_.Exception.InnerException } else { $_.Exception }
    }
    $pipelineState = $powerShell.InvocationStateInfo.State.ToString()
    if ($null -ne $powerShell.InvocationStateInfo.Reason) {
        $pipelineReason = $powerShell.InvocationStateInfo.Reason
    }
    $lastExitCode = $runspace.SessionStateProxy.GetVariable('LASTEXITCODE')

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

        $preBoundaryProperty = if ($null -eq $item) { $null } else { $item.PSObject.Properties['PowerForgeSemanticPreBoundary'] }
        $isPreBoundary = $null -ne $preBoundaryProperty -and [bool] $preBoundaryProperty.Value
        $semanticItem = if ($isPreBoundary) { $item.PSObject.Properties['Value'].Value } else { $item }
        $itemShape = Get-ValueShape -Value $semanticItem
        $baseValue = if ($itemShape.ValueState -eq 'Value') { $semanticItem.PSObject.BaseObject } else { $null }
        $shape = if ($itemShape.ValueState -eq 'Value') { Get-ValueShape -Value $baseValue } else { $itemShape }
        $properties = if ($isPreBoundary) {
            @($item.PSObject.Properties['Properties'].Value)
        } else {
            @()
        }
        $success.Add([ordered] @{
            Sequence = $sequence
            Value = if ($shape.ValueState -eq 'Value') { [string] $baseValue } else { '' }
            TypeName = if ($shape.ValueState -eq 'Value') { $baseValue.GetType().FullName } elseif ($shape.ValueState -eq 'AutomationNull') { 'System.Management.Automation.Internal.AutomationNull' } else { '' }
            IsNull = $shape.ValueState -eq 'Null'
            IsAutomationNull = $shape.ValueState -eq 'AutomationNull'
            ValueState = $shape.ValueState
            EnumerationState = $shape.EnumerationState
            CollectionCardinality = $shape.CollectionCardinality
            ElementTypeNames = @($shape.ElementTypeNames)
            Properties = @($properties)
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
$encoding = [ordered] @{
    ConsoleInput = Get-EncodingWebName { [Console]::InputEncoding }
    ConsoleOutput = Get-EncodingWebName { [Console]::OutputEncoding }
    PowerShellOutput = Get-EncodingWebName { $OutputEncoding }
    ObservationFile = if ($PSVersionTable.PSVersion.Major -le 5) { 'utf-8-bom' } else { 'utf-8' }
    NativeArgumentPassing = Get-NativeArgumentPassing
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
    SchemaVersion = 3
    ProfileId = [string] $config.ProfileId
    ExecutionSurface = [string] $config.ExecutionSurface
    HostVersion = $hostArtifact.HostVersion
    PowerShellEdition = $hostArtifact.PowerShellEdition
    OperatingSystem = $hostArtifact.OperatingSystem
    Architecture = $hostArtifact.Architecture
    Culture = $hostArtifact.Culture
    HostArtifact = $hostArtifact
    Success = @($success)
    SuccessState = if ($success.Count -eq 0) { 'NoOutput' } else { 'Output' }
    Information = @($information)
    Warnings = @($warnings)
    Verbose = @($verbose)
    Debug = @($debug)
    StreamRecords = @($streamRecords)
    Errors = @($errors)
    ErrorRecords = @($errorRecords)
    ExitCode = $null
    FileSystemEffects = @($effects)
    Encoding = $encoding
    ProcessState = [ordered] @{
        LastExitCode = if ($lastExitCode -is [int]) { [int] $lastExitCode } else { $null }
    }
    ProcessEffects = @()
}
$envelope | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath ([string] $config.OutputPath) -Encoding utf8
