using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderScriptExportExpressionTests
{
    [Theory]
    [InlineData(ArtefactType.Script, "$ignored = Export-ModuleMember -Function Get-Value; $script:Tail = 'assignment'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = Export-ModuleMember -Function Get-Value; $script:Tail = 'assignment'")]
    [InlineData(ArtefactType.Script, "$ignored = (Microsoft.PowerShell.Core\\Export-ModuleMember -Function Get-Value); $script:Tail = 'parenthesized'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = (Microsoft.PowerShell.Core\\Export-ModuleMember -Function Get-Value); $script:Tail = 'parenthesized'")]
    [InlineData(ArtefactType.Script, "$ignored = $(Export-ModuleMember -Function Get-Value); $script:Tail = 'subexpression'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = $(Export-ModuleMember -Function Get-Value); $script:Tail = 'subexpression'")]
    [InlineData(ArtefactType.Script, "$ignored = @((Export-ModuleMember -Function Get-Value)); $script:Tail = 'array-expression'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = @((Export-ModuleMember -Function Get-Value)); $script:Tail = 'array-expression'")]
    [InlineData(ArtefactType.Script, "$ignored = & Export-ModuleMember -Function Get-Value; $script:Tail = 'call-operator'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = & Export-ModuleMember -Function Get-Value; $script:Tail = 'call-operator'")]
    [InlineData(ArtefactType.Script, "$ignored = & 'Export-ModuleMember' -Function Get-Value; $script:Tail = 'quoted-call-operator'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = & \"Microsoft.PowerShell.Core\\Export-ModuleMember\" -Function Get-Value; $script:Tail = 'quoted-call-operator'")]
    [InlineData(ArtefactType.Script, "$ignored = & ('Export-ModuleMember') -Function Get-Value; $script:Tail = 'parenthesized-quoted-call-operator'")]
    [InlineData(ArtefactType.ScriptPacked, "$ignored = & (\"Microsoft.PowerShell.Core\\Export-ModuleMember\") -Function Get-Value; $script:Tail = 'parenthesized-quoted-call-operator'")]
    [InlineData(ArtefactType.Script, "return Export-ModuleMember -Function Get-Value; $script:Tail = 'return-expression'")]
    [InlineData(ArtefactType.ScriptPacked, "return Export-ModuleMember -Function Get-Value; $script:Tail = 'return-expression'")]
    [InlineData(ArtefactType.Script, "Write-Output ready && & 'Export-ModuleMember' -Function Get-Value; $script:Tail = 'chained-call-operator'")]
    [InlineData(ArtefactType.ScriptPacked, "Write-Output ready && & 'Export-ModuleMember' -Function Get-Value; $script:Tail = 'chained-call-operator'")]
    public void Build_RemovesExportInvocationsWithoutBreakingExpressionContexts(
        ArtefactType artefactType,
        string invocation)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ExpressionExportModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'ExpressionExportModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "function Get-Value { 'ok' }" + Environment.NewLine + invocation);

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                inspectionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$script:Tail", script, StringComparison.Ordinal);
            System.Management.Automation.Language.Parser.ParseInput(script, out _, out var parseErrors);
            Assert.Empty(parseErrors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "&&")]
    [InlineData(ArtefactType.Script, "||")]
    [InlineData(ArtefactType.ScriptPacked, "&&")]
    [InlineData(ArtefactType.ScriptPacked, "||")]
    public void Build_RemovesExportInvocationAfterPipelineChainWithoutBreakingScript(
        ArtefactType artefactType,
        string chainOperator)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "PipelineChainExportModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'PipelineChainExportModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "function Get-Value { 'ok' }" + Environment.NewLine +
                $"Write-Output ready {chainOperator} Export-ModuleMember -Function Get-Value " +
                $"{(chainOperator == "&&" ? "||" : "&&")} Write-Output fallback; $script:Tail = 'preserved'");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                inspectionRoot = Path.Combine(root.FullName, "extracted-chain");
                ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                $"Write-Output ready {chainOperator} $null {(chainOperator == "&&" ? "||" : "&&")} Write-Output fallback",
                script,
                StringComparison.Ordinal);
            Assert.Contains("$script:Tail = 'preserved'", script, StringComparison.Ordinal);
            System.Management.Automation.Language.Parser.ParseInput(script, out _, out var parseErrors);
            Assert.Empty(parseErrors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script, "&&", "")]
    [InlineData(ArtefactType.Script, "||", "")]
    [InlineData(ArtefactType.ScriptPacked, "&&", "")]
    [InlineData(ArtefactType.ScriptPacked, "||", "")]
    [InlineData(ArtefactType.Script, "&&", " # chain comment")]
    [InlineData(ArtefactType.Script, "||", " # chain comment")]
    [InlineData(ArtefactType.ScriptPacked, "&&", " # chain comment")]
    [InlineData(ArtefactType.ScriptPacked, "||", " # chain comment")]
    public void Build_RemovesExportInvocationOnNextPipelineChainLineWithoutBreakingScript(
        ArtefactType artefactType,
        string chainOperator,
        string trailingText)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "MultilinePipelineChainExportModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psd1"),
                "@{ RootModule = 'MultilinePipelineChainExportModule.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(
                Path.Combine(stagingRoot, moduleName + ".psm1"),
                "function Get-Value { 'ok' }" + Environment.NewLine +
                $"Write-Output ready {chainOperator}{trailingText}" + Environment.NewLine +
                "Export-ModuleMember -Function Get-Value" + Environment.NewLine +
                "$script:Tail = 'preserved'");

            ArtefactBuildResult result = Build(
                root.FullName,
                stagingRoot,
                Path.Combine(root.FullName, "output"),
                moduleName,
                artefactType);

            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                inspectionRoot = Path.Combine(root.FullName, "extracted-multiline-chain");
                ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);
            }

            string script = File.ReadAllText(Path.Combine(inspectionRoot, moduleName + ".ps1"));
            Assert.DoesNotContain("Export-ModuleMember", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                $"Write-Output ready {chainOperator}{trailingText}{Environment.NewLine}$null",
                script,
                StringComparison.Ordinal);
            Assert.Contains("$script:Tail = 'preserved'", script, StringComparison.Ordinal);
            System.Management.Automation.Language.Parser.ParseInput(script, out _, out var parseErrors);
            Assert.Empty(parseErrors);
        }
        finally
        {
            Delete(root);
        }
    }

    private static ArtefactBuildResult Build(
        string projectRoot,
        string stagingRoot,
        string outputRoot,
        string moduleName,
        ArtefactType artefactType)
        => new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
            new ConfigurationArtefactSegment
            {
                ArtefactType = artefactType,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = outputRoot,
                    ArtefactName = moduleName + ".zip"
                }
            },
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            Array.Empty<RequiredModuleReference>(),
            finalizePackedArtefact: null);

    private static DirectoryInfo CreateRoot()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));

    private static void Delete(DirectoryInfo root)
    {
        try { root.Delete(recursive: true); } catch { /* best effort */ }
    }
}
