# Offline folder inspection

`Test-EmptyFolder.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/Test-EmptyFolder.ps1`, SHA-256 `59a427193e26c82f3c550c7c3219886379489e714976bec251b874a70a777e3a`. The original MIT license is included.

Hybrid net10.0 qualification compares original and generated execution on disposable empty, populated, nested, and Unicode-named directories, including path arrays, pipeline/PSPath binding, PassThru records, and missing-path errors. The net472 explanation retains this function because the authored `System.IO.EnumerationOptions` type is unavailable there; its version-guarded legacy branch does not establish complete native compilation on that target. Remote paths, inaccessible trees, reparse points, full-module import, and other platforms are outside this proof.
