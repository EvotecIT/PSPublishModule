# PSScriptTools expression form observation

`Prepare.ps1` pins the external PSScriptTools `Test-Expression.ps1` and
`form.xaml`, extracts the unchanged `Test-ExpressionForm` function extent, and
copies the unchanged XAML into a task-owned module. The full external source is
not redistributed here. The module supplies an in-memory `Test-Expression`
provider that records the requested arguments and returns a fixed result. It
accepts only the inert `40+2` script block and never invokes it. Module-local
`Test-IsPSWindows` selects the Windows path. The isolated child installs
throw-only global tripwires and verifies module command resolution before
opening the form.

Analyze the prepared `Fixture.psd1` as a Hybrid DLL for each claimed target,
review and save `result.dependencyGraph`, then build with that saved graph as
`--dependency-lock` and `--resource-mode CompleteModule`. Use
`OperateVisual.ps1` with a new task-owned output folder for each original or
generated module. It starts one owned STA PowerShell child, enters `40+2`,
captures the rendered form, invokes Run, verifies the in-memory provider result
is displayed, captures the rendered result, then invokes Quit. Only the exact
child PID/start time is stopped on failure. Content controls exclude variable
operating-system titlebar nodes.

This qualifies the simple static-interval Run/Quit path under requested WPF
software rendering. It does not test the real `Test-Expression` evaluator,
arbitrary submitted code, random intervals, validation failures, alternate
form paths, default hardware rendering, or runtime-free WPF translation.
