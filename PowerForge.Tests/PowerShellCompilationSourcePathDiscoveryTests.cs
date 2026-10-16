using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationInputResolverTests
{
    [Fact]
    public void Resolve_CompleteSitemapApplicationDiscoversItsDerivedHelperWithoutExecutingSource()
    {
        var repository = FindSourcePathRepository();
        var source = Path.Combine(repository, "Build", "Compare-WebSitemaps.ps1");
        var helper = Path.Combine(repository, "Build", "Compare-WebSitemaps.Helpers.ps1");
        var before = new[] { source, helper }.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();

        var resolved = new PowerShellCompilationInputResolver().Resolve(source);

        Assert.Equal(new[] { source, helper }, resolved.SourceFiles);
        Assert.Equal(new[] { source, helper }, resolved.CompilationSourceFiles);
        Assert.Equal(before[0], SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(before[1], SHA256.HashData(File.ReadAllBytes(helper)));
    }

    [Fact]
    public void Resolve_DerivedCandidateDoesNotExecuteShadowedPathCommands()
    {
        using var fixture = ResolverFixture.Create();
        var sentinel = Path.Combine(fixture.Root, "executed.txt").Replace("'", "''");
        var source = fixture.Write("main.ps1",
            $"function Join-Path {{ [IO.File]::WriteAllText('{sentinel}', 'executed'); throw 'caller path command' }}; " +
            "$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; " +
            "if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) { throw 'missing' }; . $helperPath");
        var helper = fixture.Write("helper.ps1", "function Get-Value { 7 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(source);

        Assert.Equal(new[] { source, helper }, resolved.SourceFiles);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "executed.txt")));
    }

    [Theory]
    [InlineData("$helperPath = 'helper.ps1'; . $helperPath")]
    [InlineData("$script:helperPath = Join-Path $PSScriptRoot 'helper.ps1'; . $script:helperPath")]
    [InlineData("if ($true) { $helperPath = Join-Path $PSScriptRoot 'helper.ps1' }; . $helperPath")]
    [InlineData("foreach ($item in 1) { $helperPath = Join-Path $PSScriptRoot 'helper.ps1' }; . $helperPath")]
    [InlineData("& { $helperPath = Join-Path $PSScriptRoot 'helper.ps1' }; . $helperPath")]
    [InlineData("$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; if ($true) { . $helperPath }")]
    [InlineData("Set-Alias jp Join-Path; $helperPath = jp $PSScriptRoot 'helper.ps1'; . $helperPath")]
    [InlineData("$name = 'helper.ps1'; $helperPath = Join-Path $PSScriptRoot $name; . $helperPath")]
    [InlineData("$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; $helperPath = 'other.ps1'; . $helperPath")]
    [InlineData("$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; $script:helperPath = 'other.ps1'; . $helperPath")]
    [InlineData("$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; if ($true) { $helperPath = 'other.ps1' }; . $helperPath")]
    [InlineData("$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; $helperPath = Join-Path $PSScriptRoot 'other.ps1'; . $helperPath")]
    public void Resolve_RejectsUnknownOrContradictoryDerivedSourceBindings(string text)
    {
        using var fixture = ResolverFixture.Create();
        var source = fixture.Write("main.ps1", text);
        fixture.Write("helper.ps1", "function Get-Value { 7 }");
        fixture.Write("other.ps1", "throw 'unselected source'");

        var error = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(source));

        Assert.Contains("literal $PSScriptRoot path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.ps1")]
    [InlineData("helper*.ps1")]
    public void Resolve_RejectsEscapingOrWildcardDerivedSourceCandidates(string child)
    {
        using var fixture = ResolverFixture.Create();
        var source = fixture.Write("main.ps1", $"$helperPath = Join-Path $PSScriptRoot '{child}'; . $helperPath");

        Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(source));
    }

    [Fact]
    public void Resolve_DerivedSourceAdmissionDoesNotWidenBinaryModulesOrStrictExecutables()
    {
        using var fixture = ResolverFixture.Create();
        const string text = "$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; . $helperPath";
        var module = fixture.Write("main.psm1", text);
        var script = fixture.Write("main.ps1", text);
        fixture.Write("helper.ps1", "function Get-Value { 7 }");
        var resolver = new PowerShellCompilationInputResolver();

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(module,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(script,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict));
    }

    [UnixSourcePathFact]
    public void Resolve_RejectsLinkedDerivedSourceEvenWhenItsBytesMatchAContainedFile()
    {
        using var fixture = ResolverFixture.Create();
        var source = fixture.Write("main.ps1", "$helperPath = Join-Path $PSScriptRoot 'helper.ps1'; . $helperPath");
        var target = fixture.Write("unselected.ps1", "function Get-Value { 7 }");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "helper.ps1"), target);

        var error = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(source));

        Assert.Contains("symbolic link or junction", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSourcePathRepository([CallerFilePath] string testSourcePath = "")
    {
        foreach (var candidate in new[] { Path.GetDirectoryName(testSourcePath), Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = string.IsNullOrWhiteSpace(candidate) ? null : new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Build", "Compare-WebSitemaps.ps1"))) return current.FullName;
                current = current.Parent;
            }
        }
        throw new InvalidOperationException("The complete sitemap application source was not found from the test source or execution context.");
    }
}

internal sealed class UnixSourcePathFactAttribute : FactAttribute
{
    public UnixSourcePathFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Source-link qualification uses Unix links without requiring Windows symbolic-link privileges.";
    }
}
