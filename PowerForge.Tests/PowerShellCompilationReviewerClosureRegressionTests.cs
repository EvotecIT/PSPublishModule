namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Theory]
    [InlineData("return $ExecutionContext.SessionState.PSVariable.GetValue('Host').Name")]
    [InlineData("return $ExecutionContext.SessionState.PSVariable.Get('Host').Value.Name")]
    [InlineData("$name = 'Host'; return $ExecutionContext.SessionState.PSVariable.GetValue($name).Name")]
    [InlineData("return (Get-Variable Host -ValueOnly).Name")]
    [InlineData("return (Get-Item Variable:Host).Value.Name")]
    [InlineData("$path = 'Variable:Host'; return (Get-Item $path).Value.Name")]
    public void Build_PackagedExecutableRejectsIndirectHostRetrieval(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.IndirectHost", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        var error = result.Error ?? string.Empty;
        Assert.True(
            error.Contains("PSHost", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("invocation metadata", StringComparison.OrdinalIgnoreCase),
            error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridBinaryModuleRoutesDynamicVariableProviderPathToFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DynamicLocal { [int] $x = 1; [string] $path = 'Variable:x'; " +
            "[int] $y = (Get-Item $path).Value; return $y }; Export-ModuleMember -Function Get-DynamicLocal",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DynamicVariableProvider",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-DynamicLocal");
        Assert.Equal((0, "1", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModulePreservesDiscoveryInsideImmediatelyInvokedScriptBlock()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:before = & { [bool](Get-Command Get-PowerForgeNestedLater -ErrorAction SilentlyContinue) }; " +
            "function Get-PowerForgeNestedLater { return 1 }; function Get-NestedBefore { return $script:before }; " +
            "Export-ModuleMember -Function Get-PowerForgeNestedLater, Get-NestedBefore",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NestedDiscovery",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-NestedBefore");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_CompleteModuleRejectsOutputAtModuleRoot()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RootOutputValue { return 1 }; Export-ModuleMember -Function Get-RootOutputValue",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "template.txt"), "payload");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.RootPath,
            "PowerForge.RootOutput",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            ResourceMode = PowerShellCompilationResourceMode.CompleteModule
        });

        Assert.False(result.Succeeded);
        Assert.Contains("CompleteModule", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("neither the module root", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(fixture.RootPath, "template.txt")));
    }

    [Fact]
    public void Analyze_CompleteModuleRejectsOutputAncestorOfModuleRoot()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-AncestorOutputValue { return 1 }; Export-ModuleMember -Function Get-AncestorOutputValue",
            ".psm1");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            fixture.ScriptPath,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var outputAncestor = Directory.GetParent(fixture.RootPath)!.FullName;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationAnalyzer().Analyze(
                resolved,
                PowerShellCompilationMode.Hybrid,
                resourceMode: PowerShellCompilationResourceMode.CompleteModule,
                outputDirectory: outputAncestor));

        Assert.Contains("nor one of its ancestors", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PackagedExecutableRewritesOrdinaryReferencesToEmbeddedScripts()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/helper.ps1\"; \"$(Test-Path \"$PSScriptRoot/helper.ps1\")|$(Get-HelperValue)\"");
        var helper = Path.Combine(fixture.RootPath, "helper.ps1");
        File.WriteAllText(helper, "function Get-HelperValue { return 7 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ScriptReference",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper },
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "True|7", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableKeepsExtractedPayloadForAsynchronousChild()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Marker); " +
            "[Environment]::SetEnvironmentVariable('POWERFORGE_ASYNC_MARKER', $Marker); " +
            "Start-Process -FilePath 'pwsh' -ArgumentList '-NoProfile','-NonInteractive','-File',\"$PSScriptRoot/worker.ps1\" | Out-Null; " +
            "$PSScriptRoot");
        File.WriteAllText(
            Path.Combine(fixture.RootPath, "worker.ps1"),
            "Start-Sleep -Milliseconds 750; Get-Content -LiteralPath \"$PSScriptRoot/data.txt\" | " +
            "Set-Content -LiteralPath $env:POWERFORGE_ASYNC_MARKER");
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "async-payload");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AsyncPayload",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            IncludeResource = new[] { "worker.ps1", "data.txt" },
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var marker = Path.Combine(fixture.RootPath, "async-marker.txt");
        var run = Run(result.ArtifactPath!, "--Marker=" + marker);
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        var extractedRoot = run.StandardOutput.Trim();
        try
        {
            Assert.True(Directory.Exists(extractedRoot), $"Extracted payload root was removed: {extractedRoot}");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(marker) && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
            Assert.True(File.Exists(marker), "The asynchronous child did not consume the extracted payload in time.");
            Assert.Equal("async-payload", File.ReadAllText(marker).Trim());
        }
        finally
        {
            try { Directory.Delete(extractedRoot, recursive: true); } catch { }
        }
    }
}
