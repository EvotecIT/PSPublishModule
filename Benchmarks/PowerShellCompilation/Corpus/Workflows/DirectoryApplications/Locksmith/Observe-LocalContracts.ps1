function Invoke-OwnedClassicFunction {
    param([Management.Automation.PSModuleInfo] $Module, [string] $Name, [hashtable] $Arguments = @{})

    & $Module {
        param($Name, $Arguments, $OwnerPath)
        $command = Get-Command $Name -CommandType Function -ErrorAction Stop
        if ($command.Module.ModuleBase -ine $OwnerPath) {
            throw "The application does not own the selected function '$Name'."
        }
        & $command @Arguments
    } $Name $Arguments $Module.ModuleBase
}

function Invoke-ClassicLocalContracts {
    param([Management.Automation.PSModuleInfo] $Module, [string] $Directory)

    $result = [ordered]@{}
    $direct = [Collections.Generic.List[string]]::new()
    $sid = Invoke-OwnedClassicFunction $Module 'Convert-IdentityReferenceToSid' @{ Object = @('S-1-5-18') }
    if ([string] $sid -cne 'S-1-5-18') { throw 'The well-known SID did not survive conversion.' }
    $result.sid = [string] $sid
    $direct.Add('Convert-IdentityReferenceToSid')

    foreach ($name in 'Test-IsADAdmin', 'Test-IsElevated') {
        $flag = Invoke-OwnedClassicFunction $Module $name
        if ($flag -isnot [bool]) { throw "The local membership check '$name' did not return a Boolean." }
        $result[$name] = $flag
        $direct.Add($name)
    }

    & $Module {
        function script:Get-LocalUser { [pscustomobject]@{ SID = [Security.Principal.WindowsIdentity]::GetCurrent().User } }
        function script:Get-ADUser {
            param($Identity, $Properties)
            [pscustomobject]@{ SID = 'S-1-5-21-111-222-333-1000'; primaryGroupID = 513 }
        }
        function script:Invoke-RestMethod {
            param($Uri, $Method, [switch] $DisableKeepAlive, $ErrorAction)
            [pscustomobject]@{ tag_name = 'v2026.1'; Published_at = '2026-01-01'; Assets = @() }
        }
    }
    foreach ($check in @(
        @{ Name = 'Test-IsLocalAccountSession'; Args = @{} }
        @{ Name = 'Test-IsMemberOfProtectedUsers'; Args = @{ User = 'fixture' } }
        @{ Name = 'Test-IsRecentVersion'; Args = @{ Version = '2026.1'; Days = 32767 } }
    )) {
        $flag = Invoke-OwnedClassicFunction $Module $check.Name $check.Args
        if ($flag -cne $true) { throw "The controlled check '$($check.Name)' did not take its positive branch." }
        $result[$check.Name] = $flag
        $direct.Add($check.Name)
    }

    # Exercise the installer refusal with the actual installation commands still blocked.
    & $Module { function script:Test-IsElevated { $false } }
    $warnings = @(Invoke-OwnedClassicFunction $Module 'Install-RSATADPowerShell' 3>&1)
    if ($warnings.Count -ne 1 -or $warnings[0] -isnot [Management.Automation.WarningRecord] -or
        $warnings[0].Message -notlike '*elevated PowerShell session*') {
        throw 'The installer did not refuse the simulated non-elevated session.'
    }
    $result.installationRefused = $true
    $direct.Add('Install-RSATADPowerShell')

    $null = Invoke-OwnedClassicFunction $Module 'New-OutputPath' @{ Targets = @('example.invalid'); OutputPath = $Directory; Confirm = $false }
    $child = Join-Path $Directory 'example.invalid'
    if (!(Test-Path -LiteralPath $child -PathType Container)) { throw 'The local output directory was not created.' }
    $result.outputDirectoryCreated = $true
    $direct.Add('New-OutputPath')

    Push-Location -LiteralPath $child
    try {
        # There are no proposed directory changes in this scenario.
        $null = Invoke-OwnedClassicFunction $Module 'Invoke-Remediation' 6>$null
        $result.localFiles = @(Get-ChildItem -LiteralPath $child -File | ForEach-Object Name | Sort-Object)
    } finally {
        Pop-Location
    }
    if ($result.localFiles.Count -ne 0) { throw 'An empty remediation input should produce no local script.' }
    $direct.Add('Invoke-Remediation')

    $logo = @(Invoke-OwnedClassicFunction $Module 'Show-LocksmithLogo' 6>&1)
    $colored = @(Invoke-OwnedClassicFunction $Module 'Write-HostColorized' @{
        Pattern = @('fixture'); ForegroundColor = 'Green'; InputObject = 'fixture'
    } 6>&1)
    if ($logo.Count -eq 0 -or $colored.Count -eq 0) { throw 'The console helpers did not write their display records.' }
    $result.logoRecords = $logo.Count
    $result.coloredRecords = $colored.Count
    $direct.Add('Show-LocksmithLogo')
    $direct.Add('Write-HostColorized')

    # These helpers adjust proposal text in memory. The fixture never applies a proposal.
    & $Module {
        $script:FixturePrompts = 0
        function script:Read-Host {
            param($Prompt)
            $script:FixturePrompts++
            'y'
        }
    }
    $result.proposals = [ordered]@{}
    foreach ($name in 'Update-ESC1Remediation', 'Update-ESC4Remediation', 'Update-ESC7Remediation', 'Update-ESC9Remediation') {
        $before = & $Module { $script:FixturePrompts }
        $issue = [pscustomobject]@{
            Name = 'Fixture'
            IdentityReference = 'fixture'
            DistinguishedName = 'CN=Fixture,DC=example,DC=invalid'
            ActiveDirectoryRights = ''
            Right = 'CA Administrator'
            Issue = 'fixture'
            Fix = 'fixture'
            Revert = 'fixture'
        }
        $null = Invoke-OwnedClassicFunction $Module $name @{ Issue = $issue } 6>$null
        $prompts = (& $Module { $script:FixturePrompts }) - $before
        if ($prompts -lt 1) { throw "The proposal helper '$name' did not consume its fixture input." }
        $text = @($issue.Issue, $issue.Fix, $issue.Revert) | ConvertTo-Json -Compress
        $result.proposals[$name] = [ordered]@{
            prompts = $prompts
            sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
        }
        $direct.Add($name)
    }

    [ordered]@{ observation = $result; direct = @($direct) }
}
