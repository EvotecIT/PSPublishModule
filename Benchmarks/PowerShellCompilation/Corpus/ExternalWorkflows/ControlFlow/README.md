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

## Formatting and paging transfers

`ObservePaging.ps1` covers two additional unchanged authored functions:

| Workflow | Revision | Source SHA-256 |
| --- | --- | --- |
| PSSharedGoods Format-Stream | `2a807a4f11ba458b7bc405ce3674d93838639af1` | `68161424368aee826c0bcb13a1de2526fdd9592d373dccff0a95c174a59fdc61` |
| PSScriptTools Out-More | `549fa3e3d769532320054fc0b3c8f42df0452361` | `68b45883216818ce2050c134c4fd8551332d67ff7ca8717cb1a43a90ef3bd1cb` |

Format-Stream is the complete unchanged source file. Out-More is the exact
authored byte span from `function Out-More` through its closing comment in
`functions/Utilities.ps1`; no statements are changed. The parent file SHA-256 is
`1625373211d326669795cdd8ff2b40381e57147fd07065ca69bbec50cec81592`.
The existing [PSSharedGoods license](../PSSharedGoods/LICENSE) and
[PSScriptTools license](../PSScriptTools/EventRecord/license.txt) cover the pins.

Copy each pinned function to a separate `.psm1` input file and build as above.
Run original and generated module paths in separate child processes with
`ObservePaging.ps1 -ModulePath <module> -Workflow Format -Case empty -Mode plain`.
Format supports `empty` and `data`, each in `plain` and `loop` modes. Paging
supports `data`, `quit`, `quit-last`, `more`, `all`, `next` and `invalid-response`;
use both modes for `data` and `quit`, and `plain` for the other cases. Run on
PowerShell 7/net10.0 and Windows PowerShell 5.1/net472. The driver checks the
expected transfer and output before producing comparable JSON and exit codes.

Module-owned providers supply in-memory table rows and bounded queued Read-Host
responses, capture prompts and refuse Clear-Host or transpose requests. No user
keyboard, screen state, network or administration operation is involved. The
formatting function reads the child host's window width, but physical rendering
is not qualified. More/All/Next cases preserve the authored ANSI strings as output
records; those records are never rendered as screen-control sequences by the
observer.

Empty Format-Stream input ends a plain caller script or breaks an enclosing
caller loop. Out-More's quit response takes effect when a later input reaches
the bare break, preserving the first page. A quit response on the last input
returns normally because no later process invocation reaches that break. Caller
finally runs in each transfer case; catch and later plain-caller statements are
bypassed. Nonempty formatting and normal paging controls resume the caller.

Twenty-six original/generated case/host comparisons match. All four Hybrid
artifacts retain the function with zero complete compiled methods; all four
Strict explanations reject complete emission. The retained inventory marks these
two entries JustifiedHosted, without adding compiled coverage. Multiline/help
recursion, ClearScreen, transpose, physical display, arbitrary caller stacks,
non-Windows execution and reviewed dependency locks remain unqualified. Evidence
is in `m29a-paging-transfer-boundaries.json` in the corpus root.
