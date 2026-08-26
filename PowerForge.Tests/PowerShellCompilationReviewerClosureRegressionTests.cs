namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Theory]
    [InlineData("[CmdletBinding()] param(); $PSCmdlet.Host.UI.ReadLine()")]
    [InlineData("[CmdletBinding()] param(); $member = 'Host'; $PSCmdlet.$member.UI.ReadLine()")]
    [InlineData("[CmdletBinding()] param(); $escaped = $PSCmdlet; $escaped.Host.UI.ReadLine()")]
    [InlineData("[CmdletBinding()] param(); $PSCmdlet.InvokeCommand.InvokeScript('$Host.Name')")]
    public void Build_PackagedExecutableRejectsPSCmdletHostAccess(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.PSCmdletHost", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("PSHost", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

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

    [Theory]
    [InlineData("#requires -Version 99.0\n'blocked'")]
    [InlineData("#requires -RunAsAdministrator\n'blocked'")]
    [InlineData("#requires -Modules PowerForge.Missing\n'blocked'")]
    public void Build_PackagedExecutableRejectsFileLevelRequirements(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.Requires", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("#requires", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("param([Parameter(Mandatory)] [string] $Name); $Name")]
    [InlineData("Read-Host 'Value'")]
    [InlineData("process { 1 }")]
    public void Analyze_PackageReportsDeterministicBuildValidation(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);

        var plan = new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Package,
            outputDirectory: fixture.OutputPath);

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Files.SelectMany(static file => file.Diagnostics), static diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.InputError &&
            diagnostic.FeatureId == "powershell.package.validation");
    }

    [Fact]
    public void Build_PackagedExecutableRebuildsCorruptedExtractionCache()
    {
        using var fixture = ArtifactFixture.Create(
            "$PSScriptRoot; Get-Content -LiteralPath \"$PSScriptRoot/data.txt\"");
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "approved-payload");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CacheIntegrity",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            IncludeResource = new[] { "data.txt" },
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var first = Run(result.ArtifactPath!);
        Assert.Equal(0, first.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(first.StandardError), first.StandardError);
        var lines = first.StandardOutput.Split(
            new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        var extractedRoot = lines[0];
        try
        {
            File.WriteAllText(Path.Combine(extractedRoot, "data.txt"), "tampered-payload");
            var second = Run(result.ArtifactPath!);
            Assert.Equal((0, extractedRoot + Environment.NewLine + "approved-payload", string.Empty),
                (second.ExitCode, second.StandardOutput.Trim(), second.StandardError.Trim()));
            var extractedEntry = Path.Combine(extractedRoot, Path.GetFileName(fixture.ScriptPath));
            File.WriteAllText(extractedEntry, "'tampered-entry'");
            var third = Run(result.ArtifactPath!);
            Assert.Equal((0, extractedRoot + Environment.NewLine + "approved-payload", string.Empty),
                (third.ExitCode, third.StandardOutput.Trim(), third.StandardError.Trim()));
        }
        finally
        {
            try { Directory.Delete(extractedRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesOutOfRangeArrayMutationCatchRouting()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayMutationRoute { param([int[]] $Numbers, [int] $Slot); " +
            "try { $Numbers[$Slot] = 9; return 'assigned' } " +
            "catch [System.Management.Automation.RuntimeException] { return 'caught' } }; " +
            "Export-ModuleMember -Function Get-ArrayMutationRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ArrayMutationRoute",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("new global::System.Management.Automation.RuntimeException", generated, StringComparison.Ordinal);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-ArrayMutationRoute -Numbers @(1) -Slot 2; Get-ArrayMutationRoute -Numbers @(1) -Slot -2");
        Assert.Equal((0, "caught" + Environment.NewLine + "caught", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesMatchesAutomaticVariable()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MatchCapture { $Matches = @{'1' = 'old'}; $ok = 'new' -match '^(.)'; return $Matches.1 }; " +
            "Export-ModuleMember -Function Get-MatchCapture",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MatchCapture",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("$Matches", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-MatchCapture");
        Assert.Equal((0, "n", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesClrInvocationRuntimeExceptionCatch()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MemberCatch { param([string] $Text); try { return $Text.Substring(99) } " +
            "catch [System.Management.Automation.RuntimeException] { return 'caught' } }; " +
            "Export-ModuleMember -Function Get-MemberCatch",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MemberCatch",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("runtime-error wrapping", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-MemberCatch -Text x");
        Assert.Equal((0, "caught", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesIntegralOverflowRuntimeExceptionCatch()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-OverflowCatch { param([byte] $Value); try { $Value++; return 1 } " +
            "catch [System.Management.Automation.RuntimeException] { return 2 } }; " +
            "Export-ModuleMember -Function Get-OverflowCatch",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.OverflowCatch",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.True(
            result.Manifest.Diagnostics.Any(static diagnostic =>
                diagnostic.Message.Contains("overflow-error wrapping", StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-OverflowCatch -Value 255");
        Assert.Equal((0, "2", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesAuthoredNamedArgumentEvaluationOrder()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Order { param([int] $A, [int] $B) return $A }; " +
            "function Get-Order { param([int] $x); [int] $ignored = Invoke-Order -B ($x = 1) -A ($x = 2); return $x }; " +
            "Export-ModuleMember -Function Get-Order",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NamedArgumentOrder",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-Order -x 0");
        Assert.Equal((0, "2", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsDynamicCommandInvocation()
    {
        using var fixture = ArtifactFixture.Create("$command = 'Read-Host'; & $command");
        var result = BuildExecutable(fixture, "PowerForge.DynamicInteractiveCommand", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("dynamic command invocation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModulePreservesDynamicPreDeclarationInvocationTiming()
    {
        using var fixture = ArtifactFixture.Create(
            "$name = 'Get-Later'; try { $script:before = & $name } catch { $script:before = 'missing' }; " +
            "function Get-Later { [CmdletBinding()] param(); return 'later' }; " +
            "function Get-Stable { [CmdletBinding()] param(); return 'stable' }; " +
            "Export-ModuleMember -Function Get-Later, Get-Stable",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DynamicPreDeclaration",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Contains(result.Manifest.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("command-availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-Stable; Get-Later");
        Assert.Equal((0, "stable" + Environment.NewLine + "later", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesSimpleFunctionLooseArgumentBinding()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LooseValue { param([string] $Label); return 'preserved' }; " +
            "Export-ModuleMember -Function Get-LooseValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LooseSimpleBinding",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-LooseValue -Unknown value");
        Assert.Equal((0, "preserved", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData("function Get-CallerPath { $MyInvocation.ScriptName }; Get-CallerPath")]
    [InlineData("function Get-CallerPath { $invocation = $MyInvocation; $invocation.PSScriptRoot }; Get-CallerPath")]
    [InlineData("function Get-CallerPath { $member = 'ScriptName'; $MyInvocation.$member }; Get-CallerPath")]
    public void Build_PackagedExecutableRejectsNestedCallerPathMetadata(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.NestedCallerPath", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("invocation metadata", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModulePreservesAliasTargetAvailabilityBeforeDeclaration()
    {
        using var fixture = ArtifactFixture.Create(
            "Set-Alias Invoke-Later Get-Later; try { $script:before = Invoke-Later } catch { $script:before = 'missing' }; " +
            "function Get-Later { [CmdletBinding()] param(); return 'later' }; " +
            "function Get-Before { [CmdletBinding()] param(); return $script:before }; " +
            "Export-ModuleMember -Function Get-Later, Get-Before",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AliasTargetTiming",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("command-availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-Before; Get-Later");
        Assert.Equal((0, "missing" + Environment.NewLine + "later", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModuleRoutesDecimalArithmeticObservedByRuntimeExceptionCatchToFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DecimalRoute { try { return [decimal] 1 / [decimal] 0 } " +
            "catch [System.Management.Automation.RuntimeException] { return [object] 'runtime' } }; " +
            "Export-ModuleMember -Function Get-DecimalRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DecimalRuntimeCatch",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-DecimalRoute");
        Assert.Equal((0, "runtime", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableBindsAutomaticPagingParameters()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(SupportsPaging = $true)] param(); " +
            "return \"$($PSCmdlet.PagingParameters.First)|$($PSCmdlet.PagingParameters.Skip)|$($PSCmdlet.PagingParameters.IncludeTotalCount.IsPresent)\"");
        var result = BuildExecutable(fixture, "PowerForge.PagingParameters", PowerShellCompilationMode.Package);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "--First", "5", "--Skip", "2", "--IncludeTotalCount");
        Assert.Equal((0, "5|2|True", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void DefaultRuntimeIdentifierMapsExtendedArchitecturesAndRejectsUnknownValues()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S390x"] = "linux-s390x",
            ["Ppc64le"] = "linux-ppc64le",
            ["LoongArch64"] = "linux-loongarch64",
            ["Armv6"] = "linux-armv6"
        };
        foreach (var pair in expected)
        {
            var architecture = Enum.Parse<System.Runtime.InteropServices.Architecture>(pair.Key, ignoreCase: true);
            Assert.Equal(pair.Value, PowerShellCompilationArtifactBuilder.GetDefaultRuntimeIdentifier(
                isWindows: false,
                isMacOS: false,
                hostRuntimeIdentifier: "linux-x64",
                architecture));
        }

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationArtifactBuilder.GetDefaultRuntimeIdentifier(
                isWindows: false,
                isMacOS: false,
                hostRuntimeIdentifier: "linux-x64",
                (System.Runtime.InteropServices.Architecture)int.MaxValue));
    }
}
