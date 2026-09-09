# Pinned TimeSpan workflow

`Format-Timespan.ps1` is the unchanged complete function from [SamErde/PowerShell commit 7862250](https://github.com/SamErde/PowerShell/blob/786225096078651d0fcbc535136d074b4c500698/General/Format-Timespan.ps1). Its MIT license is included. The file preserves the original UTF-8 BOM and CRLF bytes, with SHA-256 `2f502f153597259dda9e9520685ce31ed2a5d8c7981099c4d41b2229ff3ae028`.

`CompleteWorkflow_PinnedTimeSpanPreservesTypedCollectionOutput` compiles the complete function as a Strict CLR library and compares its output against Windows PowerShell 5.1, PowerShell 7.4, and PowerShell 7.6. Each host contributes 414 observations covering three cultures, both label modes, named/positional/pipeline calls to the original function, empty and repeated input, formatting thresholds, negative values, and the TimeSpan range limits.

`CompleteWorkflow_PinnedTimeSpanPreservesNativeLifecycleOutput` compiles the same unchanged function as a Hybrid command with native begin/process callbacks. It matches the same 414 observations plus 36 cases covering null, malformed, overflow, and nested input, partial binding failure, downstream stop, and a later successful invocation on each host. Native PowerShell still owns command binding and formatting semantics; the authored lifecycle bodies are emitted as CLR code.

The library exposes a typed `TimeSpan[]` input and `string[]` output. Begin runs once and process runs for each input record. An empty array produces an empty result. A null collection is rejected with the declared CLR argument exception contract before begin; this library contract does not reproduce PowerShell parameter-binding error continuation for null records. A separate C# consumer checks output and verifies that execution does not load System.Management.Automation.

This fixture performs only in-memory formatting. Its qualification does not establish support for the rest of the repository or arbitrary advanced-function lifecycle shapes.
