Get-MissingFunctions
--------------------

### Synopsis

Get-MissingFunctions [[-FilePath] <string>] [[-Code] <scriptblock>] [[-Functions] <string[]>] [[-ApprovedModules] <array>] [[-IgnoreFunctions] <array>] [-Summary] [-SummaryWithCommands] [<CommonParameters>]

---

### Description

---

### Parameters
#### **ApprovedModules**

|Type     |Required|Position|PipelineInput|
|---------|--------|--------|-------------|
|`[array]`|false   |3       |false        |

#### **Code**

|Type           |Required|Position|PipelineInput|Aliases    |
|---------------|--------|--------|-------------|-----------|
|`[scriptblock]`|false   |1       |false        |ScriptBlock|

#### **FilePath**

|Type      |Required|Position|PipelineInput|Aliases|
|----------|--------|--------|-------------|-------|
|`[string]`|false   |0       |false        |Path   |

#### **Functions**

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[string[]]`|false   |2       |false        |

#### **IgnoreFunctions**

|Type     |Required|Position|PipelineInput|
|---------|--------|--------|-------------|
|`[array]`|false   |4       |false        |

#### **Summary**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **SummaryWithCommands**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

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
{@{name=Get-MissingFunctions; CommonParameters=True; parameter=System.Object[]}}
```
