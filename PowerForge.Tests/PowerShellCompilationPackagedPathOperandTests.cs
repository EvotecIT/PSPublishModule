using System.Management.Automation.Language;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("Get-Content -Path $Path", "$Path")]
    [InlineData("Get-Content -LiteralPath:$Path", "$Path")]
    [InlineData("Get-Content $Path -Raw", "$Path")]
    [InlineData("gc -Path:$Path", "$Path")]
    [InlineData("cat $Path", "$Path")]
    [InlineData("type -LiteralPath $Path", "$Path")]
    [InlineData("Microsoft.PowerShell.Management\\Get-Content -Path $Path", "$Path")]
    [InlineData("Get-Content data.txt -Filter $Filter -Encoding $Encoding", "data.txt")]
    [InlineData("Get-Content -Filter $Filter -Path data.txt", "data.txt")]
    [InlineData("Get-Item -Path $Path", "$Path")]
    [InlineData("gci -LiteralPath:$Paths", "$Paths")]
    public void PackagedPathPolicy_SelectsOnlyTheAuthoredPathOperand(string source, string expected)
    {
        var syntax = Parser.ParseInput(source, out _, out var errors);
        Assert.Empty(errors);
        var command = Assert.IsType<CommandAst>(Assert.IsType<PipelineAst>(syntax.EndBlock.Statements[0]).PipelineElements[0]);
        var selected = Assert.IsAssignableFrom<ExpressionAst>(PowerShellPackagedPathOperandPolicy.Select(command));
        Assert.Equal(expected, selected.Extent.Text);
        var before = command.Extent.Text;
        PowerShellPackagedScriptRewriter.ValidateHostInteraction(syntax);
        Assert.Equal(before, command.Extent.Text);
    }

    [Theory]
    [InlineData("Get-ChildItem")]
    [InlineData("gci")]
    [InlineData("Microsoft.PowerShell.Management\\Get-ChildItem")]
    public void PackagedPathPolicy_AdmitsOnlyBareCurrentLocationReadsWithoutChangingArguments(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var syntax = Parser.ParseInput(source, out _, out var errors);
        Assert.Empty(errors);
        var pipeline = Assert.IsType<PipelineAst>(syntax.EndBlock.Statements[0]);
        var command = Assert.IsType<CommandAst>(Assert.Single(pipeline.PipelineElements));
        Assert.Null(PowerShellPackagedPathOperandPolicy.Select(command));
        Assert.True(PowerShellPackagedPathOperandPolicy.IsImplicitCurrentLocationRead(command));
        Assert.Equal(new[] { command.Extent.StartOffset, command.Extent.StartOffset },
            PowerShellPackagedPathOperandPolicy.SelectNativeRegionOperands(pipeline));
        PowerShellPackagedScriptRewriter.ValidateHostInteraction(syntax);
        var retained = PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath);
        Assert.Contains("::ValidateCurrentLocation($ExecutionContext.SessionState); " + source + ")", retained);
        Assert.Single(command.CommandElements);
        Assert.Equal(TokenKind.Unknown, command.InvocationOperator);
        Assert.Equal(source, File.ReadAllText(fixture.ScriptPath));
    }

    [Theory]
    [InlineData("Get-Content -Path:Variable:Host")]
    [InlineData("cat -LiteralPath Variable:H*")]
    [InlineData("type -Path Microsoft.PowerShell.Core\\Variable::Host")]
    [InlineData("Get-Content -Path Variable:global:Host")]
    [InlineData("Get-Content -Path Microsoft.PowerShell.Core\\Variable::MyInvocation")]
    [InlineData("$Path='Variable:Host'; Get-Item -Path $Path")]
    [InlineData("Get-Variable -Name $Name")]
    [InlineData("$ExecutionContext.SessionState.PSVariable.Get($Name)")]
    public void PackagedPathPolicy_KeepsReservedVariableRetrievalFailClosed(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        Assert.Throws<InvalidOperationException>(() => PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath));
    }

    [Theory]
    [InlineData("Get-Content @Options")]
    [InlineData("Get-Content -Pa $Path")]
    [InlineData("Get-Content -Path $Path -LiteralPath $Other")]
    [InlineData("Get-Content -Path $Path other.txt")]
    [InlineData("Get-Content -Unknown $Value data.txt")]
    [InlineData("1..2 | ForEach-Object { Get-Content -Path $Path }")]
    [InlineData("Write-Output (Get-Content -Path $Path)")]
    [InlineData("Get-ChildItem -Name")]
    [InlineData("Get-ChildItem | Out-Null")]
    [InlineData("1 | Get-ChildItem")]
    [InlineData("Get-ChildItem > owned.txt")]
    [InlineData("1 | ForEach-Object { Get-ChildItem }")]
    [InlineData("& Get-ChildItem")]
    public void PackagedPathPolicy_RejectsUnselectedAndNestedBindingsBeforeBuild(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        Assert.Throws<InvalidOperationException>(() => PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void PackagedPathPolicy_ReusesExactNativeSourceAndLeavesOrdinaryModulesUnguarded()
    {
        const string source = "function Read-File { param([string]$Path) Get-Content -Path $Path -Raw }";
        using var fixture = ArtifactFixture.Create(source);
        var module = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Owned", "Module", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(module.Methods);
        Assert.DoesNotContain("new int[]", module.SourceCode);
        var executable = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Owned", "Executable", "net10.0", PowerShellCompilationCapabilities.HybridExecutable,
            guardedExecutableEntryPath: fixture.ScriptPath);
        var method = Assert.Single(executable.Methods);
        Assert.NotNull(method.NativeFunctionBinding);
        Assert.Contains("Get-Content -Path $Path -Raw", executable.SourceCode);
        Assert.Contains("new int[]", executable.SourceCode);
        Assert.Equal(source, File.ReadAllText(fixture.ScriptPath));
        var retained = PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath);
        Assert.Contains("FileSystemPathGuard]::Validate(($Path), $ExecutionContext.SessionState)", retained);
        Assert.DoesNotContain("Validate(($Path -Raw)", retained);
    }

    [Fact]
    public void PackagedPathPolicy_DoesNotInferAnUnrelatedFunctionAssignmentAsTheHelperOperand()
    {
        using var fixture = ArtifactFixture.Create("function Set-Other { $Path='Variable:Host' }; function Read-File { param([string]$Path) Get-Content -Path $Path }");
        var retained = PowerShellPackagedScriptRewriter.Rewrite(fixture.ScriptPath);
        Assert.Contains("FileSystemPathGuard]::Validate(($Path), $ExecutionContext.SessionState)", retained);
    }

    [Fact]
    public void PackagedPathPolicy_LeavesOrdinaryModuleImplicitReadsUnguarded()
    {
        using var fixture = ArtifactFixture.Create("function List-Owned { Get-ChildItem }");
        var module = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Owned", "Module", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(module.Methods);
        Assert.DoesNotContain("new int[]", module.SourceCode);
        var executable = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Owned", "Executable", "net10.0", PowerShellCompilationCapabilities.HybridExecutable,
            guardedExecutableEntryPath: fixture.ScriptPath);
        Assert.Single(executable.Methods);
        Assert.Contains("new int[]", executable.SourceCode);
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_RetainedPackageGuardsTheSelectedFilesystemOperand()
    {
        using var fixture = ArtifactFixture.Create("""
            param([switch]$Unsafe,[switch]$Implicit,[string]$Operand='String')
            $ErrorActionPreference='Stop'
            function Read-File { param([object]$Path) Get-Content -LiteralPath:$Path -Raw }
            if($Implicit){
                if($Unsafe){Set-Location Variable:}else{Set-Location ([IO.Path]::Combine($PSScriptRoot,'implicit'))}
                try{$items=Get-ChildItem; "implicit:$($items.Name):count:$(@($items).Count)"}catch{'guarded-refusal'}
                return
            }
            $path=if($Unsafe){'Variable:Host'}else{[IO.Path]::Combine($PSScriptRoot,'data.txt')}
            if($Operand -eq 'FileInfo'){$path=Get-Item -LiteralPath $path}
            elseif($Operand -eq 'PathInfo'){$path=Resolve-Path -LiteralPath $path}
            try{ $value=Read-File -Path $path; "value:$($value.Trim())" }catch{'guarded-refusal'}
            """, compactPath: true);
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "retained-file");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "implicit"));
        File.WriteAllText(Path.Combine(fixture.RootPath, "implicit", "one.txt"), "implicit-file");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Owned.RetainedPath", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package)
        { SingleFile = false, IncludeResource = new[] { "data.txt", "implicit/one.txt" } };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var positive = RunProcess(result.ArtifactPath!);
        Assert.Equal(0, positive.ExitCode); Assert.Contains("value:retained-file", positive.StandardOutput);
        foreach (var operand in new[] { "FileInfo", "PathInfo" })
        {
            var original = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Operand", operand);
            var generated = RunProcess(result.ArtifactPath!, "-Operand", operand);
            Assert.Equal(0, original.ExitCode); Assert.Equal(0, generated.ExitCode);
            Assert.Empty(generated.StandardError);
            Assert.Equal(original.StandardOutput.Trim(), generated.StandardOutput.Trim());
            Assert.Contains("value:retained-file", generated.StandardOutput);
        }
        var negative = RunProcess(result.ArtifactPath!, "-Unsafe");
        Assert.Equal(0, negative.ExitCode); Assert.Contains("guarded-refusal", negative.StandardOutput);
        Assert.Empty(negative.StandardError);
        var unsafePathInfo = RunProcess(result.ArtifactPath!, "-Unsafe", "-Operand", "PathInfo");
        Assert.Equal(0, unsafePathInfo.ExitCode);
        Assert.Contains("guarded-refusal", unsafePathInfo.StandardOutput);
        Assert.Empty(unsafePathInfo.StandardError);
        var implicitRead = RunProcess(result.ArtifactPath!, "-Implicit");
        Assert.Equal(0, implicitRead.ExitCode); Assert.Contains("implicit:one.txt:count:1", implicitRead.StandardOutput);
        var implicitUnsafe = RunProcess(result.ArtifactPath!, "-Implicit", "-Unsafe");
        Assert.Equal(0, implicitUnsafe.ExitCode); Assert.Contains("guarded-refusal", implicitUnsafe.StandardOutput);
        Assert.DoesNotContain("implicit:", implicitUnsafe.StandardOutput);
        Assert.All(result.Manifest!.Dependencies, static dependency =>
            Assert.DoesNotContain("Native script-root reads", dependency.Note));
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativePathOperandsPreserveLookupOrderAndRefuseVariableProviders()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string]$Case='File')
            $ErrorActionPreference='Stop'
            $script:trace=New-Object 'Collections.Generic.List[string]'
            function Join-Path {
                param([string]$Child)
                $script:trace.Add('path')
                if($Case -eq 'Unsafe'){ 'Variable:Host' }
                elseif($Case -eq 'Qualified'){ 'Microsoft.PowerShell.Core\Variable::MyInvocation' }
                elseif($Case -eq 'Relative'){ 'Host' }
                else { [IO.Path]::Combine($PSScriptRoot,$Child) }
            }
            function Get-Encoding { $script:trace.Add('encoding'); 'utf8' }
            function Get-Content {
                param([object]$Path,[string]$Encoding='utf8',[switch]$Raw)
                if($Case -like 'Shadow*'){ "shadow:$($Path.GetType().FullName):${Encoding}:$($Path.Count):same:$([object]::ReferenceEquals($Path,$script:pathOperand))" }
                else{ Microsoft.PowerShell.Management\Get-Content -Path $Path -Encoding $Encoding -Raw:$Raw }
            }
            function Read-Owned { param([string]$Path) Microsoft.PowerShell.Management\Get-Content -LiteralPath:$Path -Raw }
            function Get-ChildItem {
                if($Case -eq 'ImplicitShadow'){ "implicit-shadow:caller:$implicitCaller" }
                elseif($Case -eq 'ImplicitUnsafe'){$script:trace.Add('dispatch'); 'unexpected-dispatch'}
                else{Microsoft.PowerShell.Management\Get-ChildItem}
            }
            function List-Owned {
                $implicitCaller='owned-caller'
                $items=Get-ChildItem
                foreach($item in $items){if($Case -eq 'ImplicitNative'){$item.Name}else{$item}}
            }
            if($Case -eq 'ResourceRoot'){
                $PSScriptRoot
            }elseif($Case -eq 'Helper'){
                $result=Read-Owned -Path (Join-Path 'data.txt'); "result:$($result.Trim())"
            }elseif($Case -eq 'Array'){
                $paths=@([IO.Path]::Combine($PSScriptRoot,'data.txt'),[IO.Path]::Combine($PSScriptRoot,'second.txt'))
                $values=Microsoft.PowerShell.Management\Get-Content -Path $paths
                "values:$($values -join ','):type:$($values.GetType().FullName)"
            }elseif($Case -like 'PathValue*'){
                $file=[IO.Path]::Combine($PSScriptRoot,'data.txt')
                if($Case -eq 'PathValueFileInfo'){$paths=Microsoft.PowerShell.Management\Get-Item -LiteralPath $file}
                elseif($Case -eq 'PathValuePathInfo'){$paths=Resolve-Path -LiteralPath $file}
                elseif($Case -eq 'PathValueWrapped'){$paths=[psobject](Resolve-Path -LiteralPath $file)}
                else{$paths=@((Resolve-Path -LiteralPath $file),(Microsoft.PowerShell.Management\Get-Item -LiteralPath $file))}
                $values=Microsoft.PowerShell.Management\Get-Content -LiteralPath $paths
                "values:$($values -join ',')"
            }elseif($Case -like 'UnsafePathValue*'){
                $paths=Resolve-Path -LiteralPath 'Variable:Host'
                if($Case -eq 'UnsafePathValueWrapped'){$paths=[psobject]$paths}
                elseif($Case -eq 'UnsafePathValueArray'){$paths=@([IO.Path]::Combine($PSScriptRoot,'data.txt'),$paths)}
                try{Microsoft.PowerShell.Management\Get-Content -LiteralPath $paths; 'unexpected provider read'}catch{'guarded-refusal'}
            }elseif($Case -eq 'CustomConversion'){
                $script:conversionCalls=0
                $paths=[pscustomobject]@{}
                Add-Member -InputObject $paths -MemberType ScriptMethod -Name ToString -Value {
                    $script:conversionCalls++
                    if($script:conversionCalls -eq 1){[IO.Path]::Combine($PSScriptRoot,'data.txt')}else{'Variable:Host'}
                } -Force
                try{Microsoft.PowerShell.Management\Get-Content -LiteralPath $paths; 'unexpected conversion read'}catch{'guarded-refusal'}
                "conversions:$script:conversionCalls"
            }elseif($Case -like 'EmptyArray*'){
                if($Case -eq 'EmptyArrayUnsafe'){Set-Location Variable:}
                else{Set-Location ([IO.Path]::Combine($PSScriptRoot,'implicit'))}
                $paths=[string[]]@()
                try{
                    $items=Microsoft.PowerShell.Management\Get-ChildItem -Path $paths
                    "empty-array:$($items.Name):count:$(@($items).Count)"
                }catch{'guarded-refusal'}
            }elseif($Case -like 'Implicit*'){
                if($Case -eq 'ImplicitUnsafe'){Set-Location Variable:}
                else{Set-Location ([IO.Path]::Combine($PSScriptRoot,'implicit'))}
                try{List-Owned}catch{'guarded-refusal'}
            }elseif($Case -like 'Shadow*'){
                $paths=switch($Case){
                    'ShadowScalar'{[IO.Path]::Combine($PSScriptRoot,'data.txt')}
                    'ShadowOne'{[string[]]@("$PSScriptRoot/data.txt")}
                    'ShadowEmpty'{[string[]]@()}
                    'ShadowWrapped'{[psobject][string[]]@("$PSScriptRoot/data.txt")}
                    'ShadowFileInfo'{Microsoft.PowerShell.Management\Get-Item -LiteralPath ([IO.Path]::Combine($PSScriptRoot,'data.txt'))}
                    'ShadowPathInfo'{Resolve-Path -LiteralPath ([IO.Path]::Combine($PSScriptRoot,'data.txt'))}
                    'ShadowDirectoryInfo'{Microsoft.PowerShell.Management\Get-Item -LiteralPath ([IO.Path]::Combine($PSScriptRoot,'implicit'))}
                    default{[object[]]@([IO.Path]::Combine($PSScriptRoot,'data.txt'),[IO.Path]::Combine($PSScriptRoot,'second.txt'))}
                }
                # Switch pipelines can enumerate their output; use explicit shapes for these contracts.
                if($Case -eq 'ShadowOne'){$paths=[string[]]@("$PSScriptRoot/data.txt")}
                if($Case -eq 'ShadowEmpty'){$paths=[string[]]@()}
                if($Case -eq 'ShadowWrapped'){$paths=[psobject][string[]]@("$PSScriptRoot/data.txt")}
                $script:pathOperand=$paths
                Get-Content -Path $paths -Encoding (Get-Encoding)
            }elseif($Case -eq 'LiteralRelative'){
                Set-Location Variable:
                try{Get-Content -Path 'Host'; 'unexpected provider read'}catch{'guarded-refusal'}
            }elseif($Case -in @('Null','Empty','Whitespace','MissingArrayMember')){
                $path=switch($Case){'Null'{$null};'Empty'{''};'Whitespace'{' '};'MissingArrayMember'{@([IO.Path]::Combine($PSScriptRoot,'data.txt'),[IO.Path]::Combine($PSScriptRoot,'absent.txt'))}}
                try{ Microsoft.PowerShell.Management\Get-Content -LiteralPath $path -ErrorAction Stop }
                catch{ "error:$($_.FullyQualifiedErrorId):$($_.CategoryInfo.Category):$($_.Exception.GetType().FullName):$($_.Exception.Message.Replace($PSScriptRoot,'<SOURCE>'))" }
            }else{
                if($Case -eq 'Relative'){Set-Location Variable:}
                try{ $value=Get-Content -Path (Join-Path 'data.txt') -Encoding (Get-Encoding) -Raw; "value:$($value.Trim())" }
                catch{ 'guarded-refusal' }
            }
            "trace:$($script:trace -join ',')"
            """, compactPath: true);
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "alpha");
        File.WriteAllText(Path.Combine(fixture.RootPath, "second.txt"), "beta");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "implicit"));
        File.WriteAllText(Path.Combine(fixture.RootPath, "implicit", "one.txt"), "implicit-file");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Owned.PathOperands", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, IncludeResource = new[] { "data.txt", "second.txt", "implicit/one.txt" },
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var root = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            static entry => entry.Kind == PowerShellCompilationUnitKind.Script);
        Assert.True(root.Emitted);
        Assert.True(root.UsesNativeFunctionBinding);
        Assert.False(root.RetainedHostedSource);
        Assert.Equal(File.ReadAllBytes(fixture.ScriptPath), File.ReadAllBytes(Path.Combine(result.GeneratedSourcePath!, "Source.ps1")));
        var resources = result.Manifest.Dependencies.Where(static dependency =>
            dependency.Selection == PowerShellCompilationDependencySelection.ExplicitInclude).ToArray();
        Assert.Equal(3, resources.Length);
        Assert.All(resources, dependency =>
        {
            Assert.Equal(PowerShellCompilationDependencyDisposition.CopiedAdjacent, dependency.Disposition);
            Assert.Contains("immutable embedded/extracted source-layout snapshot", dependency.Note);
            Assert.Contains("editing it does not update", dependency.Note);
            Assert.Equal(File.ReadAllBytes(dependency.SourcePath!), File.ReadAllBytes(Path.Combine(
                Path.GetDirectoryName(result.ArtifactPath!)!, dependency.RelativePath)));
        });
        var stagedResources = Directory.GetFiles(Path.Combine(result.GeneratedSourcePath!, "EmbeddedDependencies"), "Resource*.bin");
        Assert.Equal(3, stagedResources.Length);
        var program = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "Program.cs"));
        foreach (var resource in stagedResources)
        {
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(resource))).ToLowerInvariant();
            Assert.Contains(sha, program);
        }
        foreach (var item in new[] { ("File", "value:alpha", "trace:path,encoding"), ("Helper", "result:alpha", "trace:path"),
            ("Array", "values:alpha,beta:type:System.Object[]", "trace:"), ("Shadow", "shadow:System.Object[]:utf8:2:same:True", "trace:encoding"),
            ("ShadowScalar", "shadow:System.String:utf8:1:same:True", "trace:encoding"),
            ("ShadowOne", "shadow:System.String[]:utf8:1:same:True", "trace:encoding"),
            ("ShadowEmpty", "shadow:System.String[]:utf8:0:same:True", "trace:encoding"),
            ("ShadowWrapped", "shadow:System.String[]:utf8:1:same:True", "trace:encoding"),
            ("ShadowFileInfo", "shadow:System.IO.FileInfo:utf8:1:same:True", "trace:encoding"),
            ("ShadowPathInfo", "shadow:System.Management.Automation.PathInfo:utf8:1:same:True", "trace:encoding"),
            ("ShadowDirectoryInfo", "shadow:System.IO.DirectoryInfo:utf8:1:same:True", "trace:encoding"),
            ("PathValueFileInfo", "values:alpha", "trace:"), ("PathValuePathInfo", "values:alpha", "trace:"),
            ("PathValueWrapped", "values:alpha", "trace:"), ("PathValueArray", "values:alpha,alpha", "trace:"),
            ("EmptyArrayFileSystem", "empty-array:one.txt:count:1", "trace:") })
        {
            var original = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Case", item.Item1);
            var generated = RunProcess(result.ArtifactPath!, "-Case", item.Item1);
            Assert.Equal(0, original.ExitCode); Assert.Equal(0, generated.ExitCode);
            Assert.Equal(original.StandardOutput.Trim(), generated.StandardOutput.Trim());
            Assert.Contains(item.Item2, generated.StandardOutput); Assert.Contains(item.Item3, generated.StandardOutput);
        }
        foreach (var item in new[] { "Null", "Empty", "Whitespace", "MissingArrayMember" })
        {
            var original = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Case", item);
            var generated = RunProcess(result.ArtifactPath!, "-Case", item);
            Assert.Equal(0, original.ExitCode); Assert.Equal(0, generated.ExitCode);
            Assert.Equal(original.StandardOutput.Trim(), generated.StandardOutput.Trim());
            Assert.Contains("error:", generated.StandardOutput);
        }
        foreach (var item in new[] { "ImplicitNative", "ImplicitShadow" })
        {
            var original = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Case", item);
            var generated = RunProcess(result.ArtifactPath!, "-Case", item);
            Assert.Equal(0, original.ExitCode); Assert.Equal(0, generated.ExitCode);
            Assert.Equal(original.StandardOutput.Trim(), generated.StandardOutput.Trim());
            Assert.Contains(item == "ImplicitNative" ? "one.txt" : "implicit-shadow:caller:owned-caller", generated.StandardOutput);
        }
        var resourceRoot = RunProcess(result.ArtifactPath!, "-Case", "ResourceRoot");
        Assert.Equal(0, resourceRoot.ExitCode);
        var extractedRoot = resourceRoot.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        var extractedData = Path.Combine(extractedRoot, "data.txt");
        Assert.Equal("alpha", File.ReadAllText(extractedData));
        Assert.Equal("implicit-file", File.ReadAllText(Path.Combine(extractedRoot, "implicit", "one.txt")));
        var adjacentData = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "data.txt");
        File.WriteAllText(adjacentData, "adjacent-edit");
        var snapshotRead = RunProcess(result.ArtifactPath!, "-Case", "File");
        Assert.Equal(0, snapshotRead.ExitCode);
        Assert.Contains("value:alpha", snapshotRead.StandardOutput);
        File.WriteAllText(adjacentData, "alpha");
        File.WriteAllText(extractedData, "corrupt-cache");
        var restoredRead = RunProcess(result.ArtifactPath!, "-Case", "File");
        Assert.Equal(0, restoredRead.ExitCode);
        Assert.Contains("value:alpha", restoredRead.StandardOutput);
        Assert.Equal("alpha", File.ReadAllText(extractedData));
        foreach (var item in new[] { "Unsafe", "Qualified", "Relative" })
        {
            var generated = RunProcess(result.ArtifactPath!, "-Case", item);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("guarded-refusal", generated.StandardOutput);
            Assert.Contains("trace:path", generated.StandardOutput);
            Assert.DoesNotContain("encoding", generated.StandardOutput);
        }
        var literalRelative = RunProcess(result.ArtifactPath!, "-Case", "LiteralRelative");
        Assert.Equal(0, literalRelative.ExitCode);
        Assert.Contains("guarded-refusal", literalRelative.StandardOutput);
        Assert.DoesNotContain("unexpected provider read", literalRelative.StandardOutput);
        var implicitUnsafe = RunProcess(result.ArtifactPath!, "-Case", "ImplicitUnsafe");
        Assert.Equal(0, implicitUnsafe.ExitCode);
        Assert.Contains("guarded-refusal", implicitUnsafe.StandardOutput);
        Assert.DoesNotContain("dispatch", implicitUnsafe.StandardOutput);
        foreach (var item in new[] { "UnsafePathValue", "UnsafePathValueWrapped", "UnsafePathValueArray" })
        {
            var generated = RunProcess(result.ArtifactPath!, "-Case", item);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("guarded-refusal", generated.StandardOutput);
            Assert.DoesNotContain("unexpected provider read", generated.StandardOutput);
            Assert.Empty(generated.StandardError);
        }
        var customConversion = RunProcess(result.ArtifactPath!, "-Case", "CustomConversion");
        Assert.Equal(0, customConversion.ExitCode);
        Assert.Contains("guarded-refusal", customConversion.StandardOutput);
        Assert.Contains("conversions:0", customConversion.StandardOutput);
        Assert.DoesNotContain("unexpected conversion read", customConversion.StandardOutput);
        Assert.Empty(customConversion.StandardError);
        var emptyArrayUnsafe = RunProcess(result.ArtifactPath!, "-Case", "EmptyArrayUnsafe");
        Assert.Equal(0, emptyArrayUnsafe.ExitCode);
        Assert.Contains("guarded-refusal", emptyArrayUnsafe.StandardOutput);
        Assert.DoesNotContain("empty-array:", emptyArrayUnsafe.StandardOutput);
        Assert.Empty(emptyArrayUnsafe.StandardError);
    }
}
