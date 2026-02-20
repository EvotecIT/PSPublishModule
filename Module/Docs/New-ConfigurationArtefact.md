---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationArtefact
## SYNOPSIS
Tells the module to create an artefact of a specified type.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationArtefact [[-PostScriptMerge] <scriptblock>] [[-PreScriptMerge] <scriptblock>] -Type <ArtefactType> [-Enable] [-IncludeTagName] [-Path <string>] [-AddRequiredModules] [-ModulesPath <string>] [-RequiredModulesPath <string>] [-RequiredModulesRepository <string>] [-RequiredModulesTool <ModuleSaveTool>] [-RequiredModulesSource <RequiredModulesSource>] [-RequiredModulesCredentialUserName <string>] [-RequiredModulesCredentialSecret <string>] [-RequiredModulesCredentialSecretFilePath <string>] [-CopyDirectories <ArtefactCopyMapping[]>] [-CopyFiles <ArtefactCopyMapping[]>] [-CopyDirectoriesRelative] [-CopyFilesRelative] [-DoNotClear] [-ArtefactName <string>] [-ScriptName <string>] [-ID <string>] [-PostScriptMergePath <string>] [-PreScriptMergePath <string>] [<CommonParameters>]
```

## DESCRIPTION
Artefacts are created after the module is built into staging. Use Packed (ZIP) for distribution and
Unpacked (folder) for inspection or offline installation.

When -AddRequiredModules is enabled, required modules are copied from locally available modules (Get-Module -ListAvailable) and,
when configured, downloaded (via Save-PSResource/Save-Module) before being copied into the artefact.

Use -ID to link an artefact to a publish step (New-ConfigurationPublish) and publish only a specific artefact.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationArtefact -Type Packed -Enable -Path 'Artefacts\Packed' -ID 'Packed'
```

### EXAMPLE 2
```powershell
New-ConfigurationArtefact -Type Unpacked -Enable -AddRequiredModules -Path 'Artefacts\Unpacked' -RequiredModulesRepository 'PSGallery'
```

## PARAMETERS

### -AddRequiredModules
Add required modules to artefact by copying them over.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: RequiredModules
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ArtefactName
The name of the artefact. If not specified, the default name will be used.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CopyDirectories
Directories to copy to artefact (Source/Destination). Accepts legacy hashtable (source=>destination) or T:PowerForge.ArtefactCopyMapping[]

```yaml
Type: ArtefactCopyMapping[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CopyDirectoriesRelative
Define if destination directories should be relative to artefact root.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CopyFiles
Files to copy to artefact (Source/Destination). Accepts legacy hashtable (source=>destination) or T:PowerForge.ArtefactCopyMapping[]

```yaml
Type: ArtefactCopyMapping[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CopyFilesRelative
Define if destination files should be relative to artefact root.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DoNotClear
Do not clear artefact output directory before creating artefact.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enable
Enable artefact creation. By default artefact creation is disabled.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ID
Optional ID of the artefact (to be used by New-ConfigurationPublish).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeTagName
Include tag name in artefact name. By default tag name is not included.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModulesPath
Path where main module (or required module) will be copied to.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path where artefact will be created.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PostScriptMerge
ScriptBlock that will be added at the end of the script (Script / ScriptPacked).

```yaml
Type: ScriptBlock
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PostScriptMergePath
Path to file that will be added at the end of the script (Script / ScriptPacked).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PreScriptMerge
ScriptBlock that will be added at the beginning of the script (Script / ScriptPacked).

```yaml
Type: ScriptBlock
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PreScriptMergePath
Path to file that will be added at the beginning of the script (Script / ScriptPacked).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesCredentialSecret
Repository credential secret (password/token) in clear text used when downloading required modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesCredentialSecretFilePath
Repository credential secret (password/token) in a clear-text file used when downloading required modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesCredentialUserName
Repository credential username (basic auth) used when downloading required modules.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesPath
Path where required modules will be copied to.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesRepository
Repository name used when downloading required modules (Save-PSResource / Save-Module).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesSource
Source used when resolving required modules (Auto / Installed / Download). When omitted, PowerForge defaults to Installed (no download).

```yaml
Type: RequiredModulesSource
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, Installed, Download

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredModulesTool
Tool used when downloading required modules (Save-PSResource / Save-Module).

```yaml
Type: ModuleSaveTool
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, PSResourceGet, PowerShellGet

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptName
The name of the script artefact (alias: FileName).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: FileName
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Type
Artefact type to generate.

```yaml
Type: ArtefactType
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Unpacked, Packed, Script, ScriptPacked

Required: True
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

- `System.Object`

## RELATED LINKS

- None

