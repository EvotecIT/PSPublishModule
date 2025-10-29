---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Show-ModuleDocumentation

## SYNOPSIS
Shows README/CHANGELOG or a chosen document for a module, with a simple console view.

## SYNTAX

### ByName (Default)
```
Show-ModuleDocumentation [[-Name] <String>] [-RequiredVersion <Version>] [-Readme] [-Changelog] [-License]
 [-Intro] [-Upgrade] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByModule
```
Show-ModuleDocumentation [-Module <PSModuleInfo>] [-RequiredVersion <Version>] [-Readme] [-Changelog]
 [-License] [-Intro] [-Upgrade] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByPath
```
Show-ModuleDocumentation [-RequiredVersion <Version>] [-DocsPath <String>] [-Readme] [-Changelog] [-License]
 [-Intro] [-Upgrade] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Finds a module (by name or PSModuleInfo) and renders README/CHANGELOG from the module root
or from its Internals folder (as defined in PrivateData.PSData.PSPublishModuleDelivery).
You can also point directly to a docs folder via -DocsPath (e.g., output of Install-ModuleDocumentation).

## EXAMPLES

### EXAMPLE 1
```
Show-ModuleDocumentation -Name EFAdminManager -Readme
```

### EXAMPLE 2
```
Get-Module -ListAvailable EFAdminManager | Show-ModuleDocumentation -Changelog
```

### EXAMPLE 3
```
Show-ModuleDocumentation -DocsPath 'C:\Docs\EFAdminManager\3.0.0' -Readme -Open
```

## PARAMETERS

### -Name
Module name to show documentation for.
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

### -DocsPath
A folder that contains documentation to display (e.g., the destination created by Install-ModuleDocumentation).
When provided, the cmdlet does not look up the module and shows docs from this folder.

```yaml
Type: String
Parameter Sets: ByPath
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Readme
Show README*.
If both root and Internals copies exist, the root copy is preferred unless -PreferInternals is set.

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

### -Changelog
Show CHANGELOG*.
If both root and Internals copies exist, the root copy is preferred unless -PreferInternals is set.

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

### -License
{{ Fill License Description }}

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

### -Intro
{{ Fill Intro Description }}

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

### -Upgrade
{{ Fill Upgrade Description }}

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

### -File
Relative path to a specific file to display (relative to module root or Internals).
If rooted, used as-is.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PreferInternals
Prefer the Internals copy of README/CHANGELOG when both exist.

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

### -List
List available README/CHANGELOG files found (root and Internals) instead of displaying content.

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

### -Raw
Output the raw file content (no styling).

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
Open the resolved file in the system default viewer instead of rendering in the console.

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
