# Test-only artifact closure checks. PE metadata is read without loading or executing the assembly.

function Assert-WorkflowNestedContract {
    param($Entry)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($required in @(Get-WorkflowProperty $Entry.qualification 'requiredCompiledNestedFunctions' @())) {
        foreach($name in @('relativePath','parentName','sourceBodyName')) {
            $value=Get-WorkflowProperty $required $name
            if($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
                throw 'Required nested functions need relativePath, parentName and sourceBodyName strings.'
            }
        }
        if($required.relativePath -notin @($Entry.sourceInputs.path) -or
            (Test-LooksLikeRootedPath $required.relativePath) -or
            @($required.relativePath.Replace('\','/').Split('/')|Where-Object {$_ -in @('','.', '..')}).Count) {
            throw 'Required nested function source must identify a pinned portable source input.'
        }
        foreach($name in @('parentStartLine','startOffset','endOffset')) {
            $value=Get-WorkflowProperty $required $name
            if($value -isnot [int] -and $value -isnot [long]) {throw 'Nested parent lines and authored extents must be integers.'}
        }
        if($required.parentStartLine -lt 1 -or $required.startOffset -lt 0 -or $required.endOffset -le $required.startOffset) {
            throw 'Nested functions require a positive parent body line and a nonempty authored extent.'
        }
        $key=$required.relativePath.Replace('\','/')+'|'+$required.startOffset+'|'+$required.endOffset
        if(-not $seen.Add($key)) {throw 'Required nested function extent is duplicated.'}
        $parents=@($Entry.qualification.requiredCompiledOwnedFunctions|Where-Object {
            $_.relativePath.Replace('\','/') -ceq $required.relativePath.Replace('\','/') -and
            $_.name -ceq $required.parentName -and
            ($null -eq (Get-WorkflowProperty $_ 'startLine') -or $_.startLine -eq $required.parentStartLine)
        })
        if($parents.Count -ne 1) {throw 'Required nested function parent must identify one declared complete compiled owner.'}
    }
}

function Get-WorkflowScriptBlockMappings {
    param($Graph)
    if($null -eq $Graph) {return}
    foreach($block in @(Get-WorkflowProperty $Graph 'scriptBlocks' @())) {
        $block
        Get-WorkflowScriptBlockMappings (Get-WorkflowProperty $block 'graph')
    }
}

function Get-WorkflowManagedMethodMetadata {
    param([string] $AssemblyPath, [string[]] $Names)
    $selected=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($name in $Names) {[void]$selected.Add($name)}
    $stream=[IO.File]::Open($AssemblyPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $pe=$null
    try {
        $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
        if(-not $pe.HasMetadata) {throw 'Generated artifact is not a managed PE assembly.'}
        $reader=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach($typeHandle in $reader.TypeDefinitions) {
            $type=$reader.GetTypeDefinition($typeHandle)
            foreach($methodHandle in $type.GetMethods()) {
                $method=$reader.GetMethodDefinition($methodHandle)
                $name=$reader.GetString($method.Name)
                if(-not $selected.Contains($name)) {continue}
                $isPrivate=($method.Attributes -band [Reflection.MethodAttributes]::MemberAccessMask) -eq [Reflection.MethodAttributes]::Private
                $isStatic=($method.Attributes -band [Reflection.MethodAttributes]::Static) -ne 0
                $isIl=($method.ImplAttributes -band [Reflection.MethodImplAttributes]::CodeTypeMask) -eq [Reflection.MethodImplAttributes]::IL
                $ilBytes=0
                if($isIl -and $method.RelativeVirtualAddress -gt 0) {
                    $body=[Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe,$method.RelativeVirtualAddress)
                    $ilBytes=$body.GetILBytes().Length
                }
                [pscustomobject]@{
                    generatedMemberName=$name;declaringType=$reader.GetString($type.Namespace)+'.'+$reader.GetString($type.Name)
                    private=$isPrivate;static=$isStatic;ilBodyBytes=$ilBytes;relativeVirtualAddress=$method.RelativeVirtualAddress
                }
            }
        }
    } finally {if($pe) {$pe.Dispose()};$stream.Dispose()}
}

function Assert-WorkflowNestedEmission {
    param($Entry, $Manifest, [string] $ArtifactRoot)
    $required=@(Get-WorkflowProperty $Entry.qualification 'requiredCompiledNestedFunctions' @())
    if($required.Count -eq 0) {return}
    Assert-WorkflowNestedContract $Entry
    $selected=[Collections.Generic.List[object]]::new()
    $members=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($body in $required) {
        $parentId=Get-WorkflowProperty $body 'parentUnitId'
        $parents=@($Manifest.unitDispositionLedger.entries|Where-Object {
            $_.relativePath.Replace('\','/') -ceq $body.relativePath.Replace('\','/') -and
            $_.name -ceq $body.parentName -and $_.startLine -eq $body.parentStartLine -and
            (-not $parentId -or $_.unitId -ceq $parentId)
        })
        if($parents.Count -ne 1) {throw "Nested function '$($body.sourceBodyName)' parent is missing or ambiguous."}
        $parent=$parents[0]
        if($parent.emittedClrMethod -ne $true -or $parent.retainedHostedSource -ne $false -or
            $parent.omitted -ne $false -or $parent.rejected -ne $false -or $parent.shapingFallback -ne $false) {
            throw "Nested function '$($body.sourceBodyName)' parent lacks a complete emitted CLR body."
        }
        $matches=@(Get-WorkflowScriptBlockMappings (Get-WorkflowProperty $parent 'regionGraph')|Where-Object {
            $_.startOffset -eq $body.startOffset -and $_.endOffset -eq $body.endOffset
        })
        if($matches.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string](Get-WorkflowProperty $matches[0] 'generatedMemberName'))) {
            throw "Nested function '$($body.sourceBodyName)' exact authored extent mapping is missing or ambiguous."
        }
        $member=[string]$matches[0].generatedMemberName
        if(-not $members.Add($member)) {throw 'Required nested bodies map to the same generated member.'}
        $selected.Add([pscustomobject]@{required=$body;parentUnitId=$parent.unitId;generatedMemberName=$member})
    }
    $assemblies=@($Manifest.files|Where-Object role -CEQ 'TypedAssembly')
    if($assemblies.Count -ne 1) {throw 'Nested body inspection requires exactly one authenticated generated TypedAssembly.'}
    $file=$assemblies[0]
    $path=Assert-ContainedPath -Root $ArtifactRoot -Path ([string]$file.path) -Label 'Authenticated generated nested-body assembly'
    Assert-WorkflowPathWithoutLinks $path
    if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or $file.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256 -or (Get-Item -LiteralPath $path).Length -ne $file.sizeBytes) {
        throw 'Generated nested-body assembly bytes differ from authenticated artifact evidence.'
    }
    $metadata=@(Get-WorkflowManagedMethodMetadata $path @($selected|ForEach-Object {$_.generatedMemberName}))
    if((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) {throw 'Generated assembly changed during metadata inspection.'}
    foreach($selection in $selected) {
        $methods=@($metadata|Where-Object generatedMemberName -CEQ $selection.generatedMemberName)
        if($methods.Count -ne 1) {throw "Nested generated method '$($selection.generatedMemberName)' is missing or ambiguous in artifact metadata."}
        $method=$methods[0]
        if(-not $method.private -or -not $method.static -or $method.ilBodyBytes -le 0) {
            throw "Nested generated method '$($selection.generatedMemberName)' must be private static with a nonempty IL body."
        }
        [pscustomobject]@{
            relativePath=$selection.required.relativePath;sourceBodyName=$selection.required.sourceBodyName
            startOffset=$selection.required.startOffset;endOffset=$selection.required.endOffset
            parentName=$selection.required.parentName;parentStartLine=$selection.required.parentStartLine;parentUnitId=$selection.parentUnitId
            generatedMemberName=$selection.generatedMemberName;declaringType=$method.declaringType
            private=$true;static=$true;ilBodyBytes=$method.ilBodyBytes;assemblyPath=$path;assemblySha256=$file.sha256
            scope='Exact authored nested-body mapping and actual generated PE method presence; not a nested-body execution count.'
        }
    }
}
