param([Parameter(Mandatory = $true)][string] $Root)

$ErrorActionPreference = 'Stop'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$legacy = [IO.Directory]::CreateDirectory((Join-Path $Root 'Legacy')).FullName
$module = [IO.Directory]::CreateDirectory((Join-Path $Root 'Module/Lib/Default')).FullName
$dependencies = [IO.Directory]::CreateDirectory((Join-Path $Root 'Module/Lib/Dependencies')).FullName
$compilerTemp = [IO.Directory]::CreateDirectory((Join-Path $Root 'Compiler')).FullName
$keyFile = Join-Path $Root 'fixture.snk'

# Use an ephemeral software key, without touching a certificate store or hardware provider.
$keyParameters = [Security.Cryptography.CspParameters]::new(24, 'Microsoft Enhanced RSA and AES Cryptographic Provider')
$keyParameters.KeyNumber = [Security.Cryptography.KeyNumber]::Signature
$keyParameters.Flags = [Security.Cryptography.CspProviderFlags]::CreateEphemeralKey
$key = [Security.Cryptography.RSACryptoServiceProvider]::new(2048, $keyParameters)
try {
    [IO.File]::WriteAllBytes($keyFile, $key.ExportCspBlob($true))
} finally {
    $key.Dispose()
}

$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
try {
    $jobs = @(
        @{
            Source = 'Dependency.cs'
            Output = Join-Path $legacy 'DesktopCoexistence.Dependency.dll'
            Options = '/optimize+ /define:LEGACY /keyfile:"' + $keyFile + '"'
            References = @()
        }
        @{
            Source = 'Dependency.cs'
            Output = Join-Path $dependencies 'DesktopCoexistence.Dependency.dll'
            Options = '/optimize+ /keyfile:"' + $keyFile + '"'
            References = @()
        }
        @{
            Source = 'LegacyResolver.cs'
            Output = Join-Path $legacy 'DesktopCoexistence.LegacyHost.dll'
            Options = '/optimize+'
            References = @()
        }
        @{
            Source = 'Module.cs'
            Output = Join-Path $module 'PowerForgeDesktopCoexistence.dll'
            Options = '/optimize+'
            References = @(
                [Management.Automation.PSCmdlet].Assembly.Location
                (Join-Path $dependencies 'DesktopCoexistence.Dependency.dll')
            )
        }
    )
    foreach ($job in $jobs) {
        $options = [CodeDom.Compiler.CompilerParameters]::new()
        $options.GenerateExecutable = $false
        $options.GenerateInMemory = $false
        $options.OutputAssembly = $job.Output
        $options.CompilerOptions = $job.Options
        $options.TempFiles = [CodeDom.Compiler.TempFileCollection]::new($compilerTemp, $false)
        [void] $options.ReferencedAssemblies.Add('System.dll')
        foreach ($reference in $job.References) {
            [void] $options.ReferencedAssemblies.Add($reference)
        }
        $result = $provider.CompileAssemblyFromFile($options, [string[]]@(Join-Path $PSScriptRoot $job.Source))
        if ($result.Errors.HasErrors) {
            throw "Fixture compilation failed for $($job.Source): $($result.Errors -join [Environment]::NewLine)"
        }
    }
} finally {
    $provider.Dispose()
}
'DESKTOP_COEXISTENCE_FIXTURE_BUILT'
