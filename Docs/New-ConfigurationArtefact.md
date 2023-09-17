---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationArtefact

## SYNOPSIS
Tells the module to create artefact of specified type

## SYNTAX

```
New-ConfigurationArtefact [[-PostScriptMerge] <ScriptBlock>] [[-PreScriptMerge] <ScriptBlock>] -Type <String>
 [-Enable] [-IncludeTagName] [-Path <String>] [-AddRequiredModules] [-ModulesPath <String>]
 [-RequiredModulesPath <String>] [-CopyDirectories <IDictionary>] [-CopyFiles <IDictionary>]
 [-CopyDirectoriesRelative] [-CopyFilesRelative] [-DoNotClear] [-ArtefactName <String>] [-ScriptName <String>]
 [-ID <String>] [<CommonParameters>]
```

## DESCRIPTION
Tells the module to create artefact of specified type
There can be multiple artefacts created (even of same type)
At least one packed artefact is required for publishing to GitHub

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationArtefact -Type Unpacked -Enable -Path "$PSScriptRoot\..\Artefacts\Unpacked" -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Unpacked\Modules"
```

### EXAMPLE 2
```
# standard artefact, packed with tag name without any additional modules or required modules
```

New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed" -IncludeTagName

### EXAMPLE 3
```
# Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file
```

New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" -IncludeTagName

### EXAMPLE 4
```
# Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file
```

# But additionally pack it into zip fileĄŚż$%#
New-ConfigurationArtefact -Type ScriptPacked -Enable -Path "$PSScriptRoot\..\Artefacts\ScriptPacked" -ArtefactName "Script-\<ModuleName\>-$((Get-Date).ToString('yyyy-MM-dd')).zip"

## PARAMETERS

### -PostScriptMerge
ScriptBlock that will be added in the end of the script.
It's only applicable to type of Script, PackedScript.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PreScriptMerge
ScriptBlock that will be added in the beggining of the script.
It's only applicable to type of Script, PackedScript.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Type
There are 4 types of artefacts:
- Unpacked - unpacked module (useful for testing)
- Packed - packed module (as zip) - usually used for publishing to GitHub or copying somewhere
- Script - script that is module in form of PS1 without PSD1 - only applicable to very simple modules
- PackedScript - packed module (as zip) that is script that is module in form of PS1 without PSD1 - only applicable to very simple modules

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

### -Enable
Enable artefact creation.
By default artefact creation is disabled.

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

### -IncludeTagName
Include tag name in artefact name.
By default tag name is not included.
Alternatively you can provide ArtefactName parameter to specify your own artefact name (with or without TagName)

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

### -Path
Path where artefact will be created.
Please choose a separate directory for each artefact type, as logic may be interfering one another.

You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

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

### -AddRequiredModules
Add required modules to artefact by copying them over.
By default required modules are not added.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: RequiredModules

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModulesPath
Path where main module or required module (if not specified otherwise in RequiredModulesPath) will be copied to.
By default it will be put in the Path folder if not specified
You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

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

### -RequiredModulesPath
Path where required modules will be copied to.
By default it will be put in the Path folder if not specified.
If ModulesPath is specified, but RequiredModulesPath is not specified it will be put into ModulesPath folder.
You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

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

### -CopyDirectories
Provide Hashtable of directories to copy to artefact.
Key is source directory, value is destination directory.

```yaml
Type: IDictionary
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopyFiles
Provide Hashtable of files to copy to artefact.
Key is source file, value is destination file.

```yaml
Type: IDictionary
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopyDirectoriesRelative
Define if destination directories should be relative to artefact root.
By default they are not.

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

### -CopyFilesRelative
Define if destination files should be relative to artefact root.
By default they are not.

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

### -DoNotClear
Do not clear artefact directory before creating artefact.
By default artefact directory is cleared.

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

### -ArtefactName
The name of the artefact.
If not specified, the default name will be used.
You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

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

### -ScriptName
The name of the script.
If not specified, the default name will be used.
Only applicable to Script and ScriptPacked artefacts.
You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

```yaml
Type: String
Parameter Sets: (All)
Aliases: FileName

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ID
Optional ID of the artefact.
To be used by New-ConfigurationPublish cmdlet
If not specified, the first packed artefact will be used for publishing to GitHub

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS
