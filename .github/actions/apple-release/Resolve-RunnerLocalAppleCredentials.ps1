param(
    [Parameter(Mandatory)] [string] $Action
)

$ErrorActionPreference = 'Stop'

function Assert-PrivateUnixPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [bool] $Directory,
        [Parameter(Mandatory)] [string] $Description
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { throw "$Description is missing." }
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a symbolic link or reparse point."
    }
    if ($Directory -and -not $item.PSIsContainer) { throw "$Description must be a directory." }
    if (-not $Directory -and $item.PSIsContainer) { throw "$Description must be a regular file." }

    $stat = @(& /usr/bin/stat -f '%u|%l|%HT' $item.FullName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $stat.Count -ne 1) { throw "$Description metadata could not be verified." }
    $statParts = $stat[0].Split('|')
    $currentUid = [string] (& /usr/bin/id -u)
    if ($LASTEXITCODE -ne 0 -or $statParts.Count -ne 3 -or $statParts[0] -ne $currentUid) {
        throw "$Description must be owned by the self-hosted runner user."
    }
    $expectedType = if ($Directory) { 'Directory' } else { 'Regular File' }
    if ($statParts[2] -ne $expectedType) { throw "$Description must be a $($expectedType.ToLowerInvariant())." }
    if (-not $Directory -and $statParts[1] -ne '1') { throw "$Description must not have hard links." }

    $listing = @(& /bin/ls -lde $item.FullName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $listing.Count -eq 0) { throw "$Description access controls could not be verified." }
    $modeToken = ($listing[0] -split '\s+', 2)[0]
    if ($modeToken.Contains('+')) { throw "$Description must not grant access through a POSIX ACL." }

    $mode = [System.IO.File]::GetUnixFileMode($item.FullName)
    $shared =
        [System.IO.UnixFileMode]::GroupRead -bor
        [System.IO.UnixFileMode]::GroupWrite -bor
        [System.IO.UnixFileMode]::GroupExecute -bor
        [System.IO.UnixFileMode]::OtherRead -bor
        [System.IO.UnixFileMode]::OtherWrite -bor
        [System.IO.UnixFileMode]::OtherExecute
    if (($mode -band $shared) -ne 0) {
        throw "$Description permissions must not grant group or other access."
    }
}

function Convert-ProfileValue {
    param(
        [Parameter(Mandatory)] [string] $Value,
        [Parameter(Mandatory)] [string] $Name
    )

    $text = $Value.Trim()
    if ($text.Length -ge 2 -and
        (($text[0] -eq '"' -and $text[$text.Length - 1] -eq '"') -or
         ($text[0] -eq "'" -and $text[$text.Length - 1] -eq "'"))) {
        $text = $text.Substring(1, $text.Length - 2)
    } elseif ($text.StartsWith('"') -or $text.StartsWith("'") -or
              $text.EndsWith('"') -or $text.EndsWith("'")) {
        throw "Runner-local credential '$Name' has unmatched quoting."
    }
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Runner-local credential '$Name' is empty."
    }
    return $text
}

if ($Action -ine 'Doctor') {
    throw 'Runner-local App Store Connect credentials are allowed only for the read-only Doctor action.'
}
if ($env:RUNNER_ENVIRONMENT -ine 'self-hosted' -or $env:RUNNER_OS -ine 'macOS') {
    throw 'Runner-local App Store Connect credentials require a self-hosted macOS runner.'
}

$homePath = [string] $env:HOME
if ([string]::IsNullOrWhiteSpace($homePath) -or -not [System.IO.Path]::IsPathRooted($homePath)) {
    throw 'The self-hosted runner HOME directory is unavailable.'
}
$homePath = [System.IO.Path]::GetFullPath($homePath)
$profileRoot = [System.IO.Path]::GetFullPath((Join-Path $homePath '.appstoreconnect'))
$profilePath = Join-Path $profileRoot 'env'
Assert-PrivateUnixPath -Path $profileRoot -Directory $true -Description 'Runner-local App Store Connect profile directory'
Assert-PrivateUnixPath -Path $profilePath -Directory $false -Description 'Runner-local App Store Connect credential profile'

$values = @{}
foreach ($line in [System.IO.File]::ReadAllLines($profilePath)) {
    if ($line -match '^\s*(?:#.*)?$') { continue }
    $match = [regex]::Match(
        $line,
        '^\s*(?:export\s+)?(?<name>(?:APP_STORE_CONNECT|ASC)_(?:ISSUER_ID|KEY_ID|PRIVATE_KEY_PATH))\s*=\s*(?<value>.*)\s*$')
    if (-not $match.Success) {
        throw 'Runner-local App Store Connect credential profile contains unsupported content.'
    }
    $name = $match.Groups['name'].Value
    if ($values.ContainsKey($name)) { throw "Runner-local credential '$name' is declared more than once." }
    $values[$name] = Convert-ProfileValue -Value $match.Groups['value'].Value -Name $name
}

