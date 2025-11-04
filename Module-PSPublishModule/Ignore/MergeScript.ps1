$Functions = Get-MissingFunctions -FilePath 'C:\Users\przemyslaw.klys\OneDrive - Evotec\Support\GitHub\PSWinDocumentation.AD\Ignore\ACL.ps1' -SummaryWithCommands

$Functions.Summary | Format-Table -a

$Functions.Functions