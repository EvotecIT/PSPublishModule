New-ConfigurationModuleSkip
---------------------------

### Synopsis
Provides a way to ignore certain commands or modules during build process and continue module building on errors.

---

### Description

Provides a way to ignore certain commands or modules during build process and continue module building on errors.
During build if a build module can't find require module or command it will fail the build process to prevent incomplete module from being created.
This option allows to skip certain modules or commands and continue building the module.
This is useful for commands we know are not available on all systems, or we get them different way.

---

### Examples
> EXAMPLE 1

```PowerShell
New-ConfigurationModuleSkip -IgnoreFunctionName 'Invoke-Formatter', 'Find-Module' -IgnoreModuleName 'platyPS'
```

---

### Parameters
#### **IgnoreModuleName**
Ignore module name or names. If the module is not available on the system it will be ignored and build process will continue.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |1       |false        |

#### **IgnoreFunctionName**
Ignore function name or names. If the function is not available in the module it will be ignored and build process will continue.

|Type        |Required|Position|PipelineInput|
|------------|--------|--------|-------------|
|`[String[]]`|false   |2       |false        |

#### **Force**
This switch will force build process to continue even if the module or command is not available (aka you know what you are doing)

|Type      |Required|Position|PipelineInput|
|----------|--------|--------|-------------|
|`[Switch]`|false   |named   |false        |

---

### Notes
General notes

---

### Syntax
```PowerShell
New-ConfigurationModuleSkip [[-IgnoreModuleName] <String[]>] [[-IgnoreFunctionName] <String[]>] [-Force] [<CommonParameters>]
```
