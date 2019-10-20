function Format-UsingNamespace {
    [CmdletBinding()]
    param(
        [string] $FilePath,
        [string] $FilePathSave,
        [string] $FilePathUsing
    )

    if ($FilePathSave -eq '') {
        $FilePathSave = $FilePath
    }
    if ($FilePath -ne '' -and (Test-Path -Path $FilePath) -and (Get-Item -LiteralPath $FilePath).Length -gt 0kb) {
        $FileStream = New-Object -TypeName IO.FileStream -ArgumentList ($FilePath), ([System.IO.FileMode]::Open), ([System.IO.FileAccess]::Read), ([System.IO.FileShare]::ReadWrite);
        $ReadFile = New-Object -TypeName System.IO.StreamReader -ArgumentList ($FileStream, [System.Text.Encoding]::UTF8, $true);
        # Read Lines
        $UsingNamespaces = [System.Collections.Generic.List[string]]::new()
        #$AddTypes = [System.Collections.Generic.List[string]]::new()

        $Content = while (!$ReadFile.EndOfStream) {
            $Line = $ReadFile.ReadLine()
            if ($Line -like 'using namespace*') {
                $UsingNamespaces.Add($Line)
                #} elseif ($Line -like '*Add-Type*') {
                #$AddTypes.Add($Line)
            } else {
                $Line
            }
        }
        $ReadFile.Close()

        $null = New-Item -Path $FilePathSave -ItemType file -Force
        if ($UsingNamespaces) {
            # Repeat using namespaces
            $null = New-Item -Path $FilePathUsing -ItemType file -Force
            $UsingNamespaces = $UsingNamespaces.Trim() | Sort-Object -Unique
            $UsingNamespaces | Add-Content -LiteralPath $FilePathUsing -Encoding utf8
            #$UsingNamespaces | Add-Content -LiteralPath $FilePathUsing -Encoding utf8


            #$Content | Add-Content -LiteralPath $FilePathUsing -Encoding utf8
            $Content | Add-Content -LiteralPath $FilePathSave -Encoding utf8
            return $true
        } else {
            $Content | Add-Content -LiteralPath $FilePathSave -Encoding utf8
            return $False
        }
    }
}