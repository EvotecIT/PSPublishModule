# Replacing Spectre.Console with ConsoleMonster

Status: assessment, no code changed yet. Date: 2026-11-10.
Compared: PSPublishModule `main` at `e67e1bfea`, ConsoleMonster 2.4.0 (`a051278c`).

## Summary

ConsoleMonster covers nearly everything PSPublishModule and PowerForge use from
Spectre.Console. Moving to it removes roughly 1,300 lines that exist only to make
Spectre's progress view behave, takes Spectre markup out of the engine, and fixes
how cmdlet output reaches the PowerShell host. Two ConsoleMonster features must
land first: a public route for writing through the PowerShell host, and a
supported way to write host output (warnings, verbose, prompts) while a live view
is running.

## 1. Current Spectre usage

| Area | Size | What it does |
|---|---|---|
| `PowerForge.ConsoleShared\` | ~4,600 lines, 17 files | Live progress, step ledger, status rows, logger, summaries. Source-linked into both `PowerForge.Cli` and `PSPublishModule`. |
| `PSPublishModule\Cmdlets\` | 12 files | Starts the live views; `InvokeProjectBuildCommand.Helpers` draws its own rule and table. |
| `PSPublishModule\Services\` | ~9 files | `ManagedModuleSummaryWriter`, `ModuleStateConsoleRenderer`, Markdown help `Renderer`, PowerShell/JSON highlighters, `CmdletWarningLogger`. |
| `PowerForge.Cli\` | 5 files | Interactive gate, logger, run-with-status helper. |
| `PowerForge.Tests\` | 9 files, ~2,000 lines | Assert against Spectre rendering internals. |
| `PowerForge\` (engine) | 3 files | `FormattingSummary.FormatPartMarkup` returns Spectre markup; public enum value `JsonRendererPreference.Spectre`; `ConsoleEncoding` documented as Spectre support. |

Packages: `Spectre.Console` 0.57.2 (module and CLI), `Spectre.Console.Json` 0.57.2 (module only).

## 2. Workarounds that exist because of Spectre

| Workaround | Location | Reason |
|---|---|---|
| Render-hook interception, captured final frame re-printed after the display ends, private 100 ms refresh timer | `SpectreProgressDisplay.cs` | Spectre's progress clears its last frame and has no retained ledger. |
| Raw cursor movement (`\r`, `CursorUp`, `\r\n` padding) to suspend the live area around host writes | `SpectreProgressDisplay.DisplaySession.WriteExternal`, `CmdletWarningLogger` | `WriteWarning` and prompts bypass Spectre and overwrite or get erased by the live area. |
| Viewport that slices rendered lines, assuming one line per task, with a "furthest focus" index | `SpectreProgressViewport.cs` | Progress cannot scroll; long step lists overflow the terminal. |
| Six `ConcurrentDictionary<ProgressTask, …>` side tables (icon, start, completion, state, detail, signing) and a custom fixed elapsed column | `SpectreProgressPresentation.cs`, `SpectreBuildProgressColumns.cs` | `ProgressTask` carries no state; the stock elapsed column keeps counting after completion. |
| Manual width budgeting, bar sizing, text-element-aware truncation | `SpectreProgressPresentation.cs` | No responsive layout for progress rows. |
| Truncated "Messages (n; complete log follows)" area below progress | `SpectreProgressDiagnostics.cs` | No log region. |
| Own glyph policy: `ConsoleEncoding`, `POWERFORGE_CONSOLE_MODE`, emoji-or-two-letter switch repeated in each UI file | `PowerForge\Diagnostics\ConsoleEncoding.cs`, every `Spectre*` file | Spectre's Unicode detection is unreliable on older Windows and says nothing about font coverage. |
| Global `AnsiConsole.Profile.Capabilities.Unicode = true` | `PowerForge.Cli\Program.cs`, `InvokeModuleBuildCommand`, `InvokeProjectBuildCommand`, `InvokeDotNetRepositoryReleaseCommand`, `SpectreDotNetPublishConsoleUi.Display` | Forcing Unicode by mutating process-wide static state. |
| Source linking instead of a project reference | `PowerForge.ConsoleShared\README.md` | README cites differing Spectre versions; both now use 0.57.2, so the reason is stale. |

Related problem: cmdlets render through the static `AnsiConsole`, which writes
straight to the process console. `Start-Transcript` does not record that output,
ISE displays it poorly, and remoting or job hosts lose it.

## 3. ConsoleMonster mapping

ConsoleMonster targets netstandard2.0, net472, net8.0 and net10.0 with no
third-party dependencies, so it fits Windows PowerShell 5.1 and the CLI. Its markup
dialect matches Spectre's, including colour names, so most markup strings move
unchanged.

| Today | ConsoleMonster | Effect |
|---|---|---|
| `AnsiConsole.Progress` + display, viewport, diagnostics, columns, presentation | `LiveSession` (inline mode) with `LiveTable` (`MaxRows`), `Phases.StepCounter`, `Lanes`, `LogRegion` | Delete the workaround files; the ledger becomes a thin adapter from engine progress events. |
| `SpectreStatusConsoleUi` | `Terminal.Status` / `StatusLine` | Built-in; prints once per change when redirected. |
| Diagnostics during progress | `LogRegion`; captured `Console.Out` printed above an inline live area | Messages stay visible and whole instead of truncated. |
| `ConsoleEncoding` glyph policy | Capability detection: glyph tiers (Full, Basic, ASCII) from console font and WGL4 check, `NO_COLOR`, CI detection, `CONSOLEMONSTER_*` overrides; UTF-16 via `WriteConsoleW` on Windows | Remove code-page juggling; keep `POWERFORGE_CONSOLE_MODE` as an alias. |
| `Table`, `Rule`, `Panel`, `Tree`, `Grid` | Same names; `Table.For(rows)` with priority columns and narrow-terminal card fallback | Direct port. |
| `Spectre.Console.Json` `JsonText` | `CodeView` with `JsonHighlighter` | Highlighted text rather than a structural tree; acceptable for help output. |
| Own PowerShell/JSON highlighters | `CodeView` with built-in PowerShell and JSON lexers | Delete `Rendering\Highlighters\`. |
| Markdown help `Renderer` | `ConsoleMonster.Markdown` (built on OfficeIMO.Markdown 3.5.2, already referenced by the module) | Replace the hand-rolled renderer. |
| `SpectreConsoleLogger` | `ConsoleMonster.Extensions.Logging` or a small adapter over `PowerForge.ILogger` | Logs route into the live log region automatically. |
| Hard-coded colours (`deepskyblue1`, `gold3`, …) | Theme tokens (`Pass`, `Fail`, `Warn`, `Info`, `Skip`, `Running`, `Pending`, `Accent`, …) and `StatusKind` | Consistent colours, high-contrast and colour-blind themes for free. |
| Spectre-coupled tests | `TestTerminal.RenderText` + `GoldenFile.Match` | Stable golden files at fixed width and glyph tier. |

## 4. New experience worth building

- **Build view.** Phase strip, step list, parallel lanes for compile, sign and
  package, and a log region. ConsoleMonster's `gallery/pipeline` demo already
  models the 16 module-build steps.
- **Final summary.** A `DashboardFrame` with meters and a "Needs attention" list,
  fed by an `AttentionTally` that counts diagnostics during the run. This replaces
  the 1,168-line `SpectrePipelineSummaryWriter` and the release summary with one
  consistent layout.
- **Failures.** `ExceptionView` (source context, collapsed framework frames)
  instead of the failure-headline parsing in the summary writer.
- **`-WhatIf` for publish and release.** `ChangePreview` / `ObjectDiff` to show
  what would change before anything is published.
- **Terminal tab and taskbar progress.** OSC 9;4 is driven automatically and
  disabled in CI and redirected output.
- **CI artifact.** Export the final summary to HTML or SVG and attach it to the run.
- **Managed-module cmdlets.** The `ConsoleMonster.PowerShell.Display` broker lets
  `Get-ManagedModule` and similar cmdlets keep emitting real objects through
  `WriteObject` while rendering rich tables only when output reaches `Out-Default`
  on a local console.

## 5. ConsoleMonster changes needed

### Blockers

1. **Public PowerShell host output.** Routing to the console, `Host.UI`,
   transcripts or remoting exists in `ConsoleMonster.PowerShell\HostOutput.cs`, but
   the class is `internal` and lives in the module DLL, which is not a NuGet
   package. Compiled cmdlets in other modules need a public API in a NuGet
   package (for example `ConsoleMonster.PowerShell.Display`) that writes a widget
   through the calling cmdlet's host. Without it, `Terminal.Write` from a cmdlet
   repeats today's transcript and ISE problem.
2. **Host writes during a live session.** Cmdlets call `WriteWarning`,
   `WriteVerbose` and prompt while progress is shown. ConsoleMonster `TODO.md` L5
   records that host writes draw over the live frame. A supported
   suspend-write-resume call on `LiveSession` (for example `WriteAbove(Action)`)
   replaces our `WriteExternal` cursor manipulation.

### Should fix

- **One assembly version per Windows PowerShell 5.1 session.** PSPublishModule,
  TestimoX and the ConsoleMonster module will all ship the DLL. A version policy is
  needed before more than one product adopts it.
- **Unbounded buffers** (`TODO.md` L6): the live queue, capture writer and log
  entries grow without limit. Long release runs make this matter.
- **VT mode left enabled** (`TODO.md` L2): detection turns on VT processing and
  never restores the previous console mode.
- **Behaviour to confirm for the step ledger:** a scrolling `LiveTable` should
  follow the furthest running row, not only the last one, and finished rows should
  keep a frozen elapsed time.
- **Smaller items:** a JSON tree widget; correct the Spectre migration guide, which
  says `:shortcode:` emoji are unsupported although `Markup.Shortcodes` exists.

## Progress that looks stuck

Verified on `main` (`e67e1bfea`). A module build can show finished rows and no
running row for minutes while work continues.

- **Child rows land at the end.** Rows planned late, such as the 149 NuGet
  package-signing rows that arrive through `ItemsPlanned` and `ledger.Plan`, are
  added by `SpectreProgressLedger.EnsureVisibleTask` (`_context.AddTask`) at the
  end of the list instead of under their parent step, so they appear after the
  module steps.
- **The viewport follows the furthest row only.**
  `SpectreProgressViewportState.AdvanceFocus` never moves focus back. After the last
  package row, the viewport stays on finished rows while "Extract help", which has
  a lower index, runs off-screen. `RenderActiveStatus` reads the full list, so
  "Current:" names a row the user cannot see.
- **Work runs outside any step.** The type-accelerator report, dependency
  installation and version synchronisation with its checkpoint ran between steps,
  as does the manifest refresh after formatting. Nothing on screen is running.
- **Child processes are silent.** Help extraction, module import validation and
  dotnet restore/build/publish stream output that the runner already captures, but
  none of it reached the step, so a long run looks frozen.

### Engine changes made now

These survive the move to ConsoleMonster and change no JSON or quiet output.

- Dependency preparation (`deps:install`, new `Dependencies` step kind), module
  version synchronisation with its checkpoint (`version:synchronize`) and the
  type-accelerator report (`build:type-accelerators`) are planned steps.
- Help extraction, module import validation and dotnet publish processes send their
  latest output line to the step detail, at most every 250 ms.
- The compatibility check reports per-file progress, Markdown help and external
  help report per-command progress, and the file-consistency checks name the
  operation they are on.
- The MSBuild batch heartbeat in project releases reaches the phase detail as well
  as the log.

Not done yet: the manifest refresh after formatting still runs between steps, and
artefact zipping has no per-file progress (the zip writer is several layers below
the artefact step).

### Requirements for the live-progress port

- Running rows are always visible.
- Child rows are inserted under their parent and collapse into it ("149/149
  signed") once the group finishes; the final printed ledger keeps the full list.
- Between steps, show an indicator instead of nothing.
- Under "Current:", show "last activity Ns ago" and the latest child-process line.

### Follow-up

Whether NuGet package signing should run in parallel is a separate question and is
not part of this change.

## 6. Migration plan

1. **ConsoleMonster.** Ship the two blockers plus the buffer and VT-mode fixes.
2. **PSPublishModule, one PR.**
   - Replace source linking with a `PowerForge.ConsoleUi` project referenced by
     `PowerForge.Cli` and `PSPublishModule`.
   - Port the logger, status rows, tables and Markdown help.
   - Port live progress and delete `SpectreProgressDisplay`,
     `SpectreProgressViewport`, `SpectreProgressDiagnostics`,
     `SpectreBuildProgressColumns` and `SpectreProgressPresentation`; reduce
     `SpectreProgressLedger` to an adapter.
   - Rebuild the module, project and release summaries as one `DashboardFrame`
     layout.
   - Replace Spectre-coupled tests with golden files.
   - Remove the Spectre packages and the global `Capabilities.Unicode` writes.
   - Move markup out of the engine: drop `FormattingSummary.FormatPartMarkup`
     from `PowerForge`, and keep `JsonRendererPreference.Spectre` as an obsolete
     alias so the public enum does not break.
   - Keep `POWERFORGE_CONSOLE_MODE` working by mapping it to the ConsoleMonster
     glyph setting.
3. **Follow-ups.** `-WhatIf` previews, the Display broker for managed-module
   cmdlets, and HTML/SVG summary export.

## Decisions

- PSPublishModule and PowerForge consume ConsoleMonster only as NuGet packages.
  There is no `RequiredModules` dependency on the ConsoleMonster PowerShell
  module. The public host-output route (blocker 1) therefore has to ship in a
  NuGet package, and the Windows PowerShell 5.1 version policy matters more
  because the DLL is bundled.
- The CLI uses the core package only; the PowerShell host route is for the
  module.
