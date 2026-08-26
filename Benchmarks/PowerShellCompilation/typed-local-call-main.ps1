param([int] $Calls)

. "$PSScriptRoot/typed-local-call-helper.ps1"

[long] $total = 0
for ([int] $index = 0; $index -lt $Calls; $index++) {
    $total = Add-One -Value $total
}
return $total
