Initialize-ProjectManager
-------------------------

### Synopsis
Builds VSCode Project manager config from filesystem

---

### Description

Builds VSCode Project manager config from filesystem

---

### Examples
> EXAMPLE 1

```PowerShell
Initialize-ProjectManager -Path "C:\Support\GitHub"
```
> EXAMPLE 2

```PowerShell
Initialize-ProjectManager -Path "C:\Support\GitHub" -DisableSorting
```

---

### Parameters
#### **Path**
Path to where the projects are located

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |1       |false        |

#### **DisableSorting**
Disables sorting of the projects by last modified date

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
Initialize-ProjectManager [-Path] <String> [-DisableSorting] [<CommonParameters>]
```
