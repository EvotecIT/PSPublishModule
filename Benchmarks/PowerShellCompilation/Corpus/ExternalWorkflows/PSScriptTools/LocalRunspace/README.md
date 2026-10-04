# Offline local runspace cleanup

`remove-runspace.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/remove-runspace.ps1`, SHA-256 `9f07a632413fab809840d7c460fa2108e5db2633d09b64081dfa24b575519451`. The original MIT license is included. Git attributes preserve source and license bytes.

The qualification builds this source as a Hybrid binary module for PowerShell 7/net10.0 and Windows PowerShell 5.1/net472. Ten observations per host compare task-created local runspaces: idle cleanup, unopened/closed/disposed state, WhatIf reuse, ID lookup, busy refusal and child completion, pipeline binding, null and missing-ID errors. The driver bounds child waits and closes/disposes every owned runspace, pipeline and synchronization signal in finally. A synthetic function separately observes caller identity and native bound-parameter consistency.

Only local execution is qualified. The original state-string comparison and state-change races are preserved. No remote connection, administration, scheduler, full module, general cancellation, or reopen-after-removal proof is claimed. Top-level argument-completer registration and hosted commands remain PowerShell operations. Array/List signatures have separate semantic checks but no runtime container identity claim. Runtime-free Strict and PSSession stay closed.
