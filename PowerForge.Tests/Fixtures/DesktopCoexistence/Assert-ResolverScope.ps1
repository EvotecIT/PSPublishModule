param(
    [Parameter(Mandatory = $true)] $ResolverState,
    [Parameter(Mandatory = $true)][Reflection.Assembly] $Owner,
    [Parameter(Mandatory = $true)][Reflection.Assembly] $Foreign,
    [Parameter(Mandatory = $true)][Reflection.AssemblyName] $Expected
)

$resolver = $ResolverState.Handler
$name = $Expected.FullName
$dynamic = [AppDomain]::CurrentDomain.DefineDynamicAssembly(
    [Reflection.AssemblyName]::new('DesktopCoexistence.DynamicCaller'),
    [Reflection.Emit.AssemblyBuilderAccess]::Run)
$locationless = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Foreign.Location))
if (-not $dynamic.IsDynamic -or $locationless.IsDynamic -or $locationless.Location) {
    throw 'The dynamic and locationless requester fixtures are not distinct.'
}
$originalBootstrap = $ResolverState.BootstrapActive
try {
    foreach ($bootstrap in @($true, $false)) {
        $ResolverState.BootstrapActive = $bootstrap
        $owned = $resolver.Invoke($null, [ResolveEventArgs]::new($name, $Owner))
        if ($null -eq $owned -or $owned.FullName -ne $name) {
            throw "The owned request returned the wrong identity (bootstrap=$bootstrap)."
        }
        if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($name, $Foreign))) {
            throw "Another module's request was answered (bootstrap=$bootstrap)."
        }

        foreach ($caller in @($null, $dynamic, $locationless)) {
            $declared = $resolver.Invoke($null, [ResolveEventArgs]::new($name, $caller))
            if ($null -eq $declared -or $declared.FullName -ne $name) {
                throw "An exact declared reference was denied (bootstrap=$bootstrap)."
            }
            # Both candidates exist in owned directories. The lower dependency request has the
            # same token/culture and could upgrade, but it is not an exact declared reference.
            $lower = [Reflection.AssemblyName]::new($name)
            $lower.Version = [Version]'1.0.0.0'
            foreach ($undeclared in @($Owner.FullName, $lower.FullName)) {
                if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($undeclared, $caller))) {
                    throw "An undeclared unattributed request was answered (bootstrap=$bootstrap)."
                }
            }
        }

        $culture = [Reflection.AssemblyName]::new($name)
        $culture.CultureInfo = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        $token = [Reflection.AssemblyName]::new($name)
        $wrongToken = [byte[]]$Expected.GetPublicKeyToken().Clone()
        $wrongToken[0] = $wrongToken[0] -bxor 255
        $token.SetPublicKeyToken($wrongToken)
        $newer = [Reflection.AssemblyName]::new($name)
        $newer.Version = [Version]'3.0.0.0'
        foreach ($mismatch in @($culture, $token, $newer)) {
            # A real owned requester reaches identity checks rather than the attribution guard.
            if ($null -ne $resolver.Invoke($null, [ResolveEventArgs]::new($mismatch.FullName, $Owner))) {
                throw "A mismatched identity was answered: $($mismatch.FullName) (bootstrap=$bootstrap)."
            }
        }
    }
} finally {
    $ResolverState.BootstrapActive = $originalBootstrap
}
'DESKTOP_COEXISTENCE_SCOPE_OK'
