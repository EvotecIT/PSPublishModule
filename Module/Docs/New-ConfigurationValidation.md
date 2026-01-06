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
New-ConfigurationValidation [-Enable] [-StructureSeverity <ValidationSeverity>] [-DocumentationSeverity <ValidationSeverity>] [-ScriptAnalyzerSeverity <ValidationSeverity>] [-FileIntegritySeverity <ValidationSeverity>] [-TestsSeverity <ValidationSeverity>] [-BinarySeverity <ValidationSeverity>] [-CsprojSeverity <ValidationSeverity>] [-PublicFunctionPaths <string[]>] [-InternalFunctionPaths <string[]>] [-ValidateManifestFiles <bool>] [-ValidateExports <bool>] [-ValidateInternalNotExported <bool>] [-AllowWildcardExports <bool>] [-MinSynopsisPercent <int>] [-MinDescriptionPercent <int>] [-MinExamplesPerCommand <int>] [-ExcludeCommands <string[]>] [-EnableScriptAnalyzer] [-ScriptAnalyzerExcludeDirectories <string[]>] [-ScriptAnalyzerExcludeRules <string[]>] [-ScriptAnalyzerSkipIfUnavailable <bool>] [-ScriptAnalyzerTimeoutSeconds <int>] [-FileIntegrityExcludeDirectories <string[]>] [-FileIntegrityCheckTrailingWhitespace <bool>] [-FileIntegrityCheckSyntax <bool>] [-BannedCommands <string[]>] [-AllowBannedCommandsIn <string[]>] [-EnableTests] [-TestsPath <string>] [-TestAdditionalModules <string[]>] [-TestSkipModules <string[]>] [-TestSkipDependencies] [-TestSkipImport] [-TestForce] [-TestTimeoutSeconds <int>] [-ValidateBinaryAssemblies <bool>] [-ValidateBinaryExports <bool>] [-AllowBinaryWildcardExports <bool>] [-RequireTargetFramework <bool>] [-RequireLibraryOutput <bool>] [<CommonParameters>]
```

## DESCRIPTION
Adds a single validation segment that can run structure, documentation, test, binary, and csproj checks.
Each check can be configured as Off/Warning/Error to control whether it is informational or blocking.

Encoding and line-ending enforcement is handled by New-ConfigurationFileConsistency.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationValidation -Enable -StructureSeverity Error -DocumentationSeverity Warning
```

### EXAMPLE 2
```powershell
PS>New-ConfigurationValidation -Enable -EnableTests -TestsSeverity Error -TestsPath 'Tests'
```

## PARAMETERS

### -AllowBannedCommandsIn
File names allowed to use banned commands.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowBinaryWildcardExports
Allow wildcard exports for binary checks.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowWildcardExports
Allow wildcard exports (skip export validation if FunctionsToExport='*').

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BannedCommands
Commands that should not appear in scripts.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BinarySeverity
Severity for binary export checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CsprojSeverity
Severity for csproj validation checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DocumentationSeverity
Severity for documentation checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enable
Enable module validation checks during build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnableScriptAnalyzer
Enable PSScriptAnalyzer checks during validation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnableTests
Enable test execution during validation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeCommands
Command names to exclude from documentation checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileIntegrityCheckSyntax
Check for PowerShell syntax errors.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileIntegrityCheckTrailingWhitespace
Check for trailing whitespace in scripts.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileIntegrityExcludeDirectories
Directories to exclude from file integrity checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileIntegritySeverity
Severity for file integrity checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InternalFunctionPaths
Relative paths to internal function files (default: "internal\\functions").

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinDescriptionPercent
Minimum description coverage percentage (default 100).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinExamplesPerCommand
Minimum examples per command (default 1).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinSynopsisPercent
Minimum synopsis coverage percentage (default 100).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublicFunctionPaths
Relative paths to public function files (default: "functions").

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequireLibraryOutput
Require OutputType=Library in csproj (when specified).

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequireTargetFramework
Require TargetFramework/TargetFrameworks in csproj.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptAnalyzerExcludeDirectories
Directories to exclude from PSScriptAnalyzer checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptAnalyzerExcludeRules
PSScriptAnalyzer rules to exclude.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptAnalyzerSeverity
Severity for PSScriptAnalyzer checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptAnalyzerSkipIfUnavailable
Skip PSScriptAnalyzer checks if the module is not installed.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptAnalyzerTimeoutSeconds
ScriptAnalyzer timeout, in seconds (default 300).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -StructureSeverity
Severity for module structure checks.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestAdditionalModules
Additional modules to install for tests.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestForce
Force dependency reinstall and module reimport during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestSkipDependencies
Skip dependency installation during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestSkipImport
Skip importing the module during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestSkipModules
Module names to skip during test dependency installation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestsPath
Path to tests (defaults to Tests under project root).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestsSeverity
Severity for test failures.

```yaml
Type: ValidationSeverity
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestTimeoutSeconds
Test timeout, in seconds (default 600).

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ValidateBinaryAssemblies
Validate that binary assemblies exist.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ValidateBinaryExports
Validate binary exports against CmdletsToExport/AliasesToExport.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ValidateExports
Validate that FunctionsToExport matches public functions.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ValidateInternalNotExported
Validate that internal functions are not exported.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ValidateManifestFiles
Validate manifest file references (RootModule/Formats/Types/RequiredAssemblies).

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
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

