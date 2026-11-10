# PowerForge.ConsoleUi

Console rendering shared by the PowerForge CLI and the PSPublishModule
PowerShell module. Both reference this project; it targets `net472`, `net8.0`
and `net10.0` so it loads in Windows PowerShell 5.1, PowerShell 7 and the CLI.
Keep reusable pipeline behavior in `PowerForge`; keep only console rendering,
progress UI and human-facing summary formatting here. Console packages stay out
of the core engine.

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
