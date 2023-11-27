New-ConfigurationModule
-----------------------

### Synopsis
Provides a way to configure Required Modules or External Modules that will be used in the project.

---

### Description

Provides a way to configure Required Modules or External Modules that will be used in the project.

---

### Examples
Add standard module dependencies (directly, but can be used with loop as well)

```PowerShell
New-ConfigurationModule -Type RequiredModule -Name 'platyPS' -Guid 'Auto' -Version 'Latest'
New-ConfigurationModule -Type RequiredModule -Name 'powershellget' -Guid 'Auto' -Version 'Latest'
New-ConfigurationModule -Type RequiredModule -Name 'PSScriptAnalyzer' -Guid 'Auto' -Version 'Latest'
```
Add external module dependencies, using loop for simplicity

```PowerShell
foreach ($Module in @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')) {
    New-ConfigurationModule -Type ExternalModule -Name $Module
}
```
Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
And on that time only that function and dependant functions will be copied over
Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files

```PowerShell
New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'
```

---

### Parameters
#### **Type**
Choose between RequiredModule, ExternalModule and ApprovedModule, where RequiredModule is the default.
Valid Values:

* RequiredModule
* ExternalModule
* ApprovedModule

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Object]`|false   |1       |false        |

#### **Name**
Name of PowerShell module that you want your module to depend on.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|true    |2       |false        |

#### **Version**
Version of PowerShell module that you want your module to depend on.
If you don't specify a version, any version of the module is acceptable.
You can also use word 'Latest' to specify that you want to use the latest version of the module, and the module will be pickup up latest version available on the system.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |3       |false        |

#### **RequiredVersion**
RequiredVersion of PowerShell module that you want your module to depend on.
This forces the module to require this specific version.
When using Version, the module will be picked up if it's equal or higher than the version specified.
When using RequiredVersion, the module will be picked up only if it's equal to the version specified.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |4       |false        |

#### **Guid**
Guid of PowerShell module that you want your module to depend on. If you don't specify a Guid, any Guid of the module is acceptable, but it is recommended to specify it.
Alternatively you can use word 'Auto' to specify that you want to use the Guid of the module, and the module GUID

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |5       |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationModule [[-Type] <Object>] [-Name] <String[]> [[-Version] <String>] [[-RequiredVersion] <String>] [[-Guid] <String>] [<CommonParameters>]
```
