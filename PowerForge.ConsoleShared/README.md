# PowerForge.ConsoleShared

This folder contains Spectre.Console rendering helpers that are shared by the
PowerForge CLI and the PSPublishModule PowerShell surface.

The files are source-linked into both consumers instead of built as a separate
assembly because the two surfaces currently carry different Spectre.Console
package versions and different packaging/runtime constraints. Keep reusable
pipeline behavior in `PowerForge`; keep only console rendering, progress UI,
and human-facing summary formatting here.

If the CLI and PowerShell module later converge on the same console dependency
and packaging model, this folder can become a small project reference. Until
then, source linking is intentional and keeps console dependencies out of the
core engine.

Module builds, project/package builds, executable publishing, and unified releases
use the same progress display. The current operation appears in a wrapping line
below the rows. Diagnostics appear below that line while work runs, and the
complete messages follow the finished progress table with blank-line separators.
Operations without detailed progress events use one running row with elapsed
time through `SpectreStatusConsoleUi`.

Interactive callers pass their Spectre logger directly to the engine. Buffering
that logger prevents the live message area from receiving output and hides
messages on successful runs. JSON and quiet modes keep their separate output
contracts. PowerShell warnings are synchronous host writes so warning preferences
and prompts still work; the display clears its live area and resumes below the
complete warning.

Signing rows show certificate-provider guidance before a potentially blocking
operation. A running timer indicates elapsed time; it does not confirm that a
PIN dialog is open. NuGet signing reports each package and retry attempt before
calling the signer, leaving packages that have not started queued.

## Console font compatibility

UTF-8 output and emoji font coverage are separate capabilities. Progress views,
headers, diagnostics, and summaries select decorative icons using the same policy.
Windows Server 2016 / Windows 10 build 1607 and earlier use simpler icons and an
ASCII spinner while retaining Unicode rules and borders.

Set `POWERFORGE_CONSOLE_MODE` for the current process when the terminal font needs
a different choice:

- `unicode`: Unicode text and borders with simpler icons and an ASCII spinner.
- `ascii`: plain icons, labels, and borders.
- `rich`: decorative icons and a Unicode spinner, including on older Windows.

An unset value selects the platform default. Changing this setting does not install
fonts or change terminal preferences. Progress labels fit terminal display cells
and preserve complete text elements when truncated.
