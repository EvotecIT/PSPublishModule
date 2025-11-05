function Test-ScriptModule {
    [cmdletbinding()]
    param(
        [string] $ModuleName,
        [ValidateSet('Name', 'CommandType', 'ModuleName', 'Source')] $SortName,
        [switch] $Unique
    )
    $Module = Get-Module -ListAvailable $ModuleName
    $Path = Join-Path -Path $Module.ModuleBase -ChildPath $Module.RootModule
    $Output = Test-ScriptFile -Path $Path
    if ($Unique) {
        $Output = $Output | Sort-Object -Property 'Name' -Unique:$Unique
    }
    if ($SortName) {
        $Output | Sort-Object -Property $SortName
    } else {
        $Output
    }
}