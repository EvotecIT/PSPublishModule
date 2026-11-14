param(
    [Parameter(Mandatory = $true)]
    [string] $AssemblyPath,
    [Parameter(Mandatory = $true)]
    [string] $LoaderPath,
    [Parameter(Mandatory = $true)]
    [string] $RequestPath
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$resolver = $null
try {
    Add-Type -Path $LoaderPath
    $resolver = [PowerForge.ModuleLoader.V2.DesktopAssemblyResolver]::new(
        [string[]]@([IO.Path]::GetDirectoryName($AssemblyPath)),
        [string[]]@())
    $resolver.Register()
    Import-Module -Name $AssemblyPath -DisableNameChecking -ErrorAction Stop
    $resolver.BootstrapActive = $false
    [PSPublishModule.DocumentationRenderProcess]::Run($RequestPath)
}
catch {
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
finally {
    if ($null -ne $resolver) {
        $resolver.Unregister()
    }
}
