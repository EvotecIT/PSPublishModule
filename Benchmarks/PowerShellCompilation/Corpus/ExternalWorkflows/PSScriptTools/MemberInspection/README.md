# Offline member inspection

`Show-HiddenMember.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/Show-HiddenMember.ps1`, SHA-256 `cd5d7e833db4183695181880ebc4d45d76f5eca4e474bc08ee816bf2e1967ca1`. The original MIT license is included.

The compiler qualification compares direct and repeated pipeline input, member-type selection, property-method exclusion, hidden-member type names and information records on Windows PowerShell 5.1/net472 and PowerShell 7/net10.0. Inputs are local strings, a fixed date and note-property objects. Get-Member and the other pipeline commands retain their active PowerShell host; this is not a runtime-free reflection implementation or full-module qualification.

Some single-result inputs on Windows PowerShell 5.1 leave a scalar value for the authored `.where()` call and report `MethodNotFound`. The qualification captures and preserves those nonterminating errors and their source positions; it does not patch the upstream function.
