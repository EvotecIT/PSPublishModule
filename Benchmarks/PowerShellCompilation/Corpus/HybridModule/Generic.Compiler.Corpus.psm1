function Measure-TextScore {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory = $true, Position = 0, ValueFromPipeline = $true)]
        [Alias('InputText')]
        [ValidateNotNullOrEmpty()]
        [string] $Text,

        [int] $Offset = 2
    )

    [int] $length = $Text.Length
    [int] $score = ($length -shl 1)
    $score += $Offset
    return $score
}

function Get-CountdownValue {
    [OutputType([long])]
    param(
        [long] $Number
    )

    if ($Number -le [long] 0) {
        return $Number
    }

    $Number -= [long] 1
    return Get-CountdownValue -Number $Number
}

function Get-RuntimeState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    [OutputType([string])]
    param(
        [string] $Target = 'sample'
    )

    if ($WhatIfPreference) {
        return 'whatif'
    }
    if ($PSCmdlet.ShouldProcess($Target, 'Inspect')) {
        return $PSVersionTable.PSVersion.ToString()
    }
    return $PSEdition
}

function Get-CommandText {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    [string] $captured = Write-Output $Text
    return $captured.ToUpperInvariant()
}

function Test-TokenPattern {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [string] $Token,
        [string[]] $Allowed = @('alpha', 'beta')
    )

    [int] $mask = (3 -shl 2)
    return (($Token -match '^[a-z]+$') -and ($Token -in $Allowed) -and (($mask -band 12) -eq 12))
}

function Get-EnvironmentBoundary {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [string] $Fallback = 'unset'
    )

    if ($env:POWERFORGE_COMPILER_CORPUS) {
        return $env:POWERFORGE_COMPILER_CORPUS
    }
    return $Fallback
}

Export-ModuleMember -Function @(
    'Measure-TextScore'
    'Get-CountdownValue'
    'Get-RuntimeState'
    'Get-CommandText'
    'Test-TokenPattern'
    'Get-EnvironmentBoundary'
)
