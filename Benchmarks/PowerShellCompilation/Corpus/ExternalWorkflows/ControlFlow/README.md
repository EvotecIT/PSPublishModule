# Caller-stack loop transfers

This driver records control flow from two unchanged external functions:

| Workflow | Pinned source | Revision | SHA-256 |
| --- | --- | --- | --- |
| Accounts | CleanupMonster `Private/Get-ADServiceAccountsToProcess.ps1` | `f215b32ca86b38d9150629c08f6c0c232bfafb27` | `ed2af8204bb5bb48b55dfdbd0e04be46bf7444081dd0d87c2625dfcf98f0cd9b` |
| Drive | PSScriptTools `functions/New-PSDriveHere.ps1` | `549fa3e3d769532320054fc0b3c8f42df0452361` | `c64257986c8ff469fcb279eaee322767e2fd71bc1eaf7c1b7a5bda7f4b714212` |

Copy each exact source file to its own isolated input folder with a `.psm1`
extension. Keep build outputs and logs outside those input folders. Build each
with the compiler CLI using `--kind dll --mode Hybrid --resource-mode None`
and the desired framework. The recorded development builds explicitly used
`--allow-unreviewed-dependencies`; they do not establish reviewed dependency locks.

Run original and generated module paths in separate noninteractive child
PowerShell processes, using `Observe.ps1 -ModulePath <module> -Workflow Accounts`
with `-Mode plain`, `loop`, and `none`. For `-Workflow Drive`, use `plain` and
`loop`. Use PowerShell 7 for `net10.0` and Windows PowerShell 5.1 for `net472`.
Compare JSON records and exit codes, including the absence of continuation
records when authored control flow leaves the caller. Do not run these probes
in an interactive user session: the authored transfers can terminate the script.

Accounts are in-memory objects. Module-owned clock/color providers prevent real
service-account discovery. The drive workflow receives an unusual in-memory
folder name; drive creation and location changes are refusing providers. Its
validation only reads whether the driver directory exists.

An exclusion triggers authored `continue 2`, which searches for a label rather
than counting loop levels. It ends the caller script in both recorded contexts.
The drive function's unscoped `break` ends a plain caller script or breaks its
enclosing loop. Both execute caller `finally` blocks. Neither behavior is
equivalent to a function-local C# return.

The generated Hybrid artifacts preserve these effects by retaining the functions
in PowerShell. Each emits zero complete methods. These observations qualify the
hosted boundary, add no compiled-workflow coverage, and do not prove AD effects,
drive creation, interactive paging, or arbitrary caller stacks. Full evidence
is in `m29a-external-transfer-boundaries.json` in the corpus root.
