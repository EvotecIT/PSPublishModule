param(
    [Parameter(Mandatory)][string] $ModulePath,
    [Parameter(Mandatory)][string] $DefinitionPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [ValidateSet('preflight', 'public')][string] $Case = 'public'
)
$ErrorActionPreference = 'Stop'
$localName = [Environment]::MachineName
if ([string]::IsNullOrWhiteSpace($localName) -or
    $localName -cne $env:COMPUTERNAME) {
    throw 'The local computer identity is inconsistent.'
}
if ('Win32Share.NativeMethods' -as [type]) {
    throw 'Use a fresh child process without the authored native types.'
}

function Get-LocalBaseline {
    $buffer = [IntPtr]::Zero
    [uint32] $read = 0
    [uint32] $total = 0
    [uint32] $resume = 0
    try {
        $status = [Win32Share.NativeMethods]::NetShareEnum(
            $localName, 1, [ref] $buffer, [uint32]::MaxValue,
            [ref] $read, [ref] $total, [ref] $resume)
        if ($status -ne 0 -or $read -eq 0 -or $read -ne $total) {
            throw "Local enumeration is not complete: status=$status read=$read total=$total"
        }
        $pointer = $buffer
        $records = @(
            for ($i = 0; $i -lt $read; $i++) {
                $entry = [Runtime.InteropServices.Marshal]::PtrToStructure(
                    $pointer, [type] [Win32Share.NativeHelpers+SHARE_INFO_1])
                [pscustomobject]@{
                    PSTypeName = 'Win32Share.NativeMethods'
                    ComputerName = $localName
                    Path = "\\$localName\$($entry.shi1_netname)\"
                    Name = $entry.shi1_netname
                    Type = $entry.shi1_type
                    Remark = $entry.shi1_remark
                }
                $pointer = [IntPtr]::Add($pointer,
                    [Runtime.InteropServices.Marshal]::SizeOf($entry))
            }
        )
        return $records
    } finally {
        if ($buffer -ne [IntPtr]::Zero -and
            [Win32Share.NativeMethods]::NetApiBufferFree($buffer) -ne 0) {
            throw 'The independent local buffer could not be freed.'
        }
    }
}

function Convert-Record($record) {
    [ordered]@{
        name = $record.Name
        remark = $record.Remark
        type = [uint32] $record.Type
        typeIdentity = $record.Type.GetType().FullName
        computer = $record.ComputerName
        path = $record.Path
        properties = @($record.PSObject.Properties.Name)
        psType = $record.PSObject.TypeNames[0]
    }
}

if ($Case -eq 'preflight') {
    Add-Type -TypeDefinition ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $DefinitionPath).Path))
    $baseline = @(Get-LocalBaseline)
    $summary = [ordered]@{
        case = $Case
        hostVersion = $PSVersionTable.PSVersion.ToString()
        localIdentity = $true
        completeLocalEntries = $baseline.Count
    }
} else {
    $module = Import-Module $ModulePath -Force -PassThru
    try {
        $publicCommand = $module.ExportedCommands['Get-ComputerSMBShareList']
        if ($null -eq $publicCommand) { throw 'The imported module did not export the public command.' }
        if ('Win32Share.NativeMethods' -as [type]) {
            throw 'Module import unexpectedly loaded the authored type.'
        }
        $warnings = @()
        $first = @(& $publicCommand -ComputerName $localName -SkipDiskSpace -WarningVariable warnings -WarningAction SilentlyContinue -ErrorAction Stop)
        if ($warnings.Count -ne 0 -or -not ('Win32Share.NativeMethods' -as [type])) {
            throw 'The public begin block did not load the authored type cleanly.'
        }
        $baseline = @(Get-LocalBaseline)
        $selected = $baseline[0].Name
        $probes = @(
            @{label = 'all'; names = @(); result = $first; expected = $baseline},
            @{label = 'repeat'; names = @(); expected = $baseline},
            @{label = 'wildcard'; names = @('*'); expected = $baseline},
            @{label = 'exact'; names = @([WildcardPattern]::Escape($selected));
                expected = @($baseline | Where-Object { $_.Name -ceq $selected })},
            @{label = 'missing'; names = @('__PFC_NO_SUCH_SHARE_61c8__'); expected = @()}
        )
        $observations = @(
            foreach ($probe in $probes) {
                $warnings = @()
                if ($probe.ContainsKey('result')) {
                    $actual = @($probe.result)
                } else {
                    $arguments = @{ComputerName = $localName; SkipDiskSpace = $true;
                        ErrorAction = 'Stop'; WarningAction = 'SilentlyContinue';
                        WarningVariable = 'warnings'}
                    if ($probe.names.Count -gt 0) { $arguments.Name = $probe.names }
                    $actual = @(& $publicCommand @arguments)
                }
                $actualValue = ConvertTo-Json -InputObject @($actual | ForEach-Object {
                    Convert-Record $_ }) -Depth 8 -Compress
                $expectedValue = ConvertTo-Json -InputObject @($probe.expected | ForEach-Object {
                    Convert-Record $_ }) -Depth 8 -Compress
                if ($warnings.Count -ne 0 -or $actualValue -cne $expectedValue) {
                    throw "Public local SMB output differed from the independent baseline: $($probe.label)"
                }
                [ordered]@{case = $probe.label; records = $actual.Count;
                    matchesIndependentBaseline = $true; warnings = 0}
            }
        )
        $summary = [ordered]@{
            case = $Case
            hostVersion = $PSVersionTable.PSVersion.ToString()
            scope = 'verified local computer; SkipDiskSpace; no UNC open'
            authoredTypeLoadedByPublicBegin = $true
            completeLocalEntries = $baseline.Count
            observations = $observations
        }
    } finally {
        Remove-Module $module -Force
    }
}
$summary | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $OutputPath -Encoding UTF8
