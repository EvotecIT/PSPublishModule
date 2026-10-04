function Touch-DeclaredList {param([Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]]$Models) $Models[0].Name += ':list';$Models}
function Touch-DeclaredMap {param([Collections.Generic.Dictionary[string,Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]]]$Models) $Models['key'][0].Name += ':map';$Models['key']}
function Read-DeclaredSet {param([Collections.Generic.HashSet[Markdown.MAML.Model.MAML.MamlCommand]]$Models) foreach($model in $Models){$model}}
function Touch-DeclaredArray {[CmdletBinding()]param([Parameter(ValueFromPipeline)][Markdown.MAML.Model.MAML.MamlCommand[]]$Models) process {$Models[0].Name += ':array';$Models}}
function Read-AdvisoryModelOutput {[CmdletBinding()][OutputType([Markdown.MAML.Model.MAML.MamlCommand],[string],ParameterSetName='Other')][OutputType([Markdown.MAML.Model.MAML.MamlCommand[]])]param() 7}
