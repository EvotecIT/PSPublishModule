New-ConfigurationArtefact
-------------------------

### Synopsis
Tells the module to create artefact of specified type

---

### Description

Tells the module to create artefact of specified type
There can be multiple artefacts created (even of same type)
At least one packed artefact is required for publishing to GitHub

---

### Examples
> EXAMPLE 1

```PowerShell
New-ConfigurationArtefact -Type Unpacked -Enable -Path "$PSScriptRoot\..\Artefacts\Unpacked" -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Unpacked\Modules"
```
standard artefact, packed with tag name without any additional modules or required modules

```PowerShell
New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed" -IncludeTagName
```
Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file

```PowerShell
New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" -IncludeTagName
```
Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file
But additionally pack it into zip fileĄŚż$%#

```PowerShell
New-ConfigurationArtefact -Type ScriptPacked -Enable -Path "$PSScriptRoot\..\Artefacts\ScriptPacked" -ArtefactName "Script-<ModuleName>-$((Get-Date).ToString('yyyy-MM-dd')).zip"
```

---

### Parameters
#### **PostScriptMerge**
ScriptBlock that will be added in the end of the script. It's only applicable to type of Script, PackedScript.
If useed with PostScriptMergePath, this will be ignored.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[ScriptBlock]`|false   |1       |false        |

#### **PreScriptMerge**
ScriptBlock that will be added in the beggining of the script. It's only applicable to type of Script, PackedScript.
If useed with PreScriptMergePath, this will be ignored.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[ScriptBlock]`|false   |2       |false        |

#### **Type**
There are 4 types of artefacts:
* Unpacked - unpacked module (useful for testing)
* Packed - packed module (as zip) - usually used for publishing to GitHub or copying somewhere
* Script - script that is module in form of PS1 without PSD1 - only applicable to very simple modules
* PackedScript - packed module (as zip) that is script that is module in form of PS1 without PSD1 - only applicable to very simple modules
Valid Values:

* Unpacked
* Packed
* Script
* ScriptPacked

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |named   |false        |

#### **Enable**
Enable artefact creation. By default artefact creation is disabled.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **IncludeTagName**
Include tag name in artefact name. By default tag name is not included.
Alternatively you can provide ArtefactName parameter to specify your own artefact name (with or without TagName)

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **Path**
Path where artefact will be created.
Please choose a separate directory for each artefact type, as logic may be interfering one another.
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **AddRequiredModules**
Add required modules to artefact by copying them over. By default required modules are not added.

|Type      |Required|Position|PipelineInput|Aliases        |
|----------|--------|--------|-------------|---------------|
|`[Switch]`|false   |named   |false        |RequiredModules|

#### **ModulesPath**
Path where main module or required module (if not specified otherwise in RequiredModulesPath) will be copied to.
By default it will be put in the Path folder if not specified
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **RequiredModulesPath**
Path where required modules will be copied to. By default it will be put in the Path folder if not specified.
If ModulesPath is specified, but RequiredModulesPath is not specified it will be put into ModulesPath folder.
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **CopyDirectories**
Provide Hashtable of directories to copy to artefact. Key is source directory, value is destination directory.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[IDictionary]`|false   |named   |false        |

#### **CopyFiles**
Provide Hashtable of files to copy to artefact. Key is source file, value is destination file.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[IDictionary]`|false   |named   |false        |

#### **CopyDirectoriesRelative**
Define if destination directories should be relative to artefact root. By default they are not.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **CopyFilesRelative**
Define if destination files should be relative to artefact root. By default they are not.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DoNotClear**
Do not clear artefact directory before creating artefact. By default artefact directory is cleared.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **ArtefactName**
The name of the artefact. If not specified, the default name will be used.
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **ScriptName**
The name of the script. If not specified, the default name will be used.
Only applicable to Script and ScriptPacked artefacts.
You can use following variables that will be replaced with actual values:
* <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
* <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
* <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
* <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
* <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

|Type      |Required|Position|PipelineInput|Aliases |
|----------|--------|--------|-------------|--------|
|`[String]`|false   |named   |false        |FileName|

#### **ID**
Optional ID of the artefact. To be used by New-ConfigurationPublish cmdlet
If not specified, the first packed artefact will be used for publishing to GitHub

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **PostScriptMergePath**
Path to file that will be added in the end of the script. It's only applicable to type of Script, PackedScript.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **PreScriptMergePath**
Path to file that will be added in the beggining of the script. It's only applicable to type of Script, PackedScript.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationArtefact [[-PostScriptMerge] <ScriptBlock>] [[-PreScriptMerge] <ScriptBlock>] -Type <String> [-Enable] [-IncludeTagName] [-Path <String>] [-AddRequiredModules] [-ModulesPath <String>] [-RequiredModulesPath <String>] [-CopyDirectories <IDictionary>] [-CopyFiles <IDictionary>] [-CopyDirectoriesRelative] [-CopyFilesRelative] [-DoNotClear] [-ArtefactName <String>] [-ScriptName <String>] [-ID <String>] [-PostScriptMergePath <String>] [-PreScriptMergePath <String>] [<CommonParameters>]
```
