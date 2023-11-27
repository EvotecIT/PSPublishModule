Register-Certificate
--------------------

### Synopsis

Register-Certificate -CertificatePFX <string> -Path <string> [-TimeStampServer <string>] [-IncludeChain <string>] [-Include <string[]>] [-HashAlgorithm <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-Certificate -LocalStore <string> -Path <string> [-Thumbprint <string>] [-TimeStampServer <string>] [-IncludeChain <string>] [-Include <string[]>] [-HashAlgorithm <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

---

### Description

---

### Parameters
#### **CertificatePFX**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |Named   |false        |

#### **Confirm**
-Confirm is an automatic variable that is created when a command has ```[CmdletBinding(SupportsShouldProcess)]```.
-Confirm is used to -Confirm each operation.

If you pass ```-Confirm:$false``` you will not be prompted.

If the command sets a ```[ConfirmImpact("Medium")]``` which is lower than ```$confirmImpactPreference```, you will not be prompted unless -Confirm is passed.

#### **HashAlgorithm**

Valid Values:

* SHA1
* SHA256
* SHA384
* SHA512

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |Named   |false        |

#### **Include**

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[string[]]`|false   |Named   |false        |

#### **IncludeChain**

Valid Values:

* All
* NotRoot
* Signer

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |Named   |false        |

#### **LocalStore**

Valid Values:

* LocalMachine
* CurrentUser

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |Named   |false        |

#### **Path**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|true    |Named   |false        |

#### **Thumbprint**

|Type      |Required|Position|PipelineInput|Aliases              |
|----------|--------|--------|-------------|---------------------|
|`[string]`|false   |Named   |false        |CertificateThumbprint|

#### **TimeStampServer**

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[string]`|false   |Named   |false        |

#### **WhatIf**
-WhatIf is an automatic variable that is created when a command has ```[CmdletBinding(SupportsShouldProcess)]```.
-WhatIf is used to see what would happen, or return operations without executing them

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
{@{name=Register-Certificate; CommonParameters=True; parameter=System.Object[]}, @{name=Register-Certificate; CommonParameters=True; parameter=System.Object[]}}
```
