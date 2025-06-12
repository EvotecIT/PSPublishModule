function Resolve-Encoding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Ascii','BigEndianUnicode','Unicode','UTF7','UTF8','UTF8BOM','UTF32','Default','OEM')]
        [string] $Name
    )

    switch ($Name.ToUpperInvariant()) {
        'UTF8BOM' { return [System.Text.UTF8Encoding]::new($true) }
        'UTF8'    { return [System.Text.UTF8Encoding]::new($false) }
        'OEM'     { return [System.Text.Encoding]::GetEncoding([Console]::OutputEncoding.CodePage) }
        default   { return [System.Text.Encoding]::$Name }
    }
}
