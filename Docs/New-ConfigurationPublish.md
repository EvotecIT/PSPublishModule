New-ConfigurationPublish
------------------------

### Synopsis
Provide a way to configure publishing to PowerShell Gallery or GitHub

---

### Description

Provide a way to configure publishing to PowerShell Gallery or GitHub
You can configure publishing to both at the same time
You can publish to multiple PowerShellGalleries at the same time as well
You can have multiple GitHub configurations at the same time as well

---

### Examples
> EXAMPLE 1

```PowerShell
New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$true
```
> EXAMPLE 2

```PowerShell
New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHub'
```

---

### Parameters
#### **Type**
Choose between PowerShellGallery and GitHub
Valid Values:

* PowerShellGallery
* GitHub

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |named   |false        |

#### **FilePath**
API Key to be used for publishing to GitHub or PowerShell Gallery in clear text in file

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |named   |false        |

#### **ApiKey**
API Key to be used for publishing to GitHub or PowerShell Gallery in clear text

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |named   |false        |

#### **UserName**
When used for GitHub this parameter is required to know to which repository to publish.
This parameter is not used for PSGallery publishing

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **RepositoryName**
When used for PowerShellGallery publishing this parameter provides a way to overwrite default PowerShellGallery and publish to a different repository
When not used, the default PSGallery will be used.
When used for GitHub publishing this parameter provides a way to overwrite default repository name and publish to a different repository
When not used, the default repository name will be used, that matches the module name

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **Enabled**
Enable publishing to GitHub or PowerShell Gallery

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **OverwriteTagName**
Allow to overwrite tag name when publishing to GitHub. By default "v<ModuleVersion>" will be used i.e v1.0.0
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **Force**
Allow to publish lower version of module on PowerShell Gallery. By default it will fail if module with higher version already exists.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **ID**
Optional ID of the artefact. If not specified, the default packed artefact will be used.
If no packed artefact is specified, the first packed artefact will be used (if enabled)
If no packed artefact is enabled, the publishing will fail

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **DoNotMarkAsPreRelease**
Allow to publish to GitHub as release even if pre-release tag is set on the module version.
By default it will be published as pre-release if pre-release tag is set.
This setting prevents it.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationPublish -Type <String> -FilePath <String> [-UserName <String>] [-RepositoryName <String>] [-Enabled] [-OverwriteTagName <String>] [-Force] [-ID <String>] [-DoNotMarkAsPreRelease] [<CommonParameters>]
```
```PowerShell
New-ConfigurationPublish -Type <String> -ApiKey <String> [-UserName <String>] [-RepositoryName <String>] [-Enabled] [-OverwriteTagName <String>] [-Force] [-ID <String>] [-DoNotMarkAsPreRelease] [<CommonParameters>]
```
