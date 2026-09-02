using System.Management.Automation.Language;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCommandSemanticResolverTests
{
    [Theory]
    [InlineData("Get-Date", "Microsoft.PowerShell.Utility\\Get-Date")]
    [InlineData("Write-Output 1", "Microsoft.PowerShell.Utility\\Write-Output 1")]
    [InlineData("Get-Command Get-Date", "Microsoft.PowerShell.Core\\Get-Command Get-Date")]
    [InlineData("Test-Path -LiteralPath 'FileSystem::proof'", "Microsoft.PowerShell.Management\\Test-Path -LiteralPath 'FileSystem::proof'")]
    [InlineData("New-Object System.Version", "Microsoft.PowerShell.Utility\\New-Object System.Version")]
    [InlineData("Add-Member -NotePropertyName Name -NotePropertyValue Value", "Microsoft.PowerShell.Utility\\Add-Member -NotePropertyName Name -NotePropertyValue Value")]
    [InlineData("Where-Object { $true }", "Microsoft.PowerShell.Core\\Where-Object { $true }")]
    public void PowerShellHostRequiresCanonicalModuleQualifiedProviderCommand(
        string unqualified,
        string qualified)
    {
        var resolver = new PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry.Default);

        var runtime = resolver.Resolve(ParseCommand(unqualified), localFunctionNames: null, PowerShellCompilationCapabilities.BinaryModule);
        var provider = resolver.Resolve(ParseCommand(qualified), localFunctionNames: null, PowerShellCompilationCapabilities.BinaryModule);

        Assert.Equal(PowerShellCommandSemanticOrigin.PowerShellRuntime, runtime.Origin);
        Assert.NotNull(runtime.Contract);
        Assert.Equal(PowerShellCommandSemanticOrigin.ProviderQualified, provider.Origin);
        Assert.Equal(runtime.Contract!.ProviderId, provider.Contract!.ProviderId);
    }

    [Fact]
    public void RuntimeFreeTargetPrefersLocalFunctionBeforeUnqualifiedProvider()
    {
        var resolver = new PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry.Default);
        var command = ParseCommand("Get-Date");

        var local = resolver.Resolve(
            command,
            new HashSet<string>(new[] { "Get-Date" }, StringComparer.OrdinalIgnoreCase),
            PowerShellCompilationCapabilities.TypedExecutable);
        var provider = resolver.Resolve(
            command,
            localFunctionNames: null,
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Equal(PowerShellCommandSemanticOrigin.LocalFunction, local.Origin);
        Assert.Null(local.Contract);
        Assert.Equal(PowerShellCommandSemanticOrigin.ProviderUnqualified, provider.Origin);
        Assert.Equal("powerforge.command.runtime-state.get-date", provider.Contract!.ProviderId);
    }

    [Theory]
    [InlineData("return Get-Date")]
    [InlineData("$value = Get-Date; return $value")]
    [InlineData("[datetime] $value = Get-Date; return $value")]
    public void BinaryModuleNeverLowersUnqualifiedGetDateAcrossExpressionShapes(string body)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Proof {{ {body} }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandResolution", "unqualified-get-date.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.DoesNotContain(
            result.Emitted.Methods.SelectMany(static method => method.CommandProviders),
            static provider => provider.ProviderId == "powerforge.command.runtime-state.get-date");
        Assert.True(
            result.Emitted.Diagnostics.Any(static diagnostic =>
                diagnostic.Message.Contains("preserve PowerShell runtime", StringComparison.Ordinal)) ||
            result.Emitted.Methods.Any(static method => method.RequiresPowerShellCommandRegions));
    }

    [Theory]
    [InlineData("return Microsoft.PowerShell.Utility\\Get-Date")]
    [InlineData("$value = Microsoft.PowerShell.Utility\\Get-Date; return $value")]
    [InlineData("[datetime] $value = Microsoft.PowerShell.Utility\\Get-Date; return $value")]
    public void BinaryModuleLowersQualifiedGetDateAcrossExpressionShapes(string body)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Proof {{ {body} }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandResolution", "qualified-get-date.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var method = Assert.Single(result.Emitted.Methods);
        Assert.Equal("powerforge.command.runtime-state.get-date", Assert.Single(method.CommandProviders).ProviderId);
        Assert.False(method.RequiresPowerShellCommandRegions);
    }

    [Fact]
    public void LocalFunctionShadowUsesOneBindingAndInferenceDecision()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Date { return 'custom' }; function Get-Proof { $value = Get-Date; return $value.Length }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandResolution", "local-shadow.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.DoesNotContain(
            result.Emitted.Methods.SelectMany(static method => method.CommandProviders),
            static provider => provider.ProviderId == "powerforge.command.runtime-state.get-date");
        Assert.Contains(result.Emitted.Methods, static method => method.GeneratedName == "Get_Proof");
    }

    [Fact]
    public void ModuleQualifiedAliasRemainsRuntimeResolved()
    {
        var resolver = new PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry.Default);

        var resolution = resolver.Resolve(
            ParseCommand("Microsoft.PowerShell.Utility\\select -First 1"),
            localFunctionNames: null,
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Equal(PowerShellCommandSemanticOrigin.PowerShellRuntime, resolution.Origin);
        Assert.Equal("powerforge.command.projection.select-object", resolution.Contract!.ProviderId);
    }

    private static CommandAst ParseCommand(string invocation)
    {
        var ast = Parser.ParseInput(invocation, out _, out var errors);
        Assert.Empty(errors);
        return Assert.Single(ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>());
    }
}
