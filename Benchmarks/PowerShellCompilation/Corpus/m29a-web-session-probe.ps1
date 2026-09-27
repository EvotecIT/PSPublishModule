param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference = 'Stop'
Import-Module $ModulePath -Force
$cases = @(
    @{name='null';cookies=$null},
    @{name='empty';cookies=@{}},
    @{name='one';cookies=[ordered]@{owned='value'};uri='https://offline.invalid/path'},
    @{name='multiple';cookies=[ordered]@{first='one';second='two'};uri='https://offline.invalid/path'},
    @{name='missing-domain';cookies=@{owned='value'}},
    @{name='empty-value';cookies=@{owned=''};uri='https://offline.invalid/'},
    @{name='null-value';cookies=@{owned=$null};uri='https://offline.invalid/'},
    @{name='bad-name';cookies=[ordered]@{first='one';'bad name'='two'};uri='https://offline.invalid/'},
    @{name='bad-value';cookies=@{owned='bad;value'};uri='https://offline.invalid/'},
    @{name='relative-uri';cookies=@{owned='value'};uri='/path'},
    @{name='http';cookies=@{owned='value'};uri='http://offline.invalid/path'}
)
foreach($action in 'Continue','Stop') {
    foreach($case in $cases) {
        $errors=@();$outer=$null;$result=@()
        try { $result=@(New-WebSession -Cookies $case.cookies -For $case.uri -ErrorAction $action -ErrorVariable errors 2>$null) }
        catch { $outer=$_.FullyQualifiedErrorId }
        $session=if($result.Count -eq 1){$result[0]}else{$null}
        $cookies=if($null -ne $session){@($session.Cookies.GetCookies([uri]'https://offline.invalid/') | ForEach-Object {$_.Name+'='+$_.Value})}else{@()}
        [pscustomobject]@{case=$case.name;action=$action;count=$result.Count;type=if($null -ne $session){$session.GetType().FullName};
            cookieCount=if($null -ne $session){$session.Cookies.Count};cookies=$cookies;outer=$outer;
            errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 5 -Compress
    }
}
$first=New-WebSession -Cookies @{owned='value'} -For 'https://offline.invalid/'
$second=New-WebSession -Cookies @{} -For 'https://offline.invalid/'
$first.Cookies.GetCookies([uri]'https://offline.invalid/')[0].Value='changed'
[pscustomobject]@{case='reuse';same=[object]::ReferenceEquals($first,$second);first=$first.Cookies.GetCookies([uri]'https://offline.invalid/')[0].Value;second=$second.Cookies.Count}|ConvertTo-Json -Compress
