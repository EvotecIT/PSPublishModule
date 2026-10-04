# Offline hashtable parsing

`hashtableTools.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/hashtableTools.ps1`, SHA-256 `8c84ab506998fb28e70c067eda893fad8fe48e2a7f2ccf98e9c87248d5c34293`. The original MIT license is included. The complete file preserves upstream source; the execution qualification extracts the unchanged `Convert-HashtableString` function.

Original/generated comparisons cover direct and repeated pipeline input on Windows PowerShell 5.1/net472 and PowerShell 7/net10.0. Cases include simple/nested/ordered/empty hashtables, non-hashtable input, syntax errors and a command expression that must remain unevaluated by `SafeGetValue`. Values, errors and source positions agree. Unordered hashtable key enumeration is not treated as observable ordering.

Separate synthetic probes qualify two distinct trailing local references to `Parser.ParseInput`, replacement of prior token/error arrays, typed-reference binding failure, real CLR string-conversion callbacks and their failures, and finally execution. They do not qualify a method-body exception after output writes or a constraint failure midway through multiple writebacks. Aliased, scoped, non-trailing and dynamically created reference storage retains its PowerShell boundary.

The other five functions are preserved source, not new execution claims. In particular, `Convert-CommandToHashtable` creates variables through `New-Variable` and still lacks qualified native reference storage. This is Hybrid compilation with an active PowerShell host, not a runtime-free parser or full-module qualification.
