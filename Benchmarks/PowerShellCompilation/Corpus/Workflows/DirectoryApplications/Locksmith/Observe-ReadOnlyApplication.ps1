function Initialize-ClassicDirectoryFixture {
    param([Management.Automation.PSModuleInfo] $Module)

    & $Module {
        $script:FixtureCalls = [Collections.Generic.List[string]]::new()
        function script:Get-ADForest {
            [void] $script:FixtureCalls.Add('forest')
            [pscustomobject]@{ Name = 'example.invalid'; RootDomain = 'example.invalid'; Domains = @('example.invalid') }
        }
        function script:Get-ADDomain {
            [CmdletBinding()]
            param([Parameter(ValueFromPipeline, Position=0)] $Identity, $Server, $Credential)
            process {
                [void] $script:FixtureCalls.Add('domain')
                [pscustomobject]@{ DomainSID = [Security.Principal.SecurityIdentifier]::new('S-1-5-21-111-222-333') }
            }
        }
        function script:Get-ADDomainController {
            param([switch] $Discover, $Service, [switch] $ForceDiscover)
            [void] $script:FixtureCalls.Add('controller')
            [pscustomobject]@{ HostName = 'directory.example.invalid' }
        }
        function script:Get-ADGroupMember {
            param($Identity, $Server, [switch] $Recursive)
            [void] $script:FixtureCalls.Add('groups')
            [pscustomobject]@{ SID = [Security.Principal.SecurityIdentifier]::new('S-1-5-21-111-222-333-1000') }
        }
        function script:Get-ADRootDSE {
            param($Server, $Credential)
            [void] $script:FixtureCalls.Add('root')
            [pscustomobject]@{
                ConfigurationNamingContext = 'CN=Configuration,DC=example,DC=invalid'
                DefaultNamingContext = 'DC=example,DC=invalid'
            }
        }
        function script:Get-ADObject {
            param($Server, $SearchBase, $Filter, $LDAPFilter, $Properties, $Credential, $SearchScope, $Identity)
            [void] $script:FixtureCalls.Add('objects')
            foreach ($number in 1, 2) {
                $object = [Microsoft.ActiveDirectory.Management.ADObject]::new()
                $object.ObjectClass = 'container'
                $object | Add-Member -NotePropertyName CanonicalName -NotePropertyValue "example.invalid/Fixture$number" -Force
                $object | Add-Member -NotePropertyName DistinguishedName -NotePropertyValue "CN=Fixture$number,DC=example,DC=invalid" -Force
                $object
            }
        }
        function script:Get-ItemProperty {
            param($Path, $Name)
            [pscustomobject]@{ DisableRestrictedAdmin = 1 }
        }
        function script:Invoke-WebRequest { throw 'The directory fixture does not make HTTP requests.' }
        function script:Invoke-RestMethod { throw 'The directory fixture does not make HTTP requests.' }
        function script:Invoke-Command { throw 'The directory fixture does not use remoting.' }
        function script:Add-WindowsCapability { throw 'The directory fixture does not install components.' }
        function script:Install-WindowsFeature { throw 'The directory fixture does not install components.' }
        function script:Read-Host { throw 'The directory fixture does not allow interactive prompts.' }
        function script:Set-ADObject { throw 'The directory fixture does not write directory entries.' }
    }
}

function Invoke-ClassicReadOnlyApplication {
    param(
        [Management.Automation.PSModuleInfo] $Module,
        [string] $Directory,
        [ValidateSet('Auditing', 'All')] [string] $Scans
    )

    Initialize-ClassicDirectoryFixture -Module $Module
    $formatError = $null
    $output = @()
    $pipeline = [Management.Automation.PowerShell]::Create([Management.Automation.RunspaceMode]::CurrentRunspace)
    try {
        # The no-findings branch uses a bare break. Let the nested pipeline own that exit.
        $null = $pipeline.AddScript({
            param($Owner, $Path, $Selected)
            & $Owner {
                param($OutputPath, $Scans)
                Invoke-Locksmith -Mode 0 -Scans $Scans -OutputPath $OutputPath
            } $Path $Selected
        }.ToString()).AddArgument($Module).AddArgument($Directory).AddArgument($Scans)
        try {
            $output = @($pipeline.Invoke())
        } catch {
            $native = $_.Exception.InnerException
            if ($Scans -ne 'All' -or $native -isnot [Management.Automation.RuntimeException] -or
                $native.ErrorRecord.FullyQualifiedErrorId -cne 'NullArrayIndex,Format-Result') { throw }
            $formatError = $native.ErrorRecord.FullyQualifiedErrorId
        }
        if ((!$formatError -and $pipeline.HadErrors) -or $pipeline.Streams.Error.Count -ne 0 -or
            $pipeline.Streams.Warning.Count -ne 0 -or $output.Count -ne 0) {
            throw 'The read-only audit did not complete on the inert directory fixture.'
        }
    } finally {
        $pipeline.Dispose()
    }
    $calls = @(& $Module { $script:FixtureCalls })
    $expected = @('forest', 'controller', 'domain', 'groups', 'domain', 'groups', 'groups', 'groups', 'forest', 'root', 'objects')
    if ($Scans -eq 'All') {
        if (!$formatError) { throw 'The incomplete fixture record must produce the pinned formatting error.' }
        $expected += @('objects', 'objects')
    }
    if (($calls -join ',') -cne ($expected -join ',') -or @(Get-ChildItem -LiteralPath $Directory -Force).Count -ne 0) {
        throw 'The audit did not follow the pinned read-only query and output contract.'
    }
    [ordered]@{ directoryReads = $calls; successRecords = $output.Count; formatError = $formatError; files = @() }
}
