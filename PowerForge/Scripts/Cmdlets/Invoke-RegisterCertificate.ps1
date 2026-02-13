param(
    $Path,
    $LocalStore,
    $Thumbprint,
    $TimeStampServer,
    $Include
)

Register-Certificate `
    -Path $Path `
    -LocalStore $LocalStore `
    -Thumbprint $Thumbprint `
    -TimeStampServer $TimeStampServer `
    -Include $Include

