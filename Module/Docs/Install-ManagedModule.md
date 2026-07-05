---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ManagedModule
## SYNOPSIS
Installs PowerShell modules through the managed C# module engine.

## SYNTAX
### NameParameterSet (Default)
```powershell
Install-ManagedModule [-Name] <string[]> [[-Repository] <string>] [-RepositoryName <string>] [-ProfileName <string>] [-Version <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-VersionPolicy <string>] [-Prerelease] [-Scope <ManagedModuleInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ModuleRoot <string>] [-PackageCacheDirectory <string>] [-DependencyConcurrency <int>] [-ExpectedPackageSha256 <string>] [-TrustPolicy <ManagedModuleTrustPolicy>] [-RequireTrustedRepository] [-AllowedAuthor <string[]>] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-Reinstall] [-AllowClobber] [-NoClobber] [-AcceptLicense] [-AuthenticodeCheck] [-SkipDependencyCheck] [-Plan] [-ShowSummary] [-Quiet] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### RequiredResourceParameterSet
```powershell
Install-ManagedModule -RequiredResource <Object> [-RepositoryName <string>] [-ProfileName <string>] [-Scope <ManagedModuleInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ModuleRoot <string>] [-PackageCacheDirectory <string>] [-DependencyConcurrency <int>] [-ExpectedPackageSha256 <string>] [-TrustPolicy <ManagedModuleTrustPolicy>] [-RequireTrustedRepository] [-AllowedAuthor <string[]>] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-Reinstall] [-AllowClobber] [-NoClobber] [-AcceptLicense] [-AuthenticodeCheck] [-SkipDependencyCheck] [-Plan] [-ShowSummary] [-Quiet] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### RequiredResourceFileParameterSet
```powershell
Install-ManagedModule -RequiredResourceFile <string> [-RepositoryName <string>] [-ProfileName <string>] [-Scope <ManagedModuleInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ModuleRoot <string>] [-PackageCacheDirectory <string>] [-DependencyConcurrency <int>] [-ExpectedPackageSha256 <string>] [-TrustPolicy <ManagedModuleTrustPolicy>] [-RequireTrustedRepository] [-AllowedAuthor <string[]>] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-Reinstall] [-AllowClobber] [-NoClobber] [-AcceptLicense] [-AuthenticodeCheck] [-SkipDependencyCheck] [-Plan] [-ShowSummary] [-Quiet] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This command is the first managed install surface. It uses PowerForge repository lookup, package download, and
safe archive extraction directly instead of invoking PowerShellGet or PSResourceGet.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-ManagedModule -Name Company.Tools
```


### EXAMPLE 2
```powershell
Install-ManagedModule -Name Company.Tools -Version 1.2.0 -Repository C:\Packages -Scope Custom -ModuleRoot C:\Modules
```


## PARAMETERS

### -AcceptLicense
Accept package licenses when packages declare license acceptance is required.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowedAuthor
Allowed package author values from package metadata.

```yaml
Type: String[]
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: RequiredAuthor, TrustedAuthor
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AuthenticodeCheck
Validate Authenticode signatures for signable package files before promotion.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DependencyConcurrency
Maximum number of dependency branches to install concurrently. Omit to use the managed engine default.

```yaml
Type: Int32
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedPackageSha256
Expected SHA256 hash of the root package before it is extracted and promoted.

```yaml
Type: String
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: PackageSha256, Sha256
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Reinstall the module version when it already exists.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaximumVersion
Maximum package version to install when Version is omitted.

```yaml
Type: String
Parameter Sets: NameParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinimumVersion
Minimum package version to install when Version is omitted.

```yaml
Type: String
Parameter Sets: NameParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleRoot
Explicit module root. Use with Scope Custom.

```yaml
Type: String
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: Path
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Module names to install.

```yaml
Type: String[]
Parameter Sets: NameParameterSet
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -NoClobber
PSResourceGet-compatible spelling for the managed default that rejects command export conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Return an inspectable install plan without writing files.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet
Aliases: AllowPrerelease
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved repository profile name.

```yaml
Type: String
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Proxy
Optional HTTP proxy used for repository requests.

```yaml
Type: Uri
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProxyCredential
Optional proxy credential used with Proxy.

```yaml
Type: PSCredential
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Quiet
Suppress optional host summaries and progress-style output without changing pipeline result objects.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Reinstall
PSResourceGet-compatible spelling for reinstalling the selected module version when it already exists.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet
Aliases: Source, RepositoryUri
Possible values:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Friendly repository name used in output.

```yaml
Type: String
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredResource
PSResourceGet-style required resource map to install.

```yaml
Type: Object
Parameter Sets: RequiredResourceParameterSet
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredResourceFile
Path to a PowerShell data file containing a PSResourceGet-style required resource map.

```yaml
Type: String
Parameter Sets: RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequireTrustedRepository
Require the selected repository profile to be marked trusted.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Install scope used when ModuleRoot is not supplied.

```yaml
Type: ManagedModuleInstallScope
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values: Auto, Desktop, Core

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSummary
Write a compact Spectre.Console summary for each plan or result.

```yaml
Type: SwitchParameter
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

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
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: SkipDependenciesCheck
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TrustPolicy
Optional typed repository/package trust policy.

```yaml
Type: ManagedModuleTrustPolicy
Parameter Sets: NameParameterSet, RequiredResourceParameterSet, RequiredResourceFileParameterSet
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Exact package version to install. When omitted, the latest repository version is used.

```yaml
Type: String
Parameter Sets: NameParameterSet
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
Parameter Sets: NameParameterSet
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

- `PowerForge.ManagedModuleInstallResult
PowerForge.ManagedModuleInstallPlan`

## RELATED LINKS

- None