$issuerId = [string] $values['APP_STORE_CONNECT_ISSUER_ID']
$keyId = [string] $values['APP_STORE_CONNECT_KEY_ID']
$keyPathSetting = [string] $values['APP_STORE_CONNECT_PRIVATE_KEY_PATH']
if ($issuerId -notmatch '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$') {
    throw 'Runner-local App Store Connect issuer id is missing or invalid.'
}
if ($keyId -notmatch '^[A-Z0-9]{10}$') {
    throw 'Runner-local App Store Connect key id is missing or invalid.'
}

$aliases = [ordered]@{
    ASC_ISSUER_ID = 'APP_STORE_CONNECT_ISSUER_ID'
    ASC_KEY_ID = 'APP_STORE_CONNECT_KEY_ID'
    ASC_PRIVATE_KEY_PATH = 'APP_STORE_CONNECT_PRIVATE_KEY_PATH'
}
foreach ($entry in $aliases.GetEnumerator()) {
    if (-not $values.ContainsKey($entry.Key)) { continue }
    $aliasValue = [string] $values[$entry.Key]
    $canonicalName = [string] $entry.Value
    $plainReference = '$' + $canonicalName
    $bracedReference = '${' + $canonicalName + '}'
    if ($aliasValue -ne $plainReference -and
        $aliasValue -ne $bracedReference) {
        throw "Runner-local credential alias '$($entry.Key)' must reference its canonical value."
    }
}

if ($keyPathSetting.StartsWith('$HOME/', [StringComparison]::Ordinal)) {
    $keyPathSetting = Join-Path $homePath $keyPathSetting.Substring(6)
} elseif ($keyPathSetting.StartsWith('${HOME}/', [StringComparison]::Ordinal)) {
    $keyPathSetting = Join-Path $homePath $keyPathSetting.Substring(8)
} elseif ($keyPathSetting.StartsWith('~/', [StringComparison]::Ordinal)) {
    $keyPathSetting = Join-Path $homePath $keyPathSetting.Substring(2)
}
if ($keyPathSetting.Contains('$') -or $keyPathSetting.Contains('`') -or
    -not [System.IO.Path]::IsPathRooted($keyPathSetting)) {
    throw 'Runner-local App Store Connect private-key path must be an absolute path or use only the HOME prefix.'
}

$comparison = [StringComparison]::Ordinal
$keyPath = [System.IO.Path]::GetFullPath($keyPathSetting)
$root = $profileRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$prefix = $root + [System.IO.Path]::DirectorySeparatorChar
if (-not $keyPath.StartsWith($prefix, $comparison) -or
    [System.IO.Path]::GetExtension($keyPath) -ine '.p8') {
    throw 'Runner-local App Store Connect private key must be a .p8 file inside the private profile directory.'
}

$current = $keyPath
while (-not $current.Equals($root, $comparison)) {
    Assert-PrivateUnixPath -Path $current -Directory:$($current -ne $keyPath) -Description 'Runner-local App Store Connect private-key path'
    $parent = [System.IO.Directory]::GetParent($current)?.FullName
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw 'Runner-local App Store Connect private-key path escaped the private profile directory.'
    }
    $current = $parent
}
$privateKeyText = [System.IO.File]::ReadAllText($keyPath)
try {
    if ($privateKeyText -notmatch '(?s)\A-----BEGIN PRIVATE KEY-----\r?\n(?<body>[A-Za-z0-9+/=\r\n]+)\r?\n-----END PRIVATE KEY-----\s*\z') {
        throw 'The private key must be an unencrypted PKCS#8 PEM document.'
    }
    $privateKeyBytes = [Convert]::FromBase64String(($Matches['body'] -replace '\s', ''))
    $privateKey = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $bytesRead = 0
        $privateKey.ImportPkcs8PrivateKey($privateKeyBytes, [ref] $bytesRead)
        $curveOid = [string] $privateKey.ExportParameters($false).Curve.Oid.Value
        if ($bytesRead -ne $privateKeyBytes.Length -or
            $privateKey.KeySize -ne 256 -or
            $curveOid -ne '1.2.840.10045.3.1.7') {
            throw 'The private key must use the Apple P-256 curve.'
        }
    } finally {
        $privateKey.Dispose()
        [Array]::Clear($privateKeyBytes, 0, $privateKeyBytes.Length)
    }
} catch {
    throw 'Runner-local App Store Connect private key is not a valid unencrypted P-256 PKCS#8 PEM document.'
}

$env:APP_STORE_CONNECT_ISSUER_ID = $issuerId
$env:APP_STORE_CONNECT_KEY_ID = $keyId
$env:APP_STORE_CONNECT_PRIVATE_KEY_PATH = $keyPath
