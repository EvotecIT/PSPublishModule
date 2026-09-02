([long] 42 -gt 0) -and
([System.DayOfWeek]::Monday -eq 'monday') -and
([System.DayOfWeek]::Monday -ceq 'monday') -and
([System.DayOfWeek]::Monday -eq '1') -and
([System.DayOfWeek]::Monday -ne 'NotADay')
