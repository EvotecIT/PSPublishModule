---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-BenchmarkSuite
## SYNOPSIS
Runs a reusable PowerShell benchmark suite.

## SYNTAX
### Path (Default)
```powershell
Invoke-BenchmarkSuite [-Path] <string> [-OutputRoot <string>] [-WarmupCount <int>] [-IterationCount <int>] [-RunMode <string>] [-Suite <string>] [-Case <string[]>] [-Engine <string[]>] [-Operation <string[]>] [-HostName <string[]>] [-Profile <PowerShellBenchmarkProfileKind>] [-Cleanup <PowerShellBenchmarkCleanupMode>] [-Variable <hashtable>] [-Plan] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Settings
```powershell
Invoke-BenchmarkSuite [-Settings] <scriptblock> [-OutputRoot <string>] [-WarmupCount <int>] [-IterationCount <int>] [-RunMode <string>] [-Suite <string>] [-Case <string[]>] [-Engine <string[]>] [-Operation <string[]>] [-HostName <string[]>] [-Profile <PowerShellBenchmarkProfileKind>] [-Cleanup <PowerShellBenchmarkCleanupMode>] [-Variable <hashtable>] [-Plan] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Runs a reusable PowerShell benchmark suite.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\module.benchmark.ps1
```


## PARAMETERS

### -Case
Case or scenario names to include.

```yaml
Type: String[]
Parameter Sets: Path, Settings
Aliases: Cases, Scenario, Scenarios
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Cleanup
Optional cleanup override.

```yaml
Type: Nullable`1
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Engine
Engine names to include.

```yaml
Type: String[]
Parameter Sets: Path, Settings
Aliases: Engines
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -HostName
Host labels to include.

```yaml
Type: String[]
Parameter Sets: Path, Settings
Aliases: Host, Hosts
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IterationCount
Optional measured iteration count override.

```yaml
Type: Nullable`1
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Operation
Operation names to include.

```yaml
Type: String[]
Parameter Sets: Path, Settings
Aliases: Operations
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputRoot
Optional output root override.

```yaml
Type: String
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to a .benchmark.ps1 spec.

```yaml
Type: String
Parameter Sets: Path
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Prints the resolved plan instead of executing measurements.

```yaml
Type: SwitchParameter
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Optional profile override.

```yaml
Type: Nullable`1
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RunMode
Run mode label.

```yaml
Type: String
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Settings
Inline benchmark DSL settings.

```yaml
Type: ScriptBlock
Parameter Sets: Settings
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Suite
Optional suite name override.

```yaml
Type: String
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Variable
Optional variables exposed to benchmark specs as $BenchmarkVariables.

```yaml
Type: Hashtable
Parameter Sets: Path, Settings
Aliases: Variables
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -WarmupCount
Optional warmup count override.

```yaml
Type: Nullable`1
Parameter Sets: Path, Settings
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.BenchmarkRunResult
PowerForge.PowerShellBenchmarkWorkItem`

## RELATED LINKS

- None
