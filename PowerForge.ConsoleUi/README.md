# PowerForge.ConsoleUi

Console rendering shared by the PowerForge CLI and the PSPublishModule
PowerShell module. Both reference this project; it targets `net472`, `net8.0`
and `net10.0` so it loads in Windows PowerShell 5.1, PowerShell 7 and the CLI.
Keep reusable pipeline behavior in `PowerForge`; keep only console rendering,
progress UI and human-facing summary formatting here. Console packages stay out
of the core engine.

Static output (Markdown help, managed-module and ModuleState summaries, the
project-build GitHub summary) uses ConsoleMonster. Every ConsoleMonster write goes
through `ConsoleOutput`, which applies `POWERFORGE_CONSOLE_MODE` and is the one
place that decides where output goes. Progress views, the logger, and the module,
project-build and release summaries still use Spectre.Console.

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

ConsoleMonster output follows ConsoleMonster's terminal detection (console font,
glyph tier, `NO_COLOR`, CI, `CONSOLEMONSTER_*` overrides). The Spectre-based views
use their own policy: UTF-8 output and emoji font coverage are separate capabilities,
and progress views, headers, diagnostics, and summaries select decorative icons the
same way.
Windows Server 2016 / Windows 10 build 1607 and earlier use simpler icons and an
ASCII spinner while retaining Unicode rules and borders.

Set `POWERFORGE_CONSOLE_MODE` for the current process when the terminal font needs
a different choice:

- `unicode`: Unicode text and borders with simpler icons and an ASCII spinner.
  ConsoleMonster output uses its Basic glyph tier.
- `ascii`: plain icons, labels, and borders. ConsoleMonster uses its ASCII tier.
- `rich`: decorative icons and a Unicode spinner, including on older Windows.
  ConsoleMonster uses its Full tier.

An unset value selects the platform default. Changing this setting does not install
fonts or change terminal preferences. Progress labels fit terminal display cells
and preserve complete text elements when truncated.
