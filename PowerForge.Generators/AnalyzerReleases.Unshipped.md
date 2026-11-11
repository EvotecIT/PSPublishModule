; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PFF001 | PowerForge.Fluent | Error | Delegate property needs a fluent verb
PFF002 | PowerForge.Fluent | Error | Fluent method collides with an existing method
PFF003 | PowerForge.Fluent | Error | Two members map to one fluent method
PFF004 | PowerForge.Fluent | Error | Forward target has no matching method
PFF005 | PowerForge.Fluent | Error | Fluent name is not a valid identifier
PFF006 | PowerForge.Fluent | Error | Cmdlet parameter name is reserved
PFF007 | PowerForge.Fluent | Error | [FluentCmdlet] needs a partial PowerShell cmdlet
PFF008 | PowerForge.Fluent | Error | Unknown vocabulary setting
PFF009 | PowerForge.Fluent | Error | [FluentCmdlet] names an unknown property
