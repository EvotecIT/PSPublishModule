# Console output in PowerForge and PSPublishModule

PowerForge renders console output with [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster): live
progress views, summaries, tables and Markdown help. This page describes how output is routed, how long-running
work stays visible, and the few places that write to the console directly on purpose. Read it before adding output
to a cmdlet, a `powerforge` command or an engine service.

## Where output goes

| Code | Write through | Why |
| --- | --- | --- |
| Engine services (`PowerForge`, `PowerForge.PowerShell`) | `ILogger` (`Info`, `Success`, `Warn`, `Error`, `Verbose`) | The engine doesn't know its host. The CLI, cmdlets and Studio each supply a logger. |
| Widgets and markup (tables, summaries, Markdown) | `PowerForge.ConsoleUi.ConsoleOutput` | One route that applies the console mode and the current host. |
| Cmdlets that render | `using (CmdletConsole.Use(this)) { ... }` | Sends `ConsoleOutput` through the PowerShell host, so transcripts, the ISE, jobs and remoting receive it. |
| Cmdlet logging | `CmdletLogger`, wrapped in `CmdletWarningLogger` where warnings must honour `-WarningAction` | See below. |
| Step progress | An `IModulePipelineProgressReporter` (or the publish/release reporters) | Drives the live view, or plain step lines when there is no live view. |

How `CmdletLogger` maps the engine's levels:

- `Info` and `Success` are written the way `Write-Host` writes them (an information record tagged `PSHOST`), so
  progress lines and heartbeats are visible without `-Verbose` and land in transcripts. Capture or discard them
  with the information stream: `6>&1`, `6>$null`.
- `Warn` goes to the warning stream and `Error` to the error stream.
- `Verbose` stays verbose.
- Both loggers accept calls from any thread. A synchronous `PSCmdlet` may only write on its pipeline thread, so calls
  from worker threads go to the console route instead. `AsyncPSCmdlet` marshals stream writes, and
  `RunOnPipelineThread` runs host output (widgets) on the pipeline thread in order with the cmdlet's other output.

## Keep long work visible

Nothing should look hung. Every operation that can take more than a few seconds shows that it is alive:

- **Steps.** With a live view, `ProgressView` shows each step with a spinner, the elapsed time and the latest
  detail. Without one (CI, redirected output, `-Verbose`), `LoggingModulePipelineProgressReporter` logs
  `[n/N] Step` when a step starts, `done in 12.3s` when it ends, and keeps saying a long step is still running.
- **External processes, downloads and uploads.** Wrap them in `OperationHeartbeat`:

  ```csharp
  using var heartbeat = OperationHeartbeat.Start(logger, $"dotnet publish {project} ({framework})", detail);
  ```

  It logs `<label> is still running (1.5m)` every 15 seconds and can feed the same text to a step detail. Say what
  the wait is for when it may need the user, for example signing that waits for a token PIN.
- **Child process output.** Use the runners' line callbacks (`ProcessRunRequest` / `PowerShellRunRequest`
  `outputLineReceived` / `errorLineReceived`) instead of reading the output only at the end. Pass the lines to
  `ProgressDetailThrottle.ForStep(...)` for the live detail; the throttle limits the rate and always delivers the
  last line before the process goes quiet. Don't read stdout to the end and then stderr: read both at once, or a full
  stderr pipe blocks the child.
- **Loops.** Report `n/N` and the item (`Pushing PowerForge.1.2.0.nupkg (3/7)`), or a periodic count when there can
  be thousands of items.
- **Retries and waits.** Say why you retry, which attempt is next, and how long a wait lasts (a GitHub rate-limit
  wait shows the time left).

## Warnings and errors

- Warnings a child process prints on a successful run are reported as warnings, not dropped with the output
  (`ChildProcessOutput.WarnAboutDiagnostics`, `RepositoryDiagnosticService.RelayChildLine` for repository tools).
- A failure message keeps the part that explains it: the error lines and the end of the output, where dotnet and
  MSBuild print the actual error. `ChildProcessOutput.Tail` bounds it.
- Publishing failures name the cause: invalid or expired API key, 401/403, and "version already exists" (409).
- A cmdlet that returns a result object with a failure also writes an error record, so `-ErrorAction` and
  `$Error` see it.
- When a step falls back to another tool (PSResourceGet to PowerShellGet, Save-PSResource to Save-Module), log a
  warning with the reason.

## Console mode and colour

- `POWERFORGE_CONSOLE_MODE` = `ascii`, `unicode` or `rich` picks ConsoleMonster's glyph tier. Unset keeps
  ConsoleMonster's own detection, including its `CONSOLEMONSTER_*` overrides.
- `NO_COLOR` turns colour off. `powerforge --no-color` sets it for the process.
- JSON output (`--output json`) writes only the JSON envelope to stdout; human output and logs are suppressed or
  buffered into the envelope.

## Direct console writes that are intended

These write to `Console` on purpose. Keep new ones to these kinds:

- Machine-readable output: JSON envelopes (`CliJsonWriter`), `--version`, tab-separated or path lists meant for
  pipes.
- `powerforge` usage and help text.
- Child-process protocols read by a parent: `ModulePipelineProgressProtocol` lines, `PFSIGN::` signing progress,
  and the Windows PowerShell documentation worker (`DocumentationRenderProcess`), whose output is already encoded
  for the parent's console.
- Passthrough of a program's own stdout (`powerforge powershell project run`, simulator command output).
- The console host generated into compiled PowerShell executables.
- `BuildServices` keeps `ConsoleLogger` for scripts that call it directly; nothing in the build or publish flows
  uses it.

## Known gaps

- The child build host forwards its stderr after it exits rather than live, because the release service prints
  the same output afterwards. Streaming it needs both sides changed together.
- PSScriptAnalyzer validation runs as one call: it has a heartbeat but no per-file progress.
- The missing-function analysis says which modules it will import, not each one as it imports it.
- ConsoleMonster.Markdown keeps its guard for indented front matter until an OfficeIMO.Markdown release with the
  parser fix is published. Moving to ConsoleMonster 2.4.2 also moves OfficeIMO.Markdown to 3.6, which has to be
  checked with HtmlForgeX.Markdown, so it is a separate update.
- Several cmdlets set `[Console]::OutputEncoding` to UTF-8 and leave it set. ConsoleMonster caches what it detects
  about the console once per process, so restoring the encoding after each cmdlet needs a separate change and live
  testing.
