using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Compile_NativeScriptRootMatchesOriginalBindingAndExecution(string framework, string host)
    {
        var fixtureProject = FindStatementErrorFixtureProject();
        var fixtureRoot = System.IO.Path.GetDirectoryName(fixtureProject)!;
        var probePath = System.IO.Path.Combine(fixtureRoot, "NativeScriptEntryProbe.ps1");
        var probeAst = System.Management.Automation.Language.Parser.ParseFile(probePath, out _, out _);
        var source = probeAst.FindAll(static node => node is System.Management.Automation.Language.StringConstantExpressionAst,
                searchNestedScriptBlocks: true).OfType<System.Management.Automation.Language.StringConstantExpressionAst>()
            .Single(static literal => literal.Value.StartsWith("param([ValidateScript", System.StringComparison.Ordinal)).Value;
        using var fixture = ArtifactFixture.Create(source);
        var profile = framework == "net472"
            ? PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId
            : PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: framework,
            capabilities: PowerShellCompilationCapabilities.HybridExecutable));
        var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(fixture.ScriptPath, plan, framework, profile);
        var method = compiled.EntryPointMethod;
        Assert.NotNull(method.NativeFunctionBinding);
        var generated = new System.Text.StringBuilder("#nullable enable\nnamespace Generic.Compiler.StatementErrors;\npublic static class CompiledRoot {\n")
            .AppendLine(method.Source)
            .AppendLine("public static global::System.Management.Automation.ExternalScriptInfo Create(string path) => NativeScriptEntryFixture.Create(path, context => {")
            .AppendLine("var clause = 2;");
        PowerShellNativeCallbackSource.AppendBody(generated, "    ", method.GeneratedName, "<script>",
            method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping,
            method.RequiresPowerShellStreams, method.ReturnType == typeof(void), method.ReturnType.IsArray);
        generated.AppendLine("});\n}");
        var projectRoot = System.IO.Path.GetDirectoryName(fixture.ScriptPath)!;
        var repositoryRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(fixtureRoot, "..", "..", ".."));
        var project = System.IO.Path.Combine(projectRoot, "NativeRoot.csproj");
        System.IO.File.WriteAllText(project, System.IO.File.ReadAllText(fixtureProject)
            .Replace("..\\..\\..\\PowerForge.PowerShell", System.IO.Path.Combine(repositoryRoot, "PowerForge.PowerShell"), System.StringComparison.Ordinal));
        System.IO.File.WriteAllText(System.IO.Path.Combine(projectRoot, "CompiledRoot.cs"), generated.ToString());
        System.IO.File.Copy(System.IO.Path.Combine(fixtureRoot, "NativeScriptEntryFixture.cs"),
            System.IO.Path.Combine(projectRoot, "NativeScriptEntryFixture.cs"));
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var assembly = System.IO.Path.Combine(projectRoot, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        var result = RunProcess(host, "-NoProfile", "-NonInteractive", "-File", probePath,
            "-Assembly", assembly, "-Factory", "Generic.Compiler.StatementErrors.CompiledRoot");
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.Contains("Native script entry scope/binding comparison passed: 4 cases plus binding rejection.", result.StandardOutput);
    }

    [Fact]
    public void Compile_NativeScriptRootUsesAuthoredStorageAndSourcePositions()
    {
        using var fixture = ArtifactFixture.Create("param([string] $Value = 'original')\n$Value = 'changed'\nWrite-Output $Value\n$args.Count");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable));
        var compiled = PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(fixture.ScriptPath,
            plan, "net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        var method = compiled.EntryPointMethod;
        Assert.NotNull(method.NativeFunctionBinding);
        Assert.Contains("PowerShellNativeFunctionContext __nativeFunction", method.Source);
        Assert.DoesNotContain(".powerforge-entry.ps1", method.Source);
        Assert.Contains(PowerShellCSharpLiteral.QuoteString(System.IO.File.ReadAllText(fixture.ScriptPath)), method.Source);
        Assert.All(method.SourceMap, span => Assert.InRange(span.SourceStartLine, 2, 4));
        Assert.Contains(method.SourceMap, span => span.SourceStartLine == 3);
        Assert.Equal(PowerShellSourceParser.ParseFile(fixture.ScriptPath).DocumentId, method.SourceSpan.DocumentId);
    }

    [Theory]
    [InlineData("function Helper { 1 }\nHelper")]
    [InlineData("enum Kind { One }\n1")]
    [InlineData("#requires -Version 7.0\n1")]
    public void Compile_NativeScriptRootKeepsUnqualifiedDeclarationOwnersClosed(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable));
        var error = Assert.Throws<System.InvalidOperationException>(() =>
            PowerShellTypedExecutableCompiler.CompileHybridNativeEntry(fixture.ScriptPath,
                plan, "net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId));
        Assert.Contains("without declarations, requirements or dependencies", error.Message);
    }
}
