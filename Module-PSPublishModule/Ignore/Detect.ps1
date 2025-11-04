# https://www.nuget.org/packages/System.Reflection.MetadataLoadContext
Add-Type -Path ~/Downloads/MetadataLoader/lib/net7.0/System.Reflection.MetadataLoadContext.dll

$assemblyPath = ".../foo.dll"

$resolver = [System.Reflection.PathAssemblyResolver]::new(
    [string[]]@(
        (Get-ChildItem -Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) -Filter "*.dll").FullName
        $assemblyPath
    ))
$context = [System.Reflection.MetadataLoadContext]::new($resolver)
try {
    $smaAssembly = $context.LoadFromAssemblyPath([PSObject].Assembly.Location)
    $cmdletType = $smaAssembly.GetType('System.Management.Automation.Cmdlet')
    $cmdletAttribute = $smaAssembly.GetType('System.Management.Automation.CmdletAttribute')
    $aliasAttribute = $smaAssembly.GetType('System.Management.Automation.AliasAttribute')

    $assembly = $context.LoadFromAssemblyPath($assemblyPath)

    $cmdletsToExport = [System.Collections.Generic.List[string]]::new()
    $aliasesToExport = [System.Collections.Generic.List[string]]::new()
    $assembly.GetTypes() | Where-Object {
        $_.IsSubclassOf($cmdletType)
    } | ForEach-Object -Process {
        $cmdletInfo = $_.CustomAttributes | Where-Object { $_.AttributeType -eq $cmdletAttribute }
        if (-not $cmdletInfo) { return }

        $name = "$($cmdletInfo.ConstructorArguments[0].Value)-$($cmdletInfo.ConstructorArguments[1].Value)"
        $cmdletsToExport.Add($name)

        $aliases = $_.CustomAttributes | Where-Object { $_.AttributeType -eq $aliasAttribute }
        if (-not $aliases) { return }
        $aliasesToExport.Add($aliases.ConstructorArguments.Value.Value)
    }

    [PSCustomObject]@{
        CmdletsToExport = $cmdletsToExport
        AliasesToExport = $aliasesToExport
    }
}
finally {
    $context.Dispose()
}