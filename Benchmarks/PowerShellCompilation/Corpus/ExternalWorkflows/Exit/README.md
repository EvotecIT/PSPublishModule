# Authored function exits

`Observe.ps1` compares unchanged function bodies with their generated Hybrid
hosted artifacts in isolated child PowerShell processes. It records exit codes,
caller continuation, warnings/errors and files created under a caller-supplied
owned output directory.

| Workflow | Source | Revision | SHA-256 |
| --- | --- | --- | --- |
| Availability | PSSharedGoods `Public/TestFunctionality/Test-ModuleAvailability.ps1` | `2a807a4f11ba458b7bc405ce3674d93838639af1` | `176d4b89d8ac442c23783cfe4321e1bfb5f87cecd942d2aaeac6448bfe13711d` |
| Inventory | CleanupMonster `Private/Invoke-ADComputerInventoryChildProcess.ps1` | `f215b32ca86b38d9150629c08f6c0c232bfafb27` | `8ba232f42b924b81480f8f8c9da928535cce7603b1d7a344f2742d7960708112` |

Copy each exact source into its own isolated input folder as a `.psm1` file.
Keep build logs and output outside those input folders. Build using the compiler
CLI with `--kind dll --mode Hybrid --resource-mode None` and the desired
framework. Recorded development builds explicitly allowed unreviewed dependency
resolution; they do not establish reviewed locks.

Use PowerShell 7 for `net10.0` and Windows PowerShell 5.1 for `net472`. Run each
case against original and generated module paths in separate child processes:

```powershell
pwsh -NoProfile -NonInteractive -File ./Observe.ps1 `
    -ModulePath <module-path> -Case missing -OutputRoot <new-owned-directory>
```

Use `available` and `missing` for Availability. Use `config-failure`,
`readiness-failure`, `query-failure`, `query-empty`, and `query-one` for Inventory.
Create a fresh output directory for every run; compare stdout, exit code and
authored file contents. An exit runs caller finally, but omits after-call and
after-finally markers. The tested missing-module case exits with code 0;
inventory failure cases exit with code 1. Successful cases continue normally.

The driver replaces module discovery, configuration import and AD commands with
module-owned offline providers. It neither imports ActiveDirectory nor queries
a server. The real inventory body writes initialization/readiness/progress,
CSV, success and error files only beneath the supplied output directory. Run
this driver only in a child process, then remove that exact owned directory.

Each Hybrid artifact emits zero complete methods. Strict rejects Inventory,
but its existing Availability route emits one method with the entire `if`
statement retained in a PowerShell command region. The `available` and `missing`
cases also match that Strict artifact on both hosts. Strict binary-module mode
does not imply SDK-free execution.

This qualifies the listed hosted routes, not general CLR exit lowering or a new
Hybrid coverage gain. Native exit lowering still needs a contract preserving
host exit, caller cleanup and catch behavior. Evidence is recorded in
`m29a-exit-boundaries.json` in the corpus root.
