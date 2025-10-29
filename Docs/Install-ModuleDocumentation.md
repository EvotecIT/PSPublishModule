---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Install-ModuleDocumentation

## SYNOPSIS
Installs bundled module documentation/examples (Internals) to a chosen path.

## SYNTAX

### ByName (Default)
```
Install-ModuleDocumentation [[-Name] <String>] [-RequiredVersion <Version>] -Path <String> [-Layout <String>]
 [-OnExists <String>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```
Install-ModuleDocumentation [-Module <PSModuleInfo>] [-RequiredVersion <Version>] -Path <String>
 [-Layout <String>] [-OnExists <String>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Copies the contents of a module's Internals folder (or the path defined in
PrivateData.PSData.PSPublishModuleDelivery) to a destination outside of
$env:PSModulePath, optionally including README/CHANGELOG from module root.

## EXAMPLES

### EXAMPLE 1
```
Install-ModuleDocumentation -Name AdminManager -Path 'C:\Docs'
```

### EXAMPLE 2
```
Get-Module -ListAvailable AdminManager | Install-ModuleDocumentation -Path 'D:\AM'
```

## PARAMETERS

### -Name
Module name to install documentation for.
Accepts pipeline by value.

```yaml
Type: String
Parameter Sets: ByName
Aliases: ModuleName

Required: False
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -Module
A PSModuleInfo object (e.g., from Get-Module -ListAvailable) to operate on directly.

```yaml
Type: PSModuleInfo
Parameter Sets: ByModule
Aliases: InputObject, ModuleInfo

Required: False
Position: Named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -RequiredVersion
Specific version of the module to target.
If omitted, selects the highest available.

```yaml
Type: Version
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Destination directory where the Internals content will be copied.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Layout
How to lay out the destination path:
- Direct: copy into \<Path\>
- Module: copy into \<Path\>\\\\\<Name\>
- ModuleAndVersion (default): copy into \<Path\>\\\\\<Name\>\\\\\<Version\>

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: ModuleAndVersion
Accept pipeline input: False
Accept wildcard characters: False
```

### -OnExists
What to do if the destination folder already exists:
- Merge (default): merge files/folders; overwrite files only when -Force is used
- Overwrite: remove the existing destination, then copy fresh
- Skip: do nothing and return the existing destination path
- Stop: throw an error

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Merge
Accept pipeline input: False
Accept wildcard characters: False
```

### -CreateVersionSubfolder
When set (default), content is placed under '\<Path\>\\\\\<Name\>\\\\\<Version\>'.
If disabled, content is copied directly into '\<Path\>'.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Overwrite existing files.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ListOnly
Show what would be copied and where, without copying any files.
Returns the
computed destination path(s).
Use -Verbose for details.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -Open
After a successful copy, open the README in the destination (if present).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoIntro
{{ Fill NoIntro Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES

## RELATED LINKS
