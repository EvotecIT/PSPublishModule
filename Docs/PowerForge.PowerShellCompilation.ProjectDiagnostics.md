# Understand project compilation blockers

`project explain` shows the selected compiler route for each complete function or script unit, the causes attached to that decision, and links to bound local calls. `project diagnose` adds verification of the existing build receipt, locks, reproduction evidence, and artifact files.

These commands are implemented on `feature/powershell-compiler`. Use the locally built CLI from the [development guide](PowerForge.PowerShellCompilation.Development.md); this guide does not imply a public package release.

```powershell
dotnet $powerforge powershell project explain ./powerforge.psproject.json
dotnet $powerforge powershell project diagnose ./powerforge.psproject.json
```

Use `--target <name>` to inspect selected targets. Neither command runs the authored program, imports its module into a runspace, builds an artifact, or refreshes reviewed locks. Explain writes the existing detailed decision trace under `.powerforge/explain/<target>.json` and returns grouped diagnostics directly. Diagnose reports current source decisions even when no successful build receipt exists, but still returns failure when artifact verification fails.

## Read a function group

Each group shows the source path and declaration line, function name, selected decision and lowering route, retained source, and any promoted typed regions. Causes retain their compiler feature IDs and available line/column coordinates. A local-call link identifies the callee by its portable unit ID and source location; follow that callee's group for its own causes.

For example, save this as `Functions.psm1`:

```powershell
function Get-Leaf {
    param([string] $Command)
    & $Command
}
function Get-Middle {
    param([string] $Command)
    Get-Leaf $Command
}
```

Initialize a Strict binary-module target and inspect it:

```powershell
dotnet $powerforge powershell project init ./Functions.psm1 --name Example --kind dll --mode Strict --framework net10.0
dotnet $powerforge powershell project explain ./powerforge.psproject.json
```

The report identifies a binary-cmdlet shaping restriction for these basic functions and links the call in `Get-Middle` at **7:5** to `Get-Leaf` at line **1**. Explain returns a nonzero exit code because final shaping rejected the target, even though earlier semantic analysis accepted it. A Hybrid binary-module target reports the retained runtime route instead. A Strict CLR library has a different contract and reports dynamic command discovery as a semantic blocker.

Local-call links come from the existing bound call graph. They do not guess relationships for unresolved names or dynamic commands, and a link by itself does not mean the callee caused rejection. Use the caller's feature diagnostics and the callee's decision together. Recursive graphs keep direct edges rather than expanding indefinitely. For synthetic top-level calls without an exact authored mapping, line and column are zero and text says `call location unavailable`; the containing script's declaration location remains available.

## Keep failure stages separate

| Stage | Meaning |
| --- | --- |
| Input | Project/source input or the explanation evidence file could not be read, validated, or written. |
| Target | The manifest's artifact or semantic target contract is invalid or unsupported. |
| Dependency | A reviewed provider or required dependency cannot be resolved or validated. |
| Semantic | Parsing, binding, analysis, or lowering supplied a source blocker. |
| Shaping | Final artifact rules supplied an additional cause, or Strict stopped before shaping because other blockers remain. |
| Integrity | Diagnose could not authenticate the existing receipt, restore environment, locks, or artifact files. |

A missing assembly can block the project while individual functions remain semantically eligible. Likewise, an eligible function may have no artifact when Strict compilation stops because another unit was rejected. The report preserves both observations. It does not turn a dependency problem into a language-coverage failure.

Stage labels describe the owner that supplied the issue. Coordinates are zero when that owner has no precise authored location, such as a missing contained assembly. The report does not infer locations or stage names from arbitrary message text.

When an acquired project environment exists, both commands validate it and use its package root for source analysis, including self-contained runtime packs. They do not require those packages to be duplicated in the global NuGet cache. Invalid environment evidence produces a dependency issue while source inspection is still attempted; diagnose also retains the separate artifact-integrity failure. Without acquired evidence, explain uses the normal analysis package-resolution path and reports an unavailable runtime pack as a dependency failure.

## Consume JSON

```powershell
$result = dotnet $powerforge powershell project diagnose ./powerforge.psproject.json --output json | ConvertFrom-Json
$commandExitCode = $LASTEXITCODE
$target = $result.result.targets[0]
$report = $target.diagnosticReport
$report.issues | Format-Table stage, code, relativePath, line, message
$report.units | ForEach-Object {
    $_.unit | Select-Object name, decision, loweringRoute, retainedHostedSource
    $_.issues | Format-Table stage, code, line, column, message
    $_.localCalls | Format-Table calleeName, calleeRelativePath, calleeStartLine, line, column
}
```

`diagnosticReport.canProceed` describes source analysis and shaping. The outer `success` and target `succeeded` fields describe the requested operation. Diagnose may therefore return `canProceed: true` and `succeeded: false` when source is valid but a build receipt is missing or stale. Treat the operation's exit code as authoritative for success.

`finalShapeAvailable: false` means the report has only partial analysis evidence or failed during project validation; do not present its unit routes as final artifact decisions. The grouped report has its own schema version. Existing detailed explanation and authenticated artifact evidence retain their contracts. Diagnostic creation does not change eligibility or semantic fingerprints.

After intentional source, target, provider, or resource changes, follow lock → restore → build → test before distribution. Explain does not bless those changes as reviewed, and diagnose never substitutes fresh analysis for a failed artifact-integrity check. Debugger stepping, exact mappings across all Hybrid boundaries, and additional host qualification remain separate M28 work.
