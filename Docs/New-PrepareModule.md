---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-PrepareModule

## SYNOPSIS
Short description

## SYNTAX

```
New-PrepareModule [[-Settings] <ScriptBlock>] [-Path <String>] [-ModuleName <String>]
 [-FunctionsToExportFolder <String>] [-AliasesToExportFolder <String>] [-Configuration <IDictionary>]
 [-ExcludeFromPackage <String[]>] [-IncludeRoot <String[]>] [-IncludePS1 <String[]>] [-IncludeAll <String[]>]
 [-IncludeCustomCode <ScriptBlock>] [-IncludeToArray <IDictionary>] [-LibrariesCore <String>]
 [-LibrariesDefault <String>] [-LibrariesStandard <String>] [<CommonParameters>]
```

## DESCRIPTION
Long description

## EXAMPLES

### EXAMPLE 1
```
An example
```

## PARAMETERS

### -Settings
Parameter description

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

### -Path
Path to the folder where new project will be created.
If not provided it will be created in one up folder from the location of build script.

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

### -ModuleName
Module name to be used for the project.

```yaml
Type: String
Parameter Sets: (All)
Aliases: ProjectName

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FunctionsToExportFolder
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Public
Accept pipeline input: False
Accept wildcard characters: False
```

### -AliasesToExportFolder
Parameter description

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Public
Accept pipeline input: False
Accept wildcard characters: False
```

### -Configuration
Parameter description

```yaml
Type: IDictionary
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: [ordered] @{}
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeFromPackage
{{ Fill ExcludeFromPackage Description }}

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRoot
{{ Fill IncludeRoot Description }}

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('*.psm1', '*.psd1', 'License*')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludePS1
{{ Fill IncludePS1 Description }}

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('Private', 'Public', 'Enums', 'Classes')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeAll
{{ Fill IncludeAll Description }}

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data')
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeCustomCode
{{ Fill IncludeCustomCode Description }}

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeToArray
{{ Fill IncludeToArray Description }}

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

### -LibrariesCore
{{ Fill LibrariesCore Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Lib\Core
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesDefault
{{ Fill LibrariesDefault Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Lib\Default
Accept pipeline input: False
Accept wildcard characters: False
```

### -LibrariesStandard
{{ Fill LibrariesStandard Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Lib\Standard
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
