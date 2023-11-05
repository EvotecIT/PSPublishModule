---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Invoke-ModuleBuild

## SYNOPSIS
Command to create new module or update existing one.
It will create new module structure and everything around it, or update existing one.

## SYNTAX

### Modern (Default)
```
Invoke-ModuleBuild [[-Settings] <ScriptBlock>] [-Path <String>] -ModuleName <String>
 [-FunctionsToExportFolder <String>] [-AliasesToExportFolder <String>] [-ExcludeFromPackage <String[]>]
 [-IncludeRoot <String[]>] [-IncludePS1 <String[]>] [-IncludeAll <String[]>] [-IncludeCustomCode <ScriptBlock>]
 [-IncludeToArray <IDictionary>] [-LibrariesCore <String>] [-LibrariesDefault <String>]
 [-LibrariesStandard <String>] [-ExitCode] [<CommonParameters>]
```

### Configuration
```
Invoke-ModuleBuild -Configuration <IDictionary> [-ExitCode] [<CommonParameters>]
```

## DESCRIPTION
Command to create new module or update existing one.
It will create new module structure and everything around it, or update existing one.

## EXAMPLES

### EXAMPLE 1
```
An example
```

## PARAMETERS

### -Settings
Provide settings for the module in form of scriptblock.
It's using DSL to define settings for the module.

```yaml
Type: ScriptBlock
Parameter Sets: Modern
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Path to the folder where new project will be created, or existing project will be updated.
If not provided it will be created in one up folder from the location of build script.

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ModuleName
Provide name of the module.
It's required parameter.

```yaml
Type: String
Parameter Sets: Modern
Aliases: ProjectName

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FunctionsToExportFolder
Public functions folder name.
Default is 'Public'.
It will be used as part of PSD1 and PSM1 to export only functions from this folder.

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: Public
Accept pipeline input: False
Accept wildcard characters: False
```

### -AliasesToExportFolder
Public aliases folder name.
Default is 'Public'.
It will be used as part of PSD1 and PSM1 to export only aliases from this folder.

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: Public
Accept pipeline input: False
Accept wildcard characters: False
```

### -Configuration
Provides a way to configure module using hashtable.
It's the old way of configuring module, that requires knowledge of inner workings of the module to name proper key/value pairs
It's required for compatibility with older versions of the module.

```yaml
Type: IDictionary
Parameter Sets: Configuration
Aliases:

Required: True
Position: Named
Default value: [ordered] @{}
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeFromPackage
Exclude files from Artefacts.
Default is '.*, 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRoot
Include files in the Artefacts from root of the project.
Default is '*.psm1', '*.psd1', 'License*' files.
Other files will be ignored.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: @('*.psm1', '*.psd1', 'License*')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludePS1
Include *.ps1 files in the Artefacts from given folders.
Default are 'Private', 'Public', 'Enums', 'Classes' folders.
If the folder doesn't exists it will be ignored.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: @('Private', 'Public', 'Enums', 'Classes')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeAll
Include all files in the Artefacts from given folders.
Default are 'Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data' folders.

```yaml
Type: String[]
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeCustomCode
Parameter description

```yaml
Type: ScriptBlock
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeToArray
Parameter description

```yaml
Type: IDictionary
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesCore
Parameter description

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: [io.path]::Combine("Lib", "Core")
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesDefault
Parameter description

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: [io.path]::Combine("Lib", "Default")
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesStandard
Parameter description

```yaml
Type: String
Parameter Sets: Modern
Aliases:

Required: False
Position: Named
Default value: [io.path]::Combine("Lib", "Standard")
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExitCode
Exit code to be returned to the caller.
If not provided, it will not exit the script, but finish gracefully.
Exit code 0 means success, 1 means failure.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS
