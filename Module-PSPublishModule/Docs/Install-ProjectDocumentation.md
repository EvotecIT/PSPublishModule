---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Install-ProjectDocumentation

## SYNOPSIS
Installs bundled module documentation/examples (Internals) to a chosen path.

## SYNTAX

### ByName (Default)
```
Install-ProjectDocumentation [[-Name] <String>] [-RequiredVersion <Version>] -Path <String> [-Layout <String>]
 [-OnExists <String>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```
Install-ProjectDocumentation [-Module <PSModuleInfo>] [-RequiredVersion <Version>] -Path <String>
 [-Layout <String>] [-OnExists <String>] [-CreateVersionSubfolder] [-Force] [-ListOnly] [-Open] [-NoIntro]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Copies the contents of a module's Internals folder (or the path defined in
PrivateData.PSData.PSPublishModuleDelivery) to a destination outside of
$env:PSModulePath, including subfolders such as Scripts, Docs, Binaries, Config.
When -IncludeRootReadme/-IncludeRootChangelog/-IncludeRootLicense are enabled in
New-ConfigurationDelivery, root README/CHANGELOG/LICENSE are also copied.

## EXAMPLES

### EXAMPLE 1
```
Install-ProjectDocumentation -Name AdminManager -Path 'C:\Docs'
```

### EXAMPLE 2
```
Get-Module -ListAvailable AdminManager | Install-ProjectDocumentation -Path 'D:\\AM'
Installs the highest available version of AdminManager to D:\AM\AdminManager\<Version>
```

### EXAMPLE 3
```
# Copy into Path\Name only, merge on re-run without overwriting existing files
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\Docs' -Layout Module
```

### EXAMPLE 4
```
# Overwrite destination on re-run
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\Docs' -OnExists Overwrite
```

### EXAMPLE 5
```
# Dry-run: show what would be copied and where
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\Docs' -ListOnly -Verbose
```

### EXAMPLE 6
```
# Copy, suppress intro/links printing, and open README afterwards
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\Docs' -NoIntro -Open
```

### EXAMPLE 7
```
# Typical build time configuration
New-ConfigurationInformation -IncludeAll 'Internals\'
New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -DocumentationOrder '01-Intro.md','02-HowTo.md' -IncludeRootReadme -IncludeRootChangelog
```

### EXAMPLE 8
```
# Direct layout into target folder (no Module/Version subfolders)
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\\Docs' -Layout Direct
```

### EXAMPLE 9
```
# Copy into C:\\Docs\\EFAdminManager and merge on rerun (only overwrite when -Force)
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\\Docs' -Layout Module -OnExists Merge -Force
```

### EXAMPLE 10
```
# Overwrite destination entirely on rerun
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\\Docs' -OnExists Overwrite
```

### EXAMPLE 11
```
# Skip if destination exists
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\\Docs' -OnExists Skip
```

### EXAMPLE 12
```
# Plan only with verbose output
Install-ProjectDocumentation -Name EFAdminManager -Path 'C:\\Docs' -ListOnly -Verbose
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
Suppress introductory notes and important links printed after installation.

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
