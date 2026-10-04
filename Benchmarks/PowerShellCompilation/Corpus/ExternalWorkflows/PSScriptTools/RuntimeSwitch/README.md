# Offline runtime-switch workflows

These unchanged source files come from [PSScriptTools](https://github.com/jdhitsolutions/PSScriptTools) commit `549fa3e3d769532320054fc0b3c8f42df0452361`. The original MIT license is included as `license.txt`.

| Authored path | SHA-256 |
| --- | --- |
| `functions/FileNameTools.ps1` | `ea264bc7898ac3a6c27b1dbe787ec555dc40144be957290a364d8f4d0c25eb3f` |
| `functions/New-ANSIBar.ps1` | `1dfb9223fff3a621fb4022c84bbdf9f2b3b8e4b909cb8894b67f2e4145dc9ded` |
| `functions/FormatFunctions.ps1` | `0a0fea01d8c99cb43216194d3dbb25e462c573ef775c2c69324e934de5e841dd` |

The source files are regression workloads, not compiler intrinsics. Qualification imports original and generated mini modules on PowerShell 7 and Windows PowerShell 5.1. It exercises `New-CustomFileName` with deterministic templates and a session-resolved fixed clock; `New-ANSIBar` with standard/custom characters, gradients, spacing, aliases, and verbose records; and `Format-Value`/`Format-String` with nested switches, multiple pipeline records, unit/case modes, reverse/replacement, and verbose streams. Culture-sensitive formatting is compared under the same explicit culture. `New-RandomFileName` and `Format-Percent` are included in their authored files but are not executed by this packet. Random filename/GUID placeholders, randomized text, full-module import, and other platforms are outside its claim.
