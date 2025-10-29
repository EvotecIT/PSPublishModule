---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationDelivery

## SYNOPSIS
Configures delivery metadata for bundling and installing internal docs/examples.

## SYNTAX

```
New-ConfigurationDelivery [-Enable] [[-InternalsPath] <String>] [-IncludeRootReadme] [-IncludeRootChangelog]
 [[-ReadmeDestination] <String>] [[-ChangelogDestination] <String>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Adds Delivery options to the PSPublishModule configuration so the build embeds
discovery metadata in the manifest (PrivateData.PSData.PSPublishModuleDelivery)
and so the Internals folder is easy to find post-install by helper cmdlets
such as Install-ModuleDocumentation.

Typical usage is to call this in your Build\Manage-Module.ps1 alongside
New-ConfigurationInformation -IncludeAll 'Internals\' so that the Internals
directory is physically included in the shipped module and discoverable later.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IncludeRootReadme -IncludeRootChangelog
Emits Options.Delivery and causes PrivateData.PSData.PSPublishModuleDelivery to be written in the manifest.
```

### EXAMPLE 2
```
New-ConfigurationInformation -IncludeAll 'Internals\'
PS> New-ConfigurationDelivery -Enable
Minimal configuration that bundles Internals and exposes it to the installer.
```

## PARAMETERS

### -Enable
Enables delivery metadata emission.
If not specified, nothing is emitted.

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

### -InternalsPath
Relative path inside the module that contains internal deliverables
(e.g.
'Internals').
Defaults to 'Internals'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeRootReadme
Include module root README.* during installation (if present).

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

### -IncludeRootChangelog
Include module root CHANGELOG.* during installation (if present).

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

### -ReadmeDestination
Where to bundle README.* within the built module.
One of: Internals, Root, Both, None.
Default: Internals.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: Internals
Accept pipeline input: False
Accept wildcard characters: False
```

### -ChangelogDestination
Where to bundle CHANGELOG.* within the built module.
One of: Internals, Root, Both, None.
Default: Internals.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: Internals
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
This emits a Type 'Options' object under Options.Delivery so it works with the
existing New-PrepareStructure logic without further changes.

## RELATED LINKS
