New-ConfigurationBuild
----------------------

### Synopsis
Short description

---

### Description

Long description

---

### Examples
> EXAMPLE 1

```PowerShell
An example
```

---

### Parameters
#### **Enable**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DeleteTargetModuleBeforeBuild**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **MergeModuleOnBuild**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **MergeFunctionsFromApprovedModules**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **SignModule**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DotSourceClasses**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DotSourceLibraries**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **SeparateFileLibraries**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **RefreshPSD1Only**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **UseWildcardForFunctions**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **LocalVersioning**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DoNotAttemptToFixRelativePaths**
Configures module builder to not replace $PSScriptRoot\..\ with $PSScriptRoot\
This is useful if you have a module that has a lot of relative paths that are required when using Private/Public folders,
but for merge process those are not supposed to be there as the paths change.
By default module builder will attempt to fix it. This option disables this functionality.
Best practice is to use $MyInvocation.MyCommand.Module.ModuleBase or similar instead of relative paths.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **MergeLibraryDebugging**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **ResolveBinaryConflicts**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **ResolveBinaryConflictsName**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |1       |false        |

#### **CertificateThumbprint**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |2       |false        |

#### **CertificatePFXPath**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |3       |false        |

#### **CertificatePFXBase64**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |4       |false        |

#### **CertificatePFXPassword**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |5       |false        |

#### **NETConfiguration**
Parameter description
Valid Values:

* Release
* Debug

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |6       |false        |

#### **NETFramework**
Parameter description

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |7       |false        |

#### **NETProjectName**
Parameter description

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |8       |false        |

#### **NETExcludeMainLibrary**
Exclude main library from build, this is useful if you have C# project that you want to build
that is used mostly for generating libraries that are used in PowerShell module
It won't include main library in the build, but it will include all other libraries

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **NETExcludeLibraryFilter**
Provide list of filters for libraries that you want to exclude from build, this is useful if you have C# project that you want to build, but don't want to include all libraries for some reason

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |9       |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationBuild [-Enable] [-DeleteTargetModuleBeforeBuild] [-MergeModuleOnBuild] [-MergeFunctionsFromApprovedModules] [-SignModule] [-DotSourceClasses] [-DotSourceLibraries] [-SeparateFileLibraries] [-RefreshPSD1Only] [-UseWildcardForFunctions] [-LocalVersioning] [-DoNotAttemptToFixRelativePaths] [-MergeLibraryDebugging] [-ResolveBinaryConflicts] [[-ResolveBinaryConflictsName] <String>] [[-CertificateThumbprint] <String>] [[-CertificatePFXPath] <String>] [[-CertificatePFXBase64] <String>] [[-CertificatePFXPassword] <String>] [[-NETConfiguration] <String>] [[-NETFramework] <String[]>] [[-NETProjectName] <String>] [-NETExcludeMainLibrary] [[-NETExcludeLibraryFilter] <String[]>] [<CommonParameters>]
```
