New-ConfigurationFormat
-----------------------

### Synopsis

New-ConfigurationFormat [-ApplyTo] <string[]> [[-Sort] <string>] [[-UseConsistentIndentationKind] <string>] [[-UseConsistentIndentationPipelineIndentation] <string>] [[-UseConsistentIndentationIndentationSize] <int>] [[-PSD1Style] <string>] [-EnableFormatting] [-RemoveComments] [-RemoveEmptyLines] [-RemoveAllEmptyLines] [-RemoveCommentsInParamBlock] [-RemoveCommentsBeforeParamBlock] [-PlaceOpenBraceEnable] [-PlaceOpenBraceOnSameLine] [-PlaceOpenBraceNewLineAfter] [-PlaceOpenBraceIgnoreOneLineBlock] [-PlaceCloseBraceEnable] [-PlaceCloseBraceNewLineAfter] [-PlaceCloseBraceIgnoreOneLineBlock] [-PlaceCloseBraceNoEmptyLineBefore] [-UseConsistentIndentationEnable] [-UseConsistentWhitespaceEnable] [-UseConsistentWhitespaceCheckInnerBrace] [-UseConsistentWhitespaceCheckOpenBrace] [-UseConsistentWhitespaceCheckOpenParen] [-UseConsistentWhitespaceCheckOperator] [-UseConsistentWhitespaceCheckPipe] [-UseConsistentWhitespaceCheckSeparator] [-AlignAssignmentStatementEnable] [-AlignAssignmentStatementCheckHashtable] [-UseCorrectCasingEnable] [<CommonParameters>]

---

### Description

---

### Parameters
#### **AlignAssignmentStatementCheckHashtable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **AlignAssignmentStatementEnable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **ApplyTo**

Valid Values:

* OnMergePSM1
* OnMergePSD1
* DefaultPSM1
* DefaultPSD1

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[string[]]`|true    |0       |false        |

#### **EnableFormatting**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PSD1Style**

Valid Values:

* Minimal
* Native

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |5       |false        |

#### **PlaceCloseBraceEnable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceCloseBraceIgnoreOneLineBlock**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceCloseBraceNewLineAfter**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceCloseBraceNoEmptyLineBefore**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceOpenBraceEnable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceOpenBraceIgnoreOneLineBlock**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceOpenBraceNewLineAfter**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **PlaceOpenBraceOnSameLine**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **RemoveAllEmptyLines**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **RemoveComments**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **RemoveCommentsBeforeParamBlock**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **RemoveCommentsInParamBlock**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **RemoveEmptyLines**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **Sort**

Valid Values:

* None
* Asc
* Desc

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |1       |false        |

#### **UseConsistentIndentationEnable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentIndentationIndentationSize**

|Type   |Required|Position|PipelineInput|
|-------|--------|--------|-------------|
|`[int]`|false   |4       |false        |

#### **UseConsistentIndentationKind**

Valid Values:

* space
* tab

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |2       |false        |

#### **UseConsistentIndentationPipelineIndentation**

Valid Values:

* IncreaseIndentationAfterEveryPipeline
* NoIndentation

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |3       |false        |

#### **UseConsistentWhitespaceCheckInnerBrace**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceCheckOpenBrace**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceCheckOpenParen**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceCheckOperator**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceCheckPipe**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceCheckSeparator**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseConsistentWhitespaceEnable**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[switch]`|false   |Named   |false        |

#### **UseCorrectCasingEnable**

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
{@{name=New-ConfigurationFormat; CommonParameters=True; parameter=System.Object[]}}
```
