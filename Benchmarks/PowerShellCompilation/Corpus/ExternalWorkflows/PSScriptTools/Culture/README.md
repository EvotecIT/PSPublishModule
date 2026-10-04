# Offline culture-scoped invocation

`Test-WithCulture.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/Test-WithCulture.ps1`, SHA-256 `1c28fd392de5b84e6624c6ef7cef0b122af79874b601ee17fa41e63e43b6e659`. The original MIT license is included.

The compiler qualification executes only the script-block parameter set in fresh PowerShell host processes. It compares formatting under the requested culture, arguments, warnings after callback failure, and restoration of the authored `$PSCulture` and `$PSUICulture` values on success, failure and downstream first-record stop. It qualifies 12 cases per host on Windows PowerShell 5.1/net472 and PowerShell 7/net10.0; these host culture values are not assumed to match an arbitrary external thread override. The function, argument completer and local Invoke-Command remain unchanged. The file parameter set, remoting, other platforms and full-module execution are outside this proof.
