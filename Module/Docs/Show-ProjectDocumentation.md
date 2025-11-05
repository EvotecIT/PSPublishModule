---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Show-ProjectDocumentation

## SYNOPSIS
Shows README/CHANGELOG or a chosen document for a module, with a simple console view.

## SYNTAX

### ByName (Default)
```
Show-ProjectDocumentation [[-Name] <String>] [-RequiredVersion <Version>] [-Readme] [-Changelog] [-License]
 [-Intro] [-Upgrade] [-All] [-Links] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByModule
```
Show-ProjectDocumentation [-Module <PSModuleInfo>] [-RequiredVersion <Version>] [-Readme] [-Changelog]
 [-License] [-Intro] [-Upgrade] [-All] [-Links] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByPath
```
Show-ProjectDocumentation [-RequiredVersion <Version>] [-DocsPath <String>] [-Readme] [-Changelog] [-License]
 [-Intro] [-Upgrade] [-All] [-Links] [-File <String>] [-PreferInternals] [-List] [-Raw] [-Open]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Finds a module (by name or PSModuleInfo) and renders README/CHANGELOG from the module root
or from its Internals folder (as defined in PrivateData.PSData.Delivery).
You can also point directly to a docs folder via -DocsPath (e.g., output of Install-ModuleDocumentation).

## EXAMPLES

### EXAMPLE 1
```
Show-ProjectDocumentation -Name EFAdminManager -Readme
```

### EXAMPLE 2
```
Get-Module -ListAvailable EFAdminManager | Show-ProjectDocumentation -Changelog
```

### EXAMPLE 3
```
Show-ProjectDocumentation -DocsPath 'C:\Docs\EFAdminManager\3.0.0' -Readme -Open
```

### EXAMPLE 4
```
Show-ProjectDocumentation -Name EFAdminManager -License
```

### EXAMPLE 5
```
Show-ProjectDocumentation -Name EFAdminManager -Intro
```

### EXAMPLE 6
```
Show-ProjectDocumentation -Name EFAdminManager -Upgrade
```

### EXAMPLE 7
```
Show-ProjectDocumentation -Name EFAdminManager -List
```

### EXAMPLE 8
```
Show-ProjectDocumentation -Name EFAdminManager -All -Links
Displays Introduction, README, CHANGELOG, LICENSE and prints ImportantLinks.
```

### EXAMPLE 9
```
# Prefer Internals copy of README/CHANGELOG when both root and Internals exist
Show-ProjectDocumentation -Name EFAdminManager -Readme -Changelog -PreferInternals
```

### EXAMPLE 10
```
# Show a specific file from a copied docs folder
Show-ProjectDocumentation -DocsPath 'C:\\Docs\\EFAdminManager\\3.0.0' -File 'Internals\\Docs\\HowTo.md'
```

### EXAMPLE 11
```
# Quick list of found README/CHANGELOG/License in root and Internals
Show-ProjectDocumentation -Name EFAdminManager -List | Format-Table -Auto
```

### EXAMPLE 12
```
# Open the resolved README in the default viewer
Show-ProjectDocumentation -Name EFAdminManager -Readme -Open
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
Show LICENSE.
If multiple variants exist (LICENSE.md, LICENSE.txt), the resolver prefers a normalized 'license.txt' in
the chosen area (root vs Internals).

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
Show introduction text defined in PrivateData.PSData.Delivery.IntroText when available.
If not defined,
falls back to README resolution (root vs Internals honoring -PreferInternals).

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
Show upgrade text defined in PrivateData.PSData.Delivery.UpgradeText when available.
If not defined,
looks for an UPGRADE* file; otherwise throws.

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

### -All
Show Introduction, README, CHANGELOG and LICENSE in a standard order.
You can still add
specific switches (e.g., -Changelog) and they will be included additively without duplication.

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

### -Links
Print ImportantLinks defined in PrivateData.PSData.Delivery after the selected documents.

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
