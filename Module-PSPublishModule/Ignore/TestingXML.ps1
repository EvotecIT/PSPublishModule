
$ModuleProjectFile = 'C:\Support\GitHub\DnsClientX\DnsClientX.PowerShell\DnsClientX.PowerShell.csproj'

try {
    [xml] $ProjectInformation = Get-Content -Raw -LiteralPath $ModuleProjectFile -Encoding UTF8 -ErrorAction Stop
} catch {
    Write-Text "[-] Can't read $ModuleProjectFile file. Error: $($_.Exception.Message)" -Color Red
    return $false
}

if ($IsLinux) {
    $Version = 'Linux'
} elseif ($IsMacOS) {
    $Version = 'OSX'
} else {
    $Version = 'Windows'
}

$SupportedFrameworks = foreach ($PropertyGroup in $ProjectInformation.Project.PropertyGroup) {
    if ($PropertyGroup.TargetFrameworks) {
        if ($PropertyGroup.TargetFrameworks -is [array]) {
            foreach ($Target in $PropertyGroup.TargetFrameworks) {
                if ($Target.Condition -like "*$Version*" -and $Target.'#text') {
                    $Target.'#text'.Trim() -split ";"
                }
            }
        } else {
            $PropertyGroup.TargetFrameworks -split ";"
        }
    } elseif ($PropertyGroup.TargetFrameworkVersion) {
        throw "TargetFrameworkVersion is not supported. Please use TargetFrameworks/TargetFramework instead which may require different project profile."
    } elseIf ($PropertyGroup.TargetFramework) {
        $PropertyGroup.TargetFramework
    }
}

$SupportedFrameworks