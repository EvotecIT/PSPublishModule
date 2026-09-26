# Offline recent-file selection

`get-lastModified.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/get-lastModified.ps1`, SHA-256 `7c4838631d77c6b5a388cf8f554bb8e7a92773d8ca3441e1f0e057b518862810`. The original MIT license is included.

The qualification uses disposable local files with fixed timestamps and an isolated session clock. Original/generated results cover filters, recursion, date intervals, empty output, repeated invocation, and invalid path/count binding. The literal `.Where(...)` predicate uses a compiled callback; filesystem commands and validation metadata retain their native PowerShell owners. Remote paths, access failures, reparse points, other providers, full-module import, and other platforms are outside this proof.
