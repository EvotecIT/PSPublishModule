# PSScriptTools WPF dialogs: qualification in progress

This fixture does **not** qualify normal GUI execution. The tracked evidence covers reviewed-lock builds and failure paths. The real-dialog driver is prepared but its callback results and visual state have not been observed.

`Prepare.ps1` pins the external New-WPFMessageBox file and Utilities file by SHA-256, extracts the unchanged Invoke-InputBox AST extent, and preserves the original functions/icons layout through a conventional root module and contained dot-source paths. Supply the pinned PSScriptTools root and a new task-owned input directory. The information icon remains an external validation input. CompleteModule resource mode includes it inside the artifact.

Run `ObserveFailures.ps1` in fresh noninteractive `-STA` child processes for each original/generated module on both Windows hosts. Seven cases assert platform refusal, module-local injected assembly refusal, actual WPF invalid-brush conversion and input-title validation. The platform and assembly shadows are explicit boundary probes. These cases never call ShowDialog and prove no modal callback behavior.

`Observe.ps1` runs an actual message or plain input dialog in STA. Use a unique title (at most 25 characters for input), click its OK button, and enter the synthetic text `compiler-value` for the input case. It requires one result of the correct type/value. A complete qualification must inspect the real window and rendered icon/text, exercise these callbacks on original/generated artifacts, record output/exit evidence on both hosts, and close every owned dialog/process. Cancellation, secure input, custom buttons, additional WPF functions, other platform/host states and full-module execution need separate proof.

During this attempt, Windows reported a responding owned dialog and MainWindowHandle, but supported Computer Use list_windows/list_apps returned no corresponding window or app. Both attempted children were stopped by their verified process IDs. No screenshot, click or callback result is claimed. The six retained WPF functions remain unresolved; advertised regions and owned helper methods are not complete workload execution credit.
