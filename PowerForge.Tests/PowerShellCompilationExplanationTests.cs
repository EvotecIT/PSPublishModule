using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationExplanationTests
{
    [Fact]
    public void ExplanationIncludesFileAndDependencyCausesAndRedactsAuthoredAbsolutePaths()
    {
        const string privatePath = @"C:\Users\Alice\Private\Module.psd1";
        const string privateUnixPath = "/home/alice/private/Module.psd1";
        const string privateUncPath = @"\\server\share\private\Module.psd1";
        var diagnostic = new PowerShellCompilationDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            $"Sources 'using module '{privatePath}'', using module {privateUnixPath}, and using module {privateUncPath} have runtime-bearing semantics.",
            @"C:\Work\Input.ps1",
            1,
            1,
            "powershell.runtime.using");
        var file = new PowerShellCompilationFilePlan(
            @"C:\Work\Input.ps1",
            "Input.ps1",
            Array.Empty<PowerShellCompilationUnitPlan>(),
            new[] { diagnostic });
        var missing = new PowerShellCompilationDependency(
            privatePath,
            sourcePath: null,
            privateUnixPath,
            PowerShellCompilationDependencyKind.ManagedAssembly,
            PowerShellCompilationDependencyDiscovery.RequiredAssemblies,
            PowerShellCompilationDependencyDisposition.Missing,
            exists: false,
            sizeBytes: 0,
            $"Required dependency '{privatePath}' was not found.");
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Strict,
            new[] { file },
            "net10.0",
            new[] { missing });

        var explanation = PowerShellCompilationExplanationService.Create(plan);
        var fileCause = Assert.Single(Assert.Single(explanation.Files).Causes);
        var dependencyCause = Assert.Single(explanation.DependencyCauses);

        Assert.False(explanation.CanProceed);
        Assert.DoesNotContain(privatePath, fileCause.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privatePath, dependencyCause.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privatePath, dependencyCause.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateUnixPath, dependencyCause.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain(privateUnixPath, fileCause.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(privateUncPath, fileCause.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-path>", fileCause.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted-path>", dependencyCause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplanationIsEquivalentAfterSourceTreeRelocation()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Explain", Guid.NewGuid().ToString("N"));
        var secondRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Explain", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        try
        {
            const string source = "function Invoke-Explain { Invoke-DynamicThing $Name }";
            var firstPath = Path.Combine(firstRoot, "Module.psm1");
            var secondPath = Path.Combine(secondRoot, "Module.psm1");
            File.WriteAllText(firstPath, source);
            File.WriteAllText(secondPath, source);

            string Explain(string path)
            {
                var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                    path,
                    PowerShellCompilationMode.Strict,
                    targetFramework: "net10.0"));
                return JsonSerializer.Serialize(PowerShellCompilationExplanationService.Create(plan));
            }

            Assert.Equal(Explain(firstPath), Explain(secondPath));
        }
        finally
        {
            try { Directory.Delete(firstRoot, recursive: true); } catch { }
            try { Directory.Delete(secondRoot, recursive: true); } catch { }
        }
    }
}
