# Coordinate binding and progress records

`Write-AnsiProgress.ps1` is unchanged from PSScriptTools revision
`549fa3e3d769532320054fc0b3c8f42df0452361`, under the accompanying MIT license.
Source SHA-256: `2768f1fda6b2ecf58b0227b5a29b2f96c5c9ed2c93c58afec7dde764bfc7a493`.

`Observe.ps1` runs in isolated child processes with redirected output. It
supplies explicit coordinates and a fixed module-owned clock, then compares
progress character codes, pipeline cardinality, host-information records,
binding/validation failures, cursor error identity/position and error preferences.
It probes console readiness explicitly; an unavailable console is preserved as
an authored cursor error rather than hidden or mocked. Continue-mode cases
exercise the authored ANSI-record construction after that error.

This qualifies the compiler's SDK coordinate binding and the function's records
and failure behavior. It does not qualify visual terminal rendering, successful
cursor movement, the Windows Terminal adjustment branch, the ISE host or default
RawUI position acquisition. No user terminal state is changed.
