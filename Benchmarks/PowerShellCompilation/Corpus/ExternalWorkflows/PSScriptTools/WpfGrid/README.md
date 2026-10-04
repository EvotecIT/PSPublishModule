# PSScriptTools WPF grid observation

`Prepare.ps1` pins the external PSScriptTools revision and copies the unchanged
`ConvertTo-WPFGrid` and `Test-IsPSWindows` files into a temporary module. It
extracts the unchanged `New-RunspaceCleanupJob` function from `Utilities.ps1`.
The original module source is not checked into this repository.

Use a new task-owned directory for each preparation and observation. Analyze
the prepared `Fixture.psd1` as a Hybrid `net10.0` DLL, review and save the raw
`result.dependencyGraph`, then build with that graph as `--dependency-lock`.
Run `OperateVisual.ps1` once with the prepared original manifest and once with
the generated artifact manifest. The operator accepts only an explicit
`pwsh.exe`, starts an owned STA child, captures its window, verifies the sample
rows and columns, invokes Close, and requires the cleanup job to finish.

This fixture uses in-memory data only. It does not select Refresh, Timeout,
UseProfile, UseLocalVariable, or InitializationScript. The driver requests WPF
software rendering so an owned `PrintWindow` capture can observe the grid; this
does not prove default hardware rendering. PowerShell 5.1 on the measured host
lacks `Start-ThreadJob`, so it is outside this workflow's claimed host set.
