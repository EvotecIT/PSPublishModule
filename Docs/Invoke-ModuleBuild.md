Invoke-ModuleBuild
------------------

### Synopsis
Command to create new module or update existing one.
It will create new module structure and everything around it, or update existing one.

---

### Description

Command to create new module or update existing one.
It will create new module structure and everything around it, or update existing one.

---

### Examples
> EXAMPLE 1

```PowerShell
An example
```

---

### Parameters
#### **Settings**
Provide settings for the module in form of scriptblock.
It's using DSL to define settings for the module.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[ScriptBlock]`|false   |1       |false        |

#### **Path**
Path to the folder where new project will be created, or existing project will be updated.
If not provided it will be created in one up folder from the location of build script.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **ModuleName**
Provide name of the module. It's required parameter.

|Type      |Required|Position|PipelineInput|Aliases    |
|----------|--------|--------|-------------|-----------|
|`[String]`|true    |named   |false        |ProjectName|

#### **FunctionsToExportFolder**
Public functions folder name. Default is 'Public'.
It will be used as part of PSD1 and PSM1 to export only functions from this folder.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **AliasesToExportFolder**
Public aliases folder name. Default is 'Public'.
It will be used as part of PSD1 and PSM1 to export only aliases from this folder.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **Configuration**
Provides a way to configure module using hashtable.
It's the old way of configuring module, that requires knowledge of inner workings of the module to name proper key/value pairs
It's required for compatibility with older versions of the module.

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[IDictionary]`|true    |named   |false        |

#### **ExcludeFromPackage**
Exclude files from Artefacts. Default is '.*, 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |named   |false        |

#### **IncludeRoot**
Include files in the Artefacts from root of the project. Default is '*.psm1', '*.psd1', 'License*' files.
Other files will be ignored.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |named   |false        |

#### **IncludePS1**
Include *.ps1 files in the Artefacts from given folders. Default are 'Private', 'Public', 'Enums', 'Classes' folders.
If the folder doesn't exists it will be ignored.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |named   |false        |

#### **IncludeAll**
Include all files in the Artefacts from given folders. Default are 'Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data' folders.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |named   |false        |

#### **IncludeCustomCode**
Parameter description

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[ScriptBlock]`|false   |named   |false        |

#### **IncludeToArray**
Parameter description

|Type           |Required|Position|PipelineInput|
|---------------|--------|--------|-------------|
|`[IDictionary]`|false   |named   |false        |

#### **LibrariesCore**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **LibrariesDefault**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **LibrariesStandard**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |named   |false        |

#### **ExitCode**
Exit code to be returned to the caller. If not provided, it will not exit the script, but finish gracefully.
Exit code 0 means success, 1 means failure.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
Invoke-ModuleBuild [[-Settings] <ScriptBlock>] [-Path <String>] -ModuleName <String> [-FunctionsToExportFolder <String>] [-AliasesToExportFolder <String>] [-ExcludeFromPackage <String[]>] [-IncludeRoot <String[]>] [-IncludePS1 <String[]>] [-IncludeAll <String[]>] [-IncludeCustomCode <ScriptBlock>] [-IncludeToArray <IDictionary>] [-LibrariesCore <String>] [-LibrariesDefault <String>] [-LibrariesStandard <String>] [-ExitCode] [<CommonParameters>]
```
```PowerShell
Invoke-ModuleBuild -Configuration <IDictionary> [-ExitCode] [<CommonParameters>]
```
