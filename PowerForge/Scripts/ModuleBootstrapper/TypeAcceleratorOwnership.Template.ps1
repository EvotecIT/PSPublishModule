# PowerShell's accelerator catalog is process-wide, including across module ALCs and runspaces.
# This lock coordinates this helper's callers; external catalog writes can still race registration or removal.
$PowerForgeAcceleratorOwnersKey = 'PowerForge.TypeAcceleratorOwners.v1'
$PowerForgeAcceleratorDomain = [AppDomain]::CurrentDomain
[Threading.Monitor]::Enter($PowerForgeAcceleratorDomain)
try {
    $PowerForgeAcceleratorOwners = $PowerForgeAcceleratorDomain.GetData($PowerForgeAcceleratorOwnersKey)
    if ($null -eq $PowerForgeAcceleratorOwners) {
        $PowerForgeAcceleratorOwners = [hashtable]::Synchronized(@{})
        $PowerForgeAcceleratorDomain.SetData($PowerForgeAcceleratorOwnersKey, $PowerForgeAcceleratorOwners)
    }
} finally {
    [Threading.Monitor]::Exit($PowerForgeAcceleratorDomain)
}
if ($null -eq $script:PowerForgeAcceleratorOwnerId) {
    $script:PowerForgeAcceleratorOwnerId = [Guid]::NewGuid().ToString('N')
}
$PowerForgeAcceleratorOwnerId = $script:PowerForgeAcceleratorOwnerId
$RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators

$RegisterPowerForgeTypeAcceleratorOwner = {
    param([string] $Name, [type] $Type)
    [Threading.Monitor]::Enter($PowerForgeAcceleratorOwners.SyncRoot)
    try {
        $Catalog = $GetTypeAccelerators.GetValue($null)
        if ($Catalog.ContainsKey($Name)) {
            if (-not [object]::ReferenceEquals($Catalog[$Name], $Type)) { return }
        } else {
            $AddTypeAccelerator.Invoke($null, @($Name, $Type)) | Out-Null
            $PreviousEntry = $PowerForgeAcceleratorOwners[$Name]
            if ($null -eq $PreviousEntry -or -not [object]::ReferenceEquals($PreviousEntry.Type, $Type)) {
                $PowerForgeAcceleratorOwners[$Name] = @{ Type = $Type; Owners = @{} }
            }
        }
        $Entry = $PowerForgeAcceleratorOwners[$Name]
        if ($null -ne $Entry -and [object]::ReferenceEquals($Entry.Type, $Type)) {
            $Entry.Owners[$PowerForgeAcceleratorOwnerId] = $true
            $RegisteredPowerForgeTypeAccelerators[$Name] = $Type
        }
    } finally {
        [Threading.Monitor]::Exit($PowerForgeAcceleratorOwners.SyncRoot)
    }
}.GetNewClosure()

$UnregisterPowerForgeTypeAcceleratorOwners = {
    [Threading.Monitor]::Enter($PowerForgeAcceleratorOwners.SyncRoot)
    try {
        $Catalog = $GetTypeAccelerators.GetValue($null)
        $RemoveTypeAccelerator = $TypeAccelerators.GetMethod('Remove', [type[]]@([string]))
        foreach ($Owned in @($RegisteredPowerForgeTypeAccelerators.GetEnumerator())) {
            $Entry = $PowerForgeAcceleratorOwners[$Owned.Key]
            if ($null -eq $Entry -or -not [object]::ReferenceEquals($Entry.Type, $Owned.Value)) { continue }
            $Entry.Owners.Remove($PowerForgeAcceleratorOwnerId)
            if ($Entry.Owners.Count -eq 0) {
                if ($Catalog.ContainsKey($Owned.Key) -and [object]::ReferenceEquals($Catalog[$Owned.Key], $Owned.Value)) {
                    $RemoveTypeAccelerator.Invoke($null, @($Owned.Key)) | Out-Null
                }
                $PowerForgeAcceleratorOwners.Remove($Owned.Key)
            }
        }
        $RegisteredPowerForgeTypeAccelerators.Clear()
    } finally {
        [Threading.Monitor]::Exit($PowerForgeAcceleratorOwners.SyncRoot)
    }
}.GetNewClosure()
