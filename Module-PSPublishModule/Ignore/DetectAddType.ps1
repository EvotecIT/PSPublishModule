function Get-Assemblies {
    param (
        [System.Management.Automation.Language.ScriptBlockAst] $Parser
    )
    $pipelineAst = $Parser.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.PipelineAst] }
    $addTypeCmd = $pipelineAst | Where-Object { $_.PipelineElements.CommandElements.Value -icontains 'Add-Type' }
    $assemblies = foreach ($cmd in $addTypeCmd) {
        $cmdElements = $cmd.PipelineElements.CommandElements | Where-Object { $_.Value -inotcontains 'Add-Type' }
        foreach ($element in $cmdElements) {
            if (
                $element -is [System.Management.Automation.Language.CommandParameterAst] -and
                $element.ParameterName -match 'AssemblyName'
            ) {
                $assembly = $true
            } elseif ($assembly) {
                if ($element.StaticType -eq [System.String]) {
                    (Get-Culture).TextInfo.ToTitleCase($element.Extent.Text)
                } elseif ($element.StaticType -eq [System.Object[]]) {
                    Invoke-Expression -Command $element.Extent.Text
                }
                $assembly = $false
            }
        }
    }
    return $assemblies | ForEach-Object { (Get-Culture).TextInfo.ToTitleCase($_) }
}

$Parser = [System.Management.Automation.Language.Parser]::ParseFile("C:\Support\GitHub\PSPublishModule\Ignore\DetetctAddTypeTest.ps1",[ref]$null, [ref]$null)
Get-Assemblies -Parser $Parser