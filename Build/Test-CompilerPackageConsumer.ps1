[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Packages,

    [string] $ScratchRoot = [IO.Path]::GetTempPath()
)

$ErrorActionPreference = 'Stop'
$packageIds = @('PowerForge.Compiler', 'PowerForge', 'PowerForge.Modules')
$compiler = @($Packages | Where-Object {
    [IO.Path]::GetFileName($_) -match '^PowerForge\.Compiler\.[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\.nupkg$'
})
if ($compiler.Count -ne 1) { throw 'The package build must produce exactly one PowerForge.Compiler package.' }
$version = [IO.Path]::GetFileNameWithoutExtension($compiler[0]).Substring('PowerForge.Compiler.'.Length)
$matched = @{}
foreach ($id in $packageIds) {
    $match = @($Packages | Where-Object { [IO.Path]::GetFileName($_) -eq "$id.$version.nupkg" })
    if ($match.Count -ne 1 -or -not (Test-Path -LiteralPath $match[0] -PathType Leaf)) {
        throw "The package build must produce one $id package at version $version."
    }
    $matched[$id] = $match[0]
}

$parent = (Resolve-Path -LiteralPath $ScratchRoot).Path
$root = Join-Path $parent ('pfc-package-' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $root 'feed'
New-Item -ItemType Directory -Path $feed | Out-Null
try {
    Copy-Item -LiteralPath @($matched.Values) -Destination $feed
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../global.json') -Destination $root
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="PowerForge.Compiler" Version="[$version]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $root 'Consumer.csproj')
    @'
<configuration>
  <packageSources>
    <clear />
    <add key="staged" value="./feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="staged">
      <package pattern="PowerForge.Compiler" />
      <package pattern="PowerForge" />
      <package pattern="PowerForge.Modules" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $root 'NuGet.Config')
    'return [int]21' | Set-Content -LiteralPath (Join-Path $root 'Input.ps1')
    @'
using PowerForge;

var compiler = new PowerShellCompiler();
var request = new PowerShellCompilationRequest("Input.ps1")
{
    Mode = PowerShellCompilationMode.Strict,
    OutputDirectory = "output"
};
var check = compiler.Check(request);
if (check.ProposedReview is not { } review)
    throw new InvalidOperationException(string.Join(Environment.NewLine, check.Diagnostics.Select(d => d.Message)));
var environment = compiler.CheckEnvironment(review.Target);
if (!environment.Succeeded)
    throw new InvalidOperationException(string.Join(Environment.NewLine, environment.Diagnostics.Select(d => d.Message)));
var result = compiler.BuildReviewed(compiler.Prepare(request), review);
if (!result.Succeeded)
    throw new InvalidOperationException(result.Error + Environment.NewLine + result.BuildOutput);
if (result.Manifest?.RequiresPowerShellRuntime is not false || result.ArtifactPath is null)
    throw new InvalidOperationException("Expected a runtime-independent Strict executable.");
Console.WriteLine(result.ArtifactPath);
'@ | Set-Content -LiteralPath (Join-Path $root 'Program.cs')

    Push-Location $root
    try {
        $cache = Join-Path $root 'packages'
        $originalFallback = $env:NUGET_FALLBACK_PACKAGES
        try {
            $env:NUGET_FALLBACK_PACKAGES = $null
            $restore = & dotnet restore Consumer.csproj --configfile NuGet.Config --packages $cache -p:DisableImplicitNuGetFallbackFolder=true -p:RestoreFallbackFolders= --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw "The staged compiler package set failed to restore. $($restore -join [Environment]::NewLine)" }
        } finally {
            $env:NUGET_FALLBACK_PACKAGES = $originalFallback
        }
        $assets = Get-Content -LiteralPath 'obj/project.assets.json' -Raw | ConvertFrom-Json
        foreach ($id in $packageIds) {
            $property = $assets.libraries.PSObject.Properties["$id/$version"]
            if ($null -eq $property) {
                throw "The compiler consumer did not resolve $id at version $version."
            }
            $archive = Join-Path (Join-Path $cache $property.Value.path) ("$id.$version.nupkg".ToLowerInvariant())
            if ((Get-FileHash -LiteralPath $archive).Hash -ne (Get-FileHash -LiteralPath $matched[$id]).Hash) {
                throw "The compiler consumer did not restore the staged $id archive."
            }
        }
        $result = & dotnet run --project Consumer.csproj --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "The packaged compiler failed to build an executable. $($result -join [Environment]::NewLine)" }
        $artifact = $result | Select-Object -Last 1
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) { throw 'The packaged compiler did not produce an executable.' }
        $actual = & $artifact
        if ($LASTEXITCODE -ne 0 -or ($actual -join [Environment]::NewLine) -ne '21') {
            throw "The reviewed Strict executable did not produce the expected output. Received '$actual'."
        }
        Write-Host "Verified the matched compiler package set ($version) with a reviewed Strict executable."
    } finally {
        Pop-Location
    }
} finally {
    $item = Get-Item -LiteralPath $root
    if ($item.Parent.FullName -ne $parent -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected compiler smoke workspace '$root'."
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}
