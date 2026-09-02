function Test-NewObjectConstruction {
    $version = Microsoft.PowerShell.Utility\New-Object -TypeName System.Version -ArgumentList (1, 2, 3, 4)
    $builder = New-Object System.Text.StringBuilder

    return ($version.ToString() -eq '1.2.3.4') -and ($builder.Length -eq 0)
}

Test-NewObjectConstruction
