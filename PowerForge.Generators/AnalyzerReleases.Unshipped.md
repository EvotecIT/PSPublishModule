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
PFF010 | PowerForge.Fluent | Error | Fluent preset cannot be assigned
PFF011 | PowerForge.Fluent | Error | Fluent copy method is missing or has the wrong shape
PFF012 | PowerForge.Fluent | Error | [FluentInline] needs a settings-type property
PFF013 | PowerForge.Fluent | Error | [FluentMutator] method has the wrong shape
PFF014 | PowerForge.Fluent | Error | Fluent type cannot get methods
PFF015 | PowerForge.Fluent | Info | Property is not edited in place
