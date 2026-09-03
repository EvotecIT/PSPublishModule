namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Theory]
    [InlineData("[ValidateRange(1, 2)][int] $Value", "3")]
    [InlineData("[ValidateSet('A', 'B')][string] $Value", "'C'")]
    [InlineData("[ValidatePattern('^ok$')][string] $Value", "'no'")]
    public void Build_HybridModulePreservesValidationExceptionRoutingForTypedLocalCalls(
        string parameter,
        string argument)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Invoke-Validated {{ param({parameter}) return 'ok' }}; " +
            $"function Get-ValidationRoute {{ try {{ Invoke-Validated -Value {argument} }} " +
            "catch [System.ArgumentException] { return 'clr' } catch { return 'ps' } }; " +
            "Export-ModuleMember -Function Get-ValidationRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ValidationRouting",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.AnalyzedUnits);
        Assert.Equal(1, result.Manifest.EmittedUnits);
        Assert.Equal(2, result.Manifest.RuntimeRoutedUnits);
        Assert.Equal(1, result.Manifest.FallbackUnits);
        Assert.Equal(1, result.Manifest.ShapedFallbackUnits);
        Assert.Equal(50d, result.Manifest.CompilationCoveragePercentage);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        Assert.Equal(2, ledger.Entries.Count(static entry => entry.RetainedHostedSource));
        Assert.Single(ledger.Entries, static entry => entry.EmittedClrMethod && entry.RetainedHostedSource);
        Assert.Single(ledger.Entries, static entry => !entry.EmittedClrMethod && entry.RetainedHostedSource);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-ValidationRoute");
        Assert.Equal((0, "ps", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictLibraryRejectsObservedLocalValidationExceptionWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Validated { param([ValidateRange(1, 2)][int] $Value) return 'ok' }; " +
            "function Get-ValidationRoute { try { Invoke-Validated -Value 3 } " +
            "catch [System.ArgumentException] { return 'clr' } catch { return 'ps' } }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ValidationRoutingIndependent",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("Strict mode rejected", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModulePreservesNullablePropertyAssignmentExceptionRouting()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PropertyRoute { [CmdletBinding()] param([System.Text.StringBuilder] $Builder) " +
            "try { $Builder.Capacity = 4; return 'ok' } " +
            "catch [System.NullReferenceException] { return 'clr' } catch { return 'ps' } }; " +
            "Export-ModuleMember -Function Get-PropertyRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullablePropertyRouting",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-PropertyRoute");
        Assert.Equal((0, "ps", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictLibraryRejectsNullablePropertyAssignmentWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-Capacity { param([System.Text.StringBuilder] $Builder) $Builder.Capacity = 4 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullablePropertyIndependent",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("runtime-error identity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StandaloneWildcardIgnoresPriorDurableOutputOnRepeatBuild()
    {
        using var fixture = ArtifactFixture.Create("Get-Content -LiteralPath \"$PSScriptRoot/data.txt\"");
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "repeatable-resource");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RepeatableResources",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            IncludeResource = new[] { "**/*" }
        };

        var first = new PowerShellCompilationArtifactBuilder().Build(spec);
        var second = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
        Assert.True(second.Succeeded, second.Error + Environment.NewLine + second.BuildOutput);
        Assert.Equal(first.ArtifactPath, second.ArtifactPath);
    }

    [Fact]
    public void Build_HybridModuleKeepsPossiblyEmptyStreamMessagesOnPowerShellPath()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MessageResult { [CmdletBinding()] param([object] $Message) " +
            "Write-Warning $Message; return 'after' }; Export-ModuleMember -Function Get-MessageResult",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StreamMessageBinding",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-MessageResult");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("after", run.StandardOutput.Trim());
        Assert.Contains("Cannot bind argument to parameter 'Message'", run.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PackagedExecutableRestoresExecutableModeForIncludedUnixResource()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = ArtifactFixture.Create(
            "/usr/bin/test -x \"$PSScriptRoot/tool.sh\"; " +
            "if ($LASTEXITCODE -ne 0) { throw 'Packaged tool is not executable.' }; " +
            "/bin/sh \"$PSScriptRoot/tool.sh\"");
        var tool = Path.Combine(fixture.RootPath, "tool.sh");
        File.WriteAllText(tool, "#!/bin/sh\nprintf 'unix-resource-proof\\n'\n");
        File.SetUnixFileMode(
            tool,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.UnixResourceMode",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            IncludeResource = new[] { "tool.sh" }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "unix-resource-proof", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData("return @('I') -contains 'ı'")]
    [InlineData("return 'ı' -in @('I')")]
    public void Build_HybridModuleUsesInvariantCultureForMembershipOperators(string expression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-CultureResult {{ {expression} }}; Export-ModuleMember -Function Get-CultureResult",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvariantMembership",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$old = [System.Globalization.CultureInfo]::CurrentCulture; try { " +
            "[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'); " +
            $"Import-Module -Name '{escapedPath}' -Force; Get-CultureResult }} finally {{ " +
            "[System.Globalization.CultureInfo]::CurrentCulture = $old }");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModulePreservesPreDeclarationGetCommandDiscovery()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:before = [bool](Get-Command Get-Later -ErrorAction SilentlyContinue); " +
            "function Get-Later { return 1 }; function Get-Before { return $script:before }; " +
            "Export-ModuleMember -Function Get-Later, Get-Before",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PreDeclarationDiscovery",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("command-availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-Before");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModuleKeepsAuthoredDispatcherVariables()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:__powerForgeRunspaceId = 'authored-runspace'; " +
            "$script:__powerForgeModule = 'authored-module'; " +
            "$script:__powerForgePreviousOnRemove = 'authored-previous'; " +
            "$script:__powerForgeRuntimeCleanup = 'authored-cleanup'; " +
            "$script:__powerForgeInstalledOnRemove = 'authored-installed'; " +
            "$script:__powerForgeEffectiveOnRemove = 'authored-effective'; " +
            "$script:__powerForgeInitializationFailed = 'authored-failed'; " +
            "function Get-RegionValue { [CmdletBinding()] param([string] $Name) Microsoft.PowerShell.Utility\\Write-Output 'region'; return $Name }; " +
            "function Get-AuthoredDispatcherState { return \"$script:__powerForgeRunspaceId|$script:__powerForgeModule|$script:__powerForgePreviousOnRemove|$script:__powerForgeRuntimeCleanup|$script:__powerForgeInstalledOnRemove|$script:__powerForgeEffectiveOnRemove|$script:__powerForgeInitializationFailed\" }; " +
            "Export-ModuleMember -Function Get-RegionValue, Get-AuthoredDispatcherState",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DispatcherVariableIsolation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.AnalyzedUnits);
        Assert.Equal(1, result.Manifest.EmittedUnits);
        Assert.Equal(2, result.Manifest.RuntimeRoutedUnits);
        Assert.Equal(2, result.Manifest.FallbackUnits);
        Assert.Equal(0, result.Manifest.ShapedFallbackUnits);
        Assert.Equal(100d / 3d, result.Manifest.CompilationCoveragePercentage, precision: 8);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-AuthoredDispatcherState");
        Assert.Equal((0, "authored-runspace|authored-module|authored-previous|authored-cleanup|authored-installed|authored-effective|authored-failed", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModuleCompilesCommentBasedHelpAndGeneratesExternalHelp()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HelpedValue {\n<#\n.SYNOPSIS\nAuthored compiler help synopsis.\n.DESCRIPTION\nRetained description.\n#>\nreturn 7\n}; " +
            "Export-ModuleMember -Function Get-HelpedValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommentHelpIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Files, file => file.Role == "ExternalHelp" && File.Exists(file.Path));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Help Get-HelpedValue).Synopsis");
        Assert.Equal((0, "Authored compiler help synopsis.", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesCommentBasedHelpWithGeneratedExternalHelp()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HelpedValue {\n<#\n.SYNOPSIS\nAuthored compiler help synopsis.\n.DESCRIPTION\nFull compiled help description.\n.PARAMETER Name\nName parameter help.\n.EXAMPLE\nGet-HelpedValue -Name Ada\n.NOTES\nCompiled help note.\n.LINK\nhttps://example.com/help\n.INPUTS\nSystem.String\n.OUTPUTS\nSystem.Int32\n#>\nparam([string] $Name)\nreturn 7\n}; " +
            "Export-ModuleMember -Function Get-HelpedValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommentHelpStrict",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        var helpFile = Assert.Single(result.Manifest.Files, file => file.Role == "ExternalHelp" && File.Exists(file.Path));
        var maml = File.ReadAllText(helpFile.Path);
        Assert.Contains("Full compiled help description.", maml, StringComparison.Ordinal);
        Assert.Contains("Name parameter help.", maml, StringComparison.Ordinal);
        Assert.Contains("Get-HelpedValue -Name Ada", maml, StringComparison.Ordinal);
        Assert.Contains("Compiled help note.", maml, StringComparison.Ordinal);
        Assert.Contains("https://example.com/help", maml, StringComparison.Ordinal);
        Assert.Contains("System.String", maml, StringComparison.Ordinal);
        Assert.Contains("System.Int32", maml, StringComparison.Ordinal);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; $h = Get-Help Get-HelpedValue -Full; $h.Synopsis; ($h.Parameters.Parameter | Where-Object Name -eq 'Name').Description.Text");
        Assert.Equal((0, "Authored compiler help synopsis." + Environment.NewLine + "Name parameter help.", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModulePreservesHelpAcrossTypedAndRetainedCommands()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedHelp {\n<#\n.SYNOPSIS\nTyped command help.\n#>\nreturn 3\n}; " +
            "function Get-RetainedHelp {\n<#\n.SYNOPSIS\nRetained command help.\n#>\nreturn [int](Get-Date -Format yyyy)\n}; " +
            "Export-ModuleMember -Function @('Get-TypedHelp', 'Get-RetainedHelp')",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MixedHelp",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Command Get-TypedHelp).CommandType; (Get-Help Get-TypedHelp).Synopsis; (Get-Command Get-RetainedHelp).CommandType; (Get-Help Get-RetainedHelp).Synopsis");
        Assert.Equal((0, "Cmdlet" + Environment.NewLine + "Typed command help." + Environment.NewLine + "Function" + Environment.NewLine + "Retained command help.", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }
}
