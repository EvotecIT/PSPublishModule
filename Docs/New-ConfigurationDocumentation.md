New-ConfigurationDocumentation
------------------------------

### Synopsis
Enables or disables creation of documentation from the module using PlatyPS

---

### Description

Enables or disables creation of documentation from the module using PlatyPS

---

### Examples
> EXAMPLE 1

```PowerShell
New-ConfigurationDocumentation -Enable:$false -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'
```
> EXAMPLE 2

```PowerShell
New-ConfigurationDocumentation -Enable -PathReadme 'Docs\Readme.md' -Path 'Docs'
```

---

### Parameters
#### **Enable**
Enables creation of documentation from the module. If not specified, the documentation will not be created.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **StartClean**
Removes all files from the documentation folder before creating new documentation.
Otherwise the `Update-MarkdownHelpModule` will be used to update the documentation.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **UpdateWhenNew**
Updates the documentation right after running `New-MarkdownHelp` due to platyPS bugs.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **Path**
Path to the folder where documentation will be created.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |1       |false        |

#### **PathReadme**
Path to the readme file that will be used for the documentation.

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |2       |false        |

#### **Tool**
Tool to use for documentation generation. By default `HelpOut` is used.
Available options are `PlatyPS` and `HelpOut`.
Valid Values:

* PlatyPS
* HelpOut

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|false   |3       |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationDocumentation [-Enable] [-StartClean] [-UpdateWhenNew] [-Path] <String> [-PathReadme] <String> [[-Tool] <String>] [<CommonParameters>]
```
