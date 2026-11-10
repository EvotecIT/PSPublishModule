# PowerForge.ConsoleUi

Console rendering shared by the PowerForge CLI and the PSPublishModule
PowerShell module. Both reference this project; it targets `net472`, `net8.0`
and `net10.0` so it loads in Windows PowerShell 5.1, PowerShell 7 and the CLI.
Keep reusable pipeline behavior in `PowerForge`; keep only console rendering,
progress UI and human-facing summary formatting here. Console packages stay out
of the core engine. All rendering uses ConsoleMonster.

## Where output goes

Every write goes through `ConsoleOutput`. The CLI writes to the process console.
The module's cmdlets install their PowerShell host with
`CmdletConsole.Use(this)`, so output goes through
`ConsoleMonster.PowerShell.Display.HostOutput` and reaches `Start-Transcript`, the
ISE, jobs and remoting. Writes made off the pipeline thread go straight to the
console. PowerShell warnings and prompts run through `ConsoleOutput.WriteOutside`,
which clears a live area around the host write and redraws it below.

## Progress

Module builds, project/package builds, executable publishing, unified releases
and single operations share `ProgressView`: an inline ConsoleMonster live session
with a numbered step ledger (`Phases` with `StepCounter`), a "Current:" line and
a message log. `ProgressLedger` maps engine progress events onto it.

- Steps keep their planned order. Work planned later, such as NuGet signing
  rows, is listed under the step that runs it; a finished step folds its rows
  into "done/total" while the live area is short. Running rows stay visible.
- "Current:" names the furthest running work with its latest detail (process
  output, the file being checked) and "last activity Ns ago" once it goes quiet.
  While signing it repeats the certificate-provider guidance: a running timer
  shows elapsed time, it does not confirm that a PIN dialog is open.
- Between steps the view shows "Preparing next step" with the time since the
  last step finished.
- Messages logged during the run appear in the log area and are printed in full
  after the final ledger, also when the run fails.

The live view is used only on an interactive console without a transcript.
Verbose, JSON, quiet, redirected and CI runs keep their line-based output.

## Summaries

The module build, project build and unified release summaries use one
`SummaryFrame` layout (a ConsoleMonster `DashboardFrame`): the outcome on the
title line, headline meters, a "Needs attention" list counted by an
`AttentionTally`, an overview and detail sections. A failed module build shows
the exception through `ExceptionView`.

## Console font compatibility

ConsoleMonster detects the terminal (console font, glyph tier, `NO_COLOR`, CI,
`CONSOLEMONSTER_*` overrides). Set `POWERFORGE_CONSOLE_MODE` for the current
process when the terminal font needs a different choice:

- `ascii`: plain characters; ConsoleMonster's ASCII glyph tier.
- `unicode`: Unicode text and borders with simpler symbols; the Basic tier.
- `rich`: the Full tier, including on older Windows.

An unset value keeps ConsoleMonster's detection. Changing this setting does not
install fonts or change terminal preferences.
