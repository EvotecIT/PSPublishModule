New-ConfigurationManifest
-------------------------

### Synopsis

New-ConfigurationManifest [-ModuleVersion] <string> [[-CompatiblePSEditions] <string[]>] [-GUID] <string> [-Author] <string> [[-CompanyName] <string>] [[-Copyright] <string>] [[-Description] <string>] [[-PowerShellVersion] <string>] [[-Tags] <string[]>] [[-IconUri] <string>] [[-ProjectUri] <string>] [[-DotNetFrameworkVersion] <string>] [[-LicenseUri] <string>] [[-Prerelease] <string>] [<CommonParameters>]

---

### Description

---

### Parameters
#### **Author**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |3       |false        |

#### **CompanyName**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |4       |false        |

#### **CompatiblePSEditions**

Valid Values:

* Desktop
* Core

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[string[]]`|false   |1       |false        |

#### **Copyright**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |5       |false        |

#### **Description**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |6       |false        |

#### **DotNetFrameworkVersion**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |11      |false        |

#### **GUID**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |2       |false        |

#### **IconUri**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |9       |false        |

#### **LicenseUri**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |12      |false        |

#### **ModuleVersion**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |0       |false        |

#### **PowerShellVersion**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |7       |false        |

#### **Prerelease**

|Type      |Required|Position|PipelineInput|Aliases      |
|----------|--------|--------|-------------|-------------|
|`[string]`|false   |13      |false        |PrereleaseTag|

#### **ProjectUri**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |10      |false        |

#### **Tags**

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[string[]]`|false   |8       |false        |

---

### Inputs
None

---

### Outputs
* [Object](https://learn.microsoft.com/en-us/dotnet/api/System.Object)

---

### Syntax
```PowerShell
syntaxItem
```
```PowerShell
----------
```
```PowerShell
{@{name=New-ConfigurationManifest; CommonParameters=True; parameter=System.Object[]}}
```
