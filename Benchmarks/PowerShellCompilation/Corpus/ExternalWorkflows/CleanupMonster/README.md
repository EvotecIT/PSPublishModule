# Pinned CleanupMonster workflow

`Get-ComputerLookupCandidates.ps1` is the unchanged complete function from [CleanupMonster commit f215b32](https://github.com/EvotecIT/CleanupMonster/blob/f215b32ca86b38d9150629c08f6c0c232bfafb27/Private/Get-ComputerLookupCandidates.ps1). Its SHA-256 is `d7decce312bf7c17965ac2c018604a2c6c465c268296e9f9ea6bd283b9e98274`. The compiler discovery packet pins the containing archive separately. That snapshot contains no standalone license file.

`CompleteWorkflow_PinnedLookupCompilesClosureAndPreservesOrder` compares the original function with its compiled Hybrid artifact on Windows PowerShell 5.1 and PowerShell 7.6.5. Ten input cases run twice, followed by a fresh invocation. They cover empty inputs, trimming, case-insensitive deduplication, DNS short names, account-name suffixes, Unicode, output order and types, and per-call collection isolation. The function operates entirely in memory.

The complete function and its assigned script block use generated CLR bodies. PowerShell supplies native scope, invocation, and operator behavior; the ledger records six command boundaries, including one inside the child block. The nested graph keeps child invocation order separate from the parent. This is a Hybrid qualification, and it does not satisfy the M26 stateful module exit gate.

`HashSet<T>` uses the shared target-compatible CLR type contract. Separate artifact checks cover native Hybrid comparer, aliasing and enumeration behavior, plus runtime-free construction and public set identity from C# consumers on .NET 8 and .NET 10. Script-block checks cover nested scope, escaped blocks, `GetNewClosure()`, error continuation, lifecycle clauses, downstream stop, and cancellation. Unsupported child bodies retain their owning function; these contracts do not enable runtime-free script blocks.

The saved-state workflow uses two more unchanged files from the same commit:

| File | SHA-256 |
| --- | --- |
| `Import-ComputersData.ps1` | `98bd6c47fd4adb1ee6b74901b8cc2330912786d24d03067356f4229a025b1324` |
| `Convert-ListProcessed.ps1` | `443dc2a79c156d4b15c9e17dc85068113433096bef1805edb0daf9adaac06785` |

`CompleteWorkflow_PinnedSavedStatePreservesConversionAndAliasing` imports the two-function closure and compares its original and generated Hybrid modules on PowerShell 5.1, 7.4.19, and 7.6.5. Both function bodies compile. Nine scenarios run twice and cover absent or empty state, legacy key conversion, already converted keys, existing properties, absent dates, read/conversion failures, and invalid history. Comparisons preserve output, diagnostics, ordered keys, repeat-call mutation, and shared object identity.

The test replaces filesystem, clock, diagnostic, and distinguished-name providers inside an isolated module. All state stays in memory. This qualifies the saved-state closure; it does not yet qualify the complete CleanupMonster module's initialization, cancellation, removal/reimport, or multiple-region lifecycle.

`FullModule/` contains the original `CleanupMonster.psm1` and all 67 files under `Private/` and `Public/` from the same pinned commit. `SHA256SUMS.txt` records their exact bytes; its SHA-256 is `efe6355df50a57bb06472c45378242b6bbff4557d0be5d6c10edd8d21ee5b530`. The source files contain function declarations, and the snapshot contains no assembly directory. The qualification imports the source module directly and invokes only the saved-state workflow with isolated providers. It does not import the dependency manifest or execute the public administration entrypoints.

`CompleteWorkflow_PinnedSavedStateInFullModule` compares the same 18 observations within that full source context on PowerShell 5.1, 7.4.19, and 7.6.5. Both saved-state functions compile while the original loader discovers the surrounding functions. The test also compares eight lifecycle observations per host: cancellation during the read or second key conversion, a subsequent invocation in the same runspace, module removal/reimport, and a fresh call. Retained in-memory providers expose cleanup counts, ordered state mutations, diagnostic traces, and pending/history object identity. The stopped command has no partial success records and reports the native cancellation exception identity. Explain output is checked against the delivered module's disposition ledger and region graphs.

Cancellation after one converted key leaves a partially converted dictionary. On the next call, the pinned source removes the previously converted entry on PowerShell 7 and retains both mixed-format keys on Windows PowerShell 5.1. The artifact preserves each host's behavior. A fresh import and provider setup start with both original entries. These checks qualify source-module initialization, discovery, and the observed saved-state lifecycle. They do not qualify the dependency manifest, real state-file access, public administration entrypoints, or the compiler's broader multiple-region selection gate.
