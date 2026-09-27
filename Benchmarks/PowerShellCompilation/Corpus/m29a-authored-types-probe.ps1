# Offline reflection workflow. Original source stays external; generated artifacts use ModulePath.
param([string]$SourcePath, [string]$ModulePath)
$ErrorActionPreference = 'Stop'
if ($ModulePath) {
    Import-Module $ModulePath -Force -ErrorAction Stop
} else {
    if (-not $SourcePath) { throw 'SourcePath or ModulePath is required.' }
    . $SourcePath
}
$cases = @(
    @{ Id = 'substring-bracketed'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = '[System.String]'; MethodName = 'Substring' } },
    @{ Id = 'substring-plain'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = 'System.String'; MethodName = 'Substring' } },
    @{ Id = 'date-method'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = '[System.DateTime]'; MethodName = 'AddDays' } },
    @{ Id = 'method-case-sensitive'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = '[System.String]'; MethodName = 'substring' } },
    @{ Id = 'missing-type'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = '[Offline.Missing.Type]'; MethodName = 'Substring' } },
    @{ Id = 'null-type'; Command = 'Get-MemberMethod'; Parameters = @{ TypeName = $null; MethodName = 'Substring' } },
    @{ Id = 'pipeline-property-transform'; Command = 'Get-MemberMethod'; Parameters = @{}; InputRecords = @(
        [pscustomobject]@{ TypeName = '[System.String]'; MethodName = 'Substring' },
        [pscustomobject]@{ TypeName = '[System.DateTime]'; MethodName = 'AddDays' }
    ) },
    @{ Id = 'string-constructors'; Command = 'Get-TypeConstructor'; Parameters = @{ TypeName = '[System.String]' } },
    @{ Id = 'static-type-constructors'; Command = 'Get-TypeConstructor'; Parameters = @{ TypeName = '[System.Math]' } },
    @{ Id = 'member-property'; Command = 'Get-TypeMember'; Parameters = @{ TypeName = '[System.String]'; MemberName = 'Length' } },
    @{ Id = 'member-method'; Command = 'Get-TypeMember'; Parameters = @{ TypeName = '[System.String]'; MemberName = 'Substring' } }
)
$observations = foreach ($case in $cases) {
    $warnings = @()
    $errorRecord = $null
    $output = @()
    try {
        $command = $case.Command
        $parameters = $case.Parameters
        if ($case.ContainsKey('InputRecords')) {
            $output = @($case.InputRecords | & $command @parameters -WarningVariable warnings -WarningAction SilentlyContinue)
        } else {
            $output = @(& $command @parameters -WarningVariable warnings -WarningAction SilentlyContinue)
        }
    } catch { $errorRecord = $_ }
    $records = foreach ($item in $output) {
        if ($item -is [string]) {
            [ordered]@{ Kind = 'string'; Value = $item }
        } elseif ($case.Command -eq 'Get-TypeConstructor') {
            [ordered]@{
                Kind = 'constructor'; TypeNames = @($item.PSObject.TypeNames); Type = $item.Type
                Parameters = @($item.Parameters | ForEach-Object {
                    [ordered]@{ Type = $_.ParameterType.FullName; Name = $_.ParameterName }
                })
            }
        } else {
            [ordered]@{
                Kind = 'member'; TypeNames = @($item.PSObject.TypeNames); Type = $item.Type
                Name = $item.Name; MemberType = [string]$item.MemberType
                PropertyType = $item.PropertyType.FullName; ReturnType = $item.ReturnType.FullName
                FieldType = $item.FieldType.FullName; IsStatic = $item.IsStatic; IsEnum = $item.IsEnum
                Syntax = @($item.Syntax)
            }
        }
    }
    [ordered]@{
        Id = $case.Id; Count = $output.Count; Records = @($records)
        Warnings = @($warnings | ForEach-Object { $_.Message })
        Error = if ($errorRecord) {
            [ordered]@{ Id = $errorRecord.FullyQualifiedErrorId; Type = $errorRecord.Exception.GetType().FullName; Category = [string]$errorRecord.CategoryInfo.Category }
        } else { $null }
    }
}
$metadata = foreach ($commandName in 'Get-MemberMethod','Get-TypeMember','Get-TypeConstructor') {
    $parameter = (Get-Command $commandName).Parameters['TypeName']
    [ordered]@{
        Command = $commandName; ParameterType = $parameter.ParameterType.FullName
        Attributes = @($parameter.Attributes | ForEach-Object { $_.GetType().FullName })
        Transformations = @($parameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ArgumentTransformationAttribute] } | ForEach-Object {
            [ordered]@{ Name = $_.GetType().FullName; BaseType = $_.GetType().BaseType.FullName }
        })
    }
}
[ordered]@{ HostVersion = $PSVersionTable.PSVersion.ToString(); Observations = @($observations); Metadata = @($metadata) } | ConvertTo-Json -Depth 20 -Compress
