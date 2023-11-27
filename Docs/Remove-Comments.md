Remove-Comments
---------------

### Synopsis
Remove comments from PowerShell file

---

### Description

Remove comments from PowerShell file and optionally remove empty lines
By default comments in param block are not removed
By default comments before param block are not removed

---

### Examples
> EXAMPLE 1

```PowerShell
Remove-Comments -SourceFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript.ps1' -DestinationFilePath 'C:\Support\GitHub\PSPublishModule\Examples\TestScript1.ps1' -RemoveAllEmptyLines -RemoveCommentsInParamBlock -RemoveCommentsBeforeParamBlock
```

---

### Parameters
#### **SourceFilePath**
File path to the source file

|Type      |Required|Position|PipelineInput|Aliases                          |
|----------|--------|--------|-------------|---------------------------------|
|`[String]`|true    |named   |false        |FilePath<br/>Path<br/>LiteralPath|

#### **Content**
Content of the file

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[String]`|true    |named   |false        |

#### **DestinationFilePath**
File path to the destination file. If not provided, the content will be returned

|Type      |Required|Position|PipelineInput|Aliases                                      |
|----------|--------|--------|-------------|---------------------------------------------|
|`[String]`|false   |named   |false        |Destination<br/>OutputFile<br/>OutputFilePath|

#### **RemoveAllEmptyLines**
Remove all empty lines from the content

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **RemoveEmptyLines**
Remove empty lines if more than one empty line is found

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **RemoveCommentsInParamBlock**
Remove comments in param block. By default comments in param block are not removed

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **RemoveCommentsBeforeParamBlock**
Remove comments before param block. By default comments before param block are not removed

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

#### **DoNotRemoveSignatureBlock**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

---

### Notes
Most of the work done by Chris Dent, with improvements by Przemyslaw Klys

---

### Syntax
```PowerShell
Remove-Comments -SourceFilePath <String> [-DestinationFilePath <String>] [-RemoveAllEmptyLines] [-RemoveEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-DoNotRemoveSignatureBlock] [<CommonParameters>]
```
```PowerShell
Remove-Comments -Content <String> [-DestinationFilePath <String>] [-RemoveAllEmptyLines] [-RemoveEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-DoNotRemoveSignatureBlock] [<CommonParameters>]
```
