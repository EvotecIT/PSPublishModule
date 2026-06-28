---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Measure-ManagedModule
## SYNOPSIS
Measures managed module lifecycle scenarios through the managed C# module engine.

## SYNTAX
### __AllParameterSets
```powershell
Measure-ManagedModule [-Name] <string[]> [-Operation <ManagedModuleBenchmarkOperation[]>] [-Engine <ManagedModuleBenchmarkEngine[]>] [-Repository <string>] [-RepositoryName <string>] [-ProfileName <string>] [-Version <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-VersionPolicy <string>] [-Prerelease] [-Scope <ManagedModuleInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ModuleRoot <string>] [-ModulePath <string>] [-ManifestPath <string>] [-PackageCacheDirectory <string>] [-PackageOutputDirectory <string>] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Force] [-AllowClobber] [-AcceptLicense] [-SkipDependencyCheck] [-Iterations <int>] [-StopOnError] [-RequireTransitionReady] [-RequireManagedEvidenceReady] [-RequireCompatibilityRetirementReady] [-EnableNativeInstallUpdateBenchmark] [-MaximumManagedSlowdownRatio <double>] [-MaximumManagedSlowdownMilliseconds <int>] [-ValidateImport] [-ImportHost <ManagedModuleImportValidationHost[]>] [-ReportPath <string>] [-MarkdownReportPath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This command is a thin PowerShell surface over the reusable PowerForge benchmark service. It measures the managed
engine directly and returns typed benchmark run data that can be compared with compatibility baselines.

## EXAMPLES

### EXAMPLE 1
```powershell
Measure-ManagedModule -Name Company.Tools -Operation Install -Repository C:\Packages -ModuleRoot C:\Temp\Modules
```


## PARAMETERS

### -AcceptLicense
Accept package licenses when packages declare license acceptance is required.

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

### -AllowClobber
Allow command exports to overlap with other modules in the target root.

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

### -Credential
Optional repository credential.

```yaml
Type: PSCredential
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecret
Optional repository credential secret.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Password, Token
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecretFilePath
Optional path to a file containing the repository credential secret.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: CredentialPath, TokenPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialUserName
Optional repository credential username.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnableNativeInstallUpdateBenchmark
Allow native Install-Module, Install-PSResource, Update-Module, and Update-PSResource comparisons in disposable child hosts.

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

### -Engine
Delivery engines to measure for each scenario.

```yaml
Type: ManagedModuleBenchmarkEngine[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Managed, PSResourceGet, PowerShellGet

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force reinstall or update of the target version.

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

### -ImportHost
PowerShell hosts used when ValidateImport is enabled.

```yaml
Type: ManagedModuleImportValidationHost[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: PowerShell7, WindowsPowerShell

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Iterations
Number of times each module scenario should be measured.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ManifestPath
Module manifest path used by publish measurements.

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

### -MarkdownReportPath
Optional Markdown report path for benchmark evidence.

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

### -MaximumManagedSlowdownMilliseconds
Absolute managed median slowdown tolerance, in milliseconds, before transition readiness is blocked.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaximumManagedSlowdownRatio
Maximum allowed managed median slowdown ratio before transition readiness is blocked.

```yaml
Type: Double
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaximumVersion
Maximum package version to measure when Version is omitted.

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

### -MinimumVersion
Minimum package version to measure when Version is omitted.

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

### -ModulePath
Module source folder used by publish measurements.

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

### -ModuleRoot
Explicit module root for install, save, or update measurements.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, DestinationPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Module names to measure.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -Operation
Lifecycle operation or operations to measure.

```yaml
Type: ManagedModuleBenchmarkOperation[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Find, Install, Save, Update, Publish

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PackageCacheDirectory
Optional package cache directory.

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

### -PackageOutputDirectory
Optional package output directory used by publish measurements.

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

### -Prerelease
Include prerelease versions when resolving the latest version.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: AllowPrerelease
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved module repository profile to use instead of Repository.

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

### -ReportPath
Optional JSON report path for benchmark evidence.

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

### -Repository
Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Source, RepositoryUri
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Friendly repository name used in output.

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

### -RequireCompatibilityRetirementReady
Fail the command when compatibility transport cannot yet be marked legacy.

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

### -RequireManagedEvidenceReady
Fail the command when managed engine evidence is missing or failed, even if compatibility baselines are still incomplete.

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

### -RequireTransitionReady
Fail the command when benchmark transition gates are not ready for managed transport defaults.

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

### -Scope
Install or update scope used when ModuleRoot is not supplied.

```yaml
Type: ManagedModuleInstallScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, AllUsers, Custom

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShellEdition
PowerShell path family used when resolving default CurrentUser or AllUsers module roots.

```yaml
Type: ManagedModuleShellEdition
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, Desktop, Core

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipDependencyCheck
Skip installing dependencies declared by the package.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: SkipDependenciesCheck
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -StopOnError
Stop at the first scenario failure instead of recording failed runs and continuing.

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

### -ValidateImport
Import the delivered module in out-of-process PowerShell hosts and record version evidence.

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

### -Version
Exact package version to measure.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RequiredVersion
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionPolicy
NuGet-style version range policy used when Version is omitted.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String[]`

## OUTPUTS

- `PowerForge.ManagedModuleBenchmarkResult`

## RELATED LINKS

- None
