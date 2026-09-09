# Pinned CleanupMonster workflow

`Get-ComputerLookupCandidates.ps1` is the unchanged complete function from [CleanupMonster commit f215b32](https://github.com/EvotecIT/CleanupMonster/blob/f215b32ca86b38d9150629c08f6c0c232bfafb27/Private/Get-ComputerLookupCandidates.ps1). Its SHA-256 is `d7decce312bf7c17965ac2c018604a2c6c465c268296e9f9ea6bd283b9e98274`. The compiler discovery packet pins the containing archive separately. That snapshot contains no standalone license file.

`CompleteWorkflow_PinnedLookupRetainsUnprovedClosureAndPreservesOrder` compares the original function with its Hybrid artifact on Windows PowerShell 5.1 and PowerShell 7.6.5. Ten input cases run twice, followed by a fresh invocation. They cover empty inputs, trimming, case-insensitive deduplication, DNS short names, account-name suffixes, Unicode, output order and types, and per-call collection isolation. The function operates entirely in memory.

This is a retained-boundary regression, not a compiled-workflow qualification. The ledger correctly retains the complete body: the generated type policy does not admit `HashSet<T>`, and the nested script block captures and mutates caller-owned collections. Compiling this workflow requires generic type and closure contracts; no fixture-specific compiler rules are permitted. It does not satisfy the M26 stateful module exit gate.
