# PSScriptTools Trace window observation

`Prepare.ps1` pins the external PSScriptTools `functions/Trace.ps1` source and
copies it unchanged into a task-owned module. The fixture supplies fixed
module-local `Get-Date` and `Get-CimInstance` results and captures the authored
`New-RunspaceCleanupJob` call. Global throw-only tripwires and pre-UI command
lookup checks guard those provider boundaries. The authored WPF window, separate
STA runspace, synchronized hash, message append and Quit callback still run.

Analyze the prepared `Fixture.psd1` as a Hybrid DLL for each claimed target,
review and save `result.dependencyGraph`, then build with the saved graph as
`--dependency-lock`. Use `OperateVisual.ps1` with a new task-owned output folder
for each original or generated module. It starts one owned PowerShell child,
waits for the fixed metadata and `compiler-note` in the real window, captures
the rendered window, invokes Quit and waits for the child. The child explicitly
ends and disposes the captured PowerShell invocation and runspace. Only the
exact child PID/start time is stopped on failure.

The unchanged source produces one nonterminating `PropertyNotFound` record
after Quit: it removes `traceSynchHash` in the window callback, then tries to
assign `$traceSynchHash.Error` after `ShowDialog` returns. The observer requires
that exact error ID and authored line instead of hiding it. This qualifies only
the fixed-metadata Init → one message → Quit path. The Save dialog, real CIM
query, natural clock, extra messages, failure/timeout paths, default rendering,
other desktops and runtime-free WPF translation remain unqualified. Screenshots
contain local account/computer labels; the tracked record keeps only hashes.
