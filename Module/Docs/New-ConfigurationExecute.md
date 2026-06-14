---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationExecute
## SYNOPSIS
Creates a module pipeline lifecycle action.

## SYNTAX
### Inline (Default)
```powershell
New-ConfigurationExecute -InlineScript <string> [-Name <string>] [-At <ModulePipelineActionStage>] [-WorkingDirectory <string>] [-Environment <hashtable>] [-TimeoutSeconds <int>] [-ContinueOnError] [-Disabled] [-PreferWindowsPowerShell] [<CommonParameters>]
```

### File
```powershell
New-ConfigurationExecute -FilePath <string> [-Name <string>] [-At <ModulePipelineActionStage>] [-WorkingDirectory <string>] [-Environment <hashtable>] [-TimeoutSeconds <int>] [-ContinueOnError] [-Disabled] [-PreferWindowsPowerShell] [<CommonParameters>]
```

### ScriptBlock
```powershell
New-ConfigurationExecute -ScriptBlock <scriptblock> [-Name <string>] [-At <ModulePipelineActionStage>] [-WorkingDirectory <string>] [-Environment <hashtable>] [-TimeoutSeconds <int>] [-ContinueOnError] [-Disabled] [-PreferWindowsPowerShell] [<CommonParameters>]
```

## DESCRIPTION
Lifecycle actions run PowerShell at a named module pipeline context point such as AfterStaging,
AfterManifest, or BeforePublish. PowerForge writes a stable JSON context file before each
action and exposes its path through the POWERFORGE_CONTEXT environment variable.

Use lifecycle actions for project-specific preparation, generated-file adjustments, artifact checks, and
release guardrails that need a precise pipeline context. Configuration segment order does not control
execution order; the At value does.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationExecute -Name 'Inspect staged module' -At AfterStaging -InlineScript '$ctx = Get-Content $env:POWERFORGE_CONTEXT | ConvertFrom-Json; Get-ChildItem $ctx.ModuleRoot'
```

Runs the inline PowerShell after the module staging context exists.

### EXAMPLE 2
```powershell
PS> New-ConfigurationExecute -Name 'Release guard' -At BeforePublish -FilePath '.\Build\Test-ReleaseReady.ps1' -TimeoutSeconds 120
```

Runs a repository script before publish steps execute.

## PARAMETERS

### -At
Stable module pipeline lifecycle point where the action runs.

```yaml
Type: ModulePipelineActionStage
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values: BeforeDependencies, AfterDependencies, BeforeVersioning, AfterVersioning, BeforeStaging, AfterStaging, BeforeBuild, AfterBuild, BeforeManifest, AfterManifest, BeforeDocumentation, AfterDocumentation, BeforeFormatting, AfterFormatting, BeforeValidation, AfterValidation, BeforeTests, AfterTests, BeforeSigning, AfterSigning, BeforeArtefacts, AfterArtefacts, BeforePublish, AfterPublish, BeforeInstall, AfterInstall

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ContinueOnError
When set, logs a failed action and lets the pipeline continue.

```yaml
Type: SwitchParameter
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Disabled
When set, disables the action while keeping it in configuration.

```yaml
Type: SwitchParameter
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Environment
Environment variable overrides passed to the action process.

```yaml
Type: Hashtable
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FilePath
Path to a PowerShell script file. Relative paths resolve from the project root.

```yaml
Type: String
Parameter Sets: File
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InlineScript
Inline PowerShell script text.

```yaml
Type: String
Parameter Sets: Inline
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Friendly action name shown in progress and result output.

```yaml
Type: String
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PreferWindowsPowerShell
When set, prefer Windows PowerShell before pwsh on Windows.

```yaml
Type: SwitchParameter
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptBlock
Inline PowerShell script block. The script block text is executed out-of-process.

```yaml
Type: ScriptBlock
Parameter Sets: ScriptBlock
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TimeoutSeconds
Action timeout in seconds. Defaults to five minutes.

```yaml
Type: Nullable`1
Parameter Sets: Inline, File, ScriptBlock
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -WorkingDirectory
Optional working directory. Relative paths resolve from the project root.

```yaml
Type: String
Parameter Sets: Inline, File, ScriptBlock
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

- `PowerForge.ConfigurationActionSegment`

## RELATED LINKS

- None
