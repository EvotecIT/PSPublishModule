# PSScriptTools dynamic-parameter form observation

`Prepare.ps1` pins the external PSScriptTools source and extracts the unchanged
`New-PSDynamicParameter` and `New-PSDynamicParameterForm` function extents into
a task-owned module. The source file itself is not redistributed here. The
fixture supplies an in-memory, module-local `Set-Clipboard` command and a read
function; the child observer also installs a global throw-only tripwire and
checks module command resolution before opening the form. This prevents the
probe from changing the desktop clipboard.

Analyze the prepared `Fixture.psd1` as a Hybrid DLL for each claimed target,
review and save `result.dependencyGraph`, and build with the saved graph as
`--dependency-lock`. Use `OperateVisual.ps1` with a new task-owned output folder
for each original or generated module. It starts an owned STA PowerShell child,
sets ParameterName to `CompilerProbe` and Condition to `$True`, captures the
rendered form, invokes Create and Close, and requires one generated code string
to reach the in-memory provider.

This qualifies only the simple Create/Close path under requested WPF software
rendering. It does not test the real clipboard, parameter validation controls,
additional types or parameter sets, editor registration, default hardware
rendering, other desktops, or runtime-free WPF translation.
