---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ManagedScript
## SYNOPSIS
Installs script resources through the managed C# resource engine.

## SYNTAX
### __AllParameterSets
```powershell
Install-ManagedScript [-Name] <string[]> [[-Repository] <string>] [-RepositoryName <string>] [-ProfileName <string>] [-Version <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-VersionPolicy <string>] [-Prerelease] [-Scope <ManagedScriptInstallScope>] [-ShellEdition <ManagedModuleShellEdition>] [-ScriptRoot <string>] [-PackageCacheDirectory <string>] [-ExpectedPackageSha256 <string>] [-TrustPolicy <ManagedModuleTrustPolicy>] [-RequireTrustedRepository] [-AllowedAuthor <string[]>] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-AcceptLicense] [-Plan] [-NoPathUpdate] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Installs script resources through the managed C# resource engine.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-ManagedScript -Name Invoke-CompanyTask -Scope CurrentUser
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

### -AllowedAuthor
Allowed package author values from package metadata.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: RequiredAuthor, TrustedAuthor
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

### -ExpectedPackageSha256
Expected SHA256 hash of the script package before it is extracted and installed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: PackageSha256, Sha256
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Reinstall the script version when it already exists.

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

### -MaximumVersion
Maximum package version to install when Version is omitted.

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
Minimum package version to install when Version is omitted.

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

### -Name
Script resource names to install.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ScriptName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -NoPathUpdate
Do not add the resolved script root to the current process PATH after installation.

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

### -Plan
Return an inspectable install plan without writing files.

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
Saved repository profile name.

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

### -Proxy
Optional HTTP proxy used for repository requests.

```yaml
Type: Uri
Parameter Sets: __AllParameterSets
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
Position: 1
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

### -RequireTrustedRepository
Require the selected repository profile to be marked trusted.

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
Install scope used when ScriptRoot is not supplied.

```yaml
Type: ManagedScriptInstallScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, AllUsers, Custom

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptRoot
Explicit script root. When supplied, Scope is treated as Custom.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShellEdition
PowerShell path family used when resolving default CurrentUser or AllUsers script roots.

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

### -TrustPolicy
Optional typed repository/package trust policy.

```yaml
Type: ManagedModuleTrustPolicy
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
Exact package version to install. When omitted, the latest repository version is used.

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

- `PowerForge.ManagedScriptInstallResult
PowerForge.ManagedScriptInstallPlan`

## RELATED LINKS

- None
