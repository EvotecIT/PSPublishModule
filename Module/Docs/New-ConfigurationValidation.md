---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationValidation
## SYNOPSIS
Creates configuration for module validation checks during build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationValidation [-Enable] [-StructureSeverity <ValidationSeverity>] [-DocumentationSeverity <ValidationSeverity>] [-ScriptAnalyzerSeverity <ValidationSeverity>] [-FileIntegritySeverity <ValidationSeverity>] [-TestsSeverity <ValidationSeverity>] [-BinarySeverity <ValidationSeverity>] [-CsprojSeverity <ValidationSeverity>] [-PublicFunctionPaths <string[]>] [-InternalFunctionPaths <string[]>] [-ValidateManifestFiles <bool>] [-ValidateExports <bool>] [-ValidateInternalNotExported <bool>] [-AllowWildcardExports <bool>] [-MinSynopsisPercent <int>] [-MinDescriptionPercent <int>] [-MinExamplesPerCommand <int>] [-MinParameterDescriptionPercent <int>] [-MinTypeDescriptionPercent <int>] [-ExcludeCommands <string[]>] [-EnableScriptAnalyzer] [-ScriptAnalyzerExcludeDirectories <string[]>] [-ScriptAnalyzerExcludeRules <string[]>] [-ScriptAnalyzerSkipIfUnavailable <bool>] [-ScriptAnalyzerInstallIfUnavailable <bool>] [-ScriptAnalyzerTimeoutSeconds <int>] [-FileIntegrityExcludeDirectories <string[]>] [-FileIntegrityCheckTrailingWhitespace <bool>] [-FileIntegrityCheckSyntax <bool>] [-BannedCommands <string[]>] [-AllowBannedCommandsIn <string[]>] [-EnableTests] [-TestsPath <string>] [-TestAdditionalModules <string[]>] [-TestSkipModules <string[]>] [-TestSkipDependencies] [-TestSkipImport] [-TestForce] [-TestTimeoutSeconds <int>] [-ValidateBinaryAssemblies <bool>] [-ValidateBinaryExports <bool>] [-AllowBinaryWildcardExports <bool>] [-RequireTargetFramework <bool>] [-RequireLibraryOutput <bool>] [<CommonParameters>]
```

## DESCRIPTION
Adds a single validation segment that can run structure, documentation, test, binary, and csproj checks.
Each check can be configured as Off/Warning/Error to control whether it is informational or blocking.

Encoding and line-ending enforcement is handled by New-ConfigurationFileConsistency.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationValidation -Enable -StructureSeverity Error -DocumentationSeverity Warning
```


### EXAMPLE 2
```powershell
PS> New-ConfigurationValidation -Enable -EnableTests -TestsSeverity Error -TestsPath 'Tests'
```


## PARAMETERS

### -AllowBannedCommandsIn
File names allowed to use banned commands.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AllowBinaryWildcardExports
Allow wildcard exports for binary checks.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AllowWildcardExports
Allow wildcard exports (skip export validation if FunctionsToExport='*').

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -BannedCommands
Commands that should not appear in scripts.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -BinarySeverity
Severity for binary export checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CsprojSeverity
Severity for csproj validation checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DocumentationSeverity
Severity for documentation checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Enable
Enable module validation checks during build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableScriptAnalyzer
Enable PSScriptAnalyzer checks during validation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableTests
Enable test execution during validation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeCommands
Command names to exclude from documentation checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FileIntegrityCheckSyntax
Check for PowerShell syntax errors.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FileIntegrityCheckTrailingWhitespace
Check for trailing whitespace in scripts.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FileIntegrityExcludeDirectories
Directories to exclude from file integrity checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FileIntegritySeverity
Severity for file integrity checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InternalFunctionPaths
Relative paths to internal function files (default: "internal\\functions").

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinDescriptionPercent
Minimum description coverage percentage (default 100).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinExamplesPerCommand
Minimum examples per command (default 1).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinParameterDescriptionPercent
Minimum percentage of parameters that must have descriptions (default 0 = disabled).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinSynopsisPercent
Minimum synopsis coverage percentage (default 100).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinTypeDescriptionPercent
Minimum percentage of unique input/output types that must have descriptions (default 0 = disabled).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublicFunctionPaths
Relative paths to public function files (default: "functions").

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequireLibraryOutput
Require OutputType=Library in csproj (when specified).

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequireTargetFramework
Require TargetFramework/TargetFrameworks in csproj.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerExcludeDirectories
Directories to exclude from PSScriptAnalyzer checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerExcludeRules
PSScriptAnalyzer rules to exclude.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerInstallIfUnavailable
Install PSScriptAnalyzer on demand before validation when it is missing.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerSeverity
Severity for PSScriptAnalyzer checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerSkipIfUnavailable
Skip PSScriptAnalyzer checks if the module is not installed.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptAnalyzerTimeoutSeconds
ScriptAnalyzer timeout, in seconds (default 300).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -StructureSeverity
Severity for module structure checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestAdditionalModules
Additional modules to install for tests.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestForce
Force dependency reinstall and module reimport during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestSkipDependencies
Skip dependency installation during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestSkipImport
Skip importing the module during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestSkipModules
Module names to skip during test dependency installation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestsPath
Path to tests (defaults to Tests under project root).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestsSeverity
Severity for test failures.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Warning, Error

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestTimeoutSeconds
Test timeout, in seconds (default 600).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ValidateBinaryAssemblies
Validate that binary assemblies exist.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ValidateBinaryExports
Validate binary exports against CmdletsToExport/AliasesToExport.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ValidateExports
Validate that FunctionsToExport matches public functions.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ValidateInternalNotExported
Validate that internal functions are not exported.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ValidateManifestFiles
Validate manifest file references (RootModule/Formats/Types/RequiredAssemblies).

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
