# New-ConfigurationValidation

Configure module validation checks (structure, documentation, script analyzer, file integrity, tests, binary exports, csproj).
Encoding and line-ending enforcement live under file consistency (see New-ConfigurationFileConsistency).

## Syntax

New-ConfigurationValidation [-Enable] [-StructureSeverity <ValidationSeverity>] [-DocumentationSeverity <ValidationSeverity>] [-ScriptAnalyzerSeverity <ValidationSeverity>] [-FileIntegritySeverity <ValidationSeverity>] [-TestsSeverity <ValidationSeverity>] [-BinarySeverity <ValidationSeverity>] [-CsprojSeverity <ValidationSeverity>] [-PublicFunctionPaths <string[]>] [-InternalFunctionPaths <string[]>] [-ValidateManifestFiles <bool>] [-ValidateExports <bool>] [-ValidateInternalNotExported <bool>] [-AllowWildcardExports <bool>] [-MinSynopsisPercent <int>] [-MinDescriptionPercent <int>] [-MinExamplesPerCommand <int>] [-ExcludeCommands <string[]>] [-EnableScriptAnalyzer] [-ScriptAnalyzerExcludeDirectories <string[]>] [-ScriptAnalyzerExcludeRules <string[]>] [-ScriptAnalyzerSkipIfUnavailable <bool>] [-ScriptAnalyzerTimeoutSeconds <int>] [-FileIntegrityExcludeDirectories <string[]>] [-FileIntegrityCheckTrailingWhitespace <bool>] [-FileIntegrityCheckSyntax <bool>] [-BannedCommands <string[]>] [-AllowBannedCommandsIn <string[]>] [-EnableTests] [-TestsPath <string>] [-TestAdditionalModules <string[]>] [-TestSkipModules <string[]>] [-TestSkipDependencies] [-TestSkipImport] [-TestForce] [-TestTimeoutSeconds <int>] [-ValidateBinaryAssemblies <bool>] [-ValidateBinaryExports <bool>] [-AllowBinaryWildcardExports <bool>] [-RequireTargetFramework <bool>] [-RequireLibraryOutput <bool>] [<CommonParameters>]

## Behavior

- Set `-Enable` to include the validation segment in the build pipeline.
- Each check runs when its severity is not `Off` (and ScriptAnalyzer/Tests also require `-EnableScriptAnalyzer` / `-EnableTests`).
- Severity controls pipeline impact: `Warning` reports issues without failing; `Error` fails the build.

## What it checks

- **Structure**: manifest references, exported functions, internal functions not exported.
- **Documentation**: synopsis/description coverage and example counts (per exported command).
- **Script analyzer**: PSScriptAnalyzer rule violations (optional).
- **File integrity**: trailing whitespace, syntax errors, banned commands in scripts.
- **Tests**: runs Pester tests and reports failures (optional).
- **Binary**: validates binary assemblies and manifest exports for compiled modules.
- **Csproj**: basic project checks such as target framework and library output.

## Parameter notes

- **Structure**: use `-PublicFunctionPaths` and `-InternalFunctionPaths` to match your folder layout; `-ValidateManifestFiles`, `-ValidateExports`, and `-ValidateInternalNotExported` tighten manifest validation; `-AllowWildcardExports` skips export comparison if FunctionsToExport uses `*`.
- **Documentation**: `-MinSynopsisPercent`, `-MinDescriptionPercent`, and `-MinExamplesPerCommand` set minimum coverage; `-ExcludeCommands` skips specific commands (for dynamic or generated commands).
- **Script analyzer**: `-EnableScriptAnalyzer` turns it on; use `-ScriptAnalyzerExcludeDirectories` and `-ScriptAnalyzerExcludeRules` to scope findings; `-ScriptAnalyzerSkipIfUnavailable` prevents failures when PSScriptAnalyzer is missing; `-ScriptAnalyzerTimeoutSeconds` caps runtime.
- **File integrity**: `-FileIntegrityExcludeDirectories` skips folders; `-FileIntegrityCheckTrailingWhitespace` flags whitespace; `-FileIntegrityCheckSyntax` parses scripts for errors; `-BannedCommands` blocks disallowed commands; `-AllowBannedCommandsIn` whitelists specific file names (for generated scripts).
- **Tests**: `-EnableTests` runs Pester; `-TestsPath` overrides the default Tests folder; `-TestAdditionalModules` installs dependencies; `-TestSkipModules`, `-TestSkipDependencies`, `-TestSkipImport`, and `-TestForce` control dependency and import behavior; `-TestTimeoutSeconds` caps runtime.
- **Binary**: `-ValidateBinaryAssemblies` checks referenced assemblies exist; `-ValidateBinaryExports` compares manifest exports; `-AllowBinaryWildcardExports` skips export comparison when wildcards are used.
- **Csproj**: `-RequireTargetFramework` enforces TargetFramework/TargetFrameworks; `-RequireLibraryOutput` ensures OutputType is Library (when present).

## Notes

- Encoding and line-ending checks are handled by file consistency (New-ConfigurationFileConsistency / Get-ProjectConsistency / Convert-ProjectEncoding).

## Examples

Enable validation with errors for structure and warnings for docs:

```powershell
New-ConfigurationValidation -Enable -StructureSeverity Error -DocumentationSeverity Warning
```

Run tests during validation and fail on failures:

```powershell
New-ConfigurationValidation -Enable -EnableTests -TestsSeverity Error -TestsPath 'Tests'
```

Enable PSScriptAnalyzer + file integrity checks:

```powershell
New-ConfigurationValidation -Enable -EnableScriptAnalyzer -ScriptAnalyzerSeverity Warning -FileIntegritySeverity Warning
```

Fail on banned commands but only warn on trailing whitespace:

```powershell
New-ConfigurationValidation -Enable `
  -FileIntegritySeverity Error `
  -FileIntegrityCheckTrailingWhitespace $true `
  -FileIntegrityCheckSyntax $true `
  -BannedCommands 'Write-Host','Invoke-Expression'
```

Enable binary + csproj checks for compiled modules:

```powershell
New-ConfigurationValidation -Enable -BinarySeverity Error -CsprojSeverity Warning
```

Enforce encoding/line endings using file consistency:

```powershell
New-ConfigurationFileConsistency -Enable -FailOnInconsistency -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF
```
