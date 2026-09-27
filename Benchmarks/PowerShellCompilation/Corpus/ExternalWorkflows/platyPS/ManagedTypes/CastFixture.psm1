function Convert-ManagedModel { [CmdletBinding()]param($Value) [Markdown.MAML.Model.MAML.MamlCommand]$Value }
function Invoke-ConstrainedModel { param($Value) [Markdown.MAML.Renderer.YamlRenderer]::MamlModelToString([Markdown.MAML.Model.MAML.MamlCommand]$Value) }
function Read-ConstrainedModel { param($Value) ([Markdown.MAML.Model.MAML.MamlCommand]$Value)[0] }
