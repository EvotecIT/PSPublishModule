using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
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

    [Fact]
    public void FinalExplanationRecordsTypesLoweringArtifactAndDependencyDecisions()
    {
        var unit = new PowerShellCompilationUnitPlan(
            "Get-RandomValue",
            PowerShellCompilationUnitKind.Function,
            4,
            typeof(int).FullName!,
            new[] { new PowerShellCompilationParameter("Count", typeof(int).FullName!, hasDefaultValue: true) },
            Array.Empty<PowerShellCompilationDiagnostic>());
        var file = new PowerShellCompilationFilePlan(
            Path.Combine(Path.GetTempPath(), "PowerForge", "Random.psm1"),
            "Random.psm1",
            new[] { unit },
            Array.Empty<PowerShellCompilationDiagnostic>());
        var dependency = new PowerShellCompilationDependency(
            "data.json",
            sourcePath: null,
            "Assets/data.json",
            PowerShellCompilationDependencyKind.Content,
            PowerShellCompilationDependencyDiscovery.ExplicitResourceInclude,
            PowerShellCompilationDependencyDisposition.Embedded,
            exists: true,
            sizeBytes: 12,
            "Selected by policy.");
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Strict,
            new[] { file },
            "net8.0",
            new[] { dependency });
        var ledger = new PowerShellCompilationUnitDispositionLedger(
            new[]
            {
                new PowerShellCompilationUnitDisposition(
                    PowerShellCompilationExplanationService.ComputeUnitId(file.RelativePath, unit),
                    file.RelativePath,
                    unit.Name,
                    unit.Kind,
                    unit.StartLine,
                    semanticEligible: true,
                    emittedClrMethod: true,
                    emittedBinaryCmdlet: false,
                    retainedHostedSource: false,
                    runtimeCommandRegions: 0,
                    boundaryCrossings: 0,
                    shapingFallback: false,
                    omitted: false,
                    rejected: false,
                    generatedMemberName: "Get_RandomValue",
                    dependencyCauses: Array.Empty<string>(),
                    boundaryCauses: Array.Empty<string>(),
                    diagnosticChain: Array.Empty<PowerShellCompilationDispositionCause>())
            },
            Array.Empty<string>());
        var explanation = PowerShellCompilationExplanationService.CreateFinal(plan, ledger);

        var tracedUnit = Assert.Single(Assert.Single(explanation.Files).Units);
        Assert.Equal(typeof(int).FullName, tracedUnit.ReturnType);
        Assert.Equal(typeof(int).FullName, Assert.Single(tracedUnit.Parameters).TypeName);
        Assert.Equal(PowerShellCompilationDecisionKind.Typed, tracedUnit.Decision);
        Assert.Equal("BoundClr", tracedUnit.LoweringRoute);
        Assert.Equal("TypedArtifact", tracedUnit.ArtifactDisposition);
        Assert.Equal(PowerShellCompilationDependencyDisposition.Embedded, Assert.Single(explanation.Dependencies).Disposition);

#pragma warning disable CS0618 // The regression locks the explicit pre-ledger compatibility contract.
        var compatibility = PowerShellCompilationExplanationService.CreateFinal(
            plan,
            PowerShellCompilationArtifactKind.Executable,
            shapedCompilation: null);
#pragma warning restore CS0618
        Assert.Equal(2, compatibility.SchemaVersion);
        Assert.Equal(1, compatibility.SemanticCompatibilityVersion);
        var compatibilityOverload = typeof(PowerShellCompilationExplanationService).GetMethod(
            nameof(PowerShellCompilationExplanationService.CreateFinal),
            new[]
            {
                typeof(PowerShellCompilationPlan),
                typeof(PowerShellCompilationArtifactKind),
                typeof(PowerShellTypedCompilationResult)
            });
        Assert.NotNull(compatibilityOverload?.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
    }

    [Fact]
    public void SemanticFingerprintIgnoresRelocationAndDeclarationOrderButPreservesParameterOrder()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "PowerForge.SemanticFingerprint", Guid.NewGuid().ToString("N"));
        var secondRoot = Path.Combine(Path.GetTempPath(), "PowerForge.SemanticFingerprint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        try
        {
            var firstPath = Path.Combine(firstRoot, "input.psm1");
            var secondPath = Path.Combine(secondRoot, "input.psm1");
            File.WriteAllText(firstPath, "function Get-First { param([int] $Left, [string] $Right) $Left }; function Get-Second { 2 }");
            File.WriteAllText(secondPath, "function Get-Second { 2 }; function Get-First { param([int] $Left, [string] $Right) $Left }");

            string Fingerprint(string path) => PowerShellCompilationExplanationService.Create(
                new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(path, PowerShellCompilationMode.Strict, targetFramework: "net8.0")))
                .SemanticFingerprintSha256;

            Assert.Equal(Fingerprint(firstPath), Fingerprint(secondPath));
            File.WriteAllText(secondPath, "function Get-Second { 2 }; function Get-First { param([string] $Right, [int] $Left) $Left }");
            Assert.NotEqual(Fingerprint(firstPath), Fingerprint(secondPath));
        }
        finally
        {
            try { Directory.Delete(firstRoot, recursive: true); } catch { }
            try { Directory.Delete(secondRoot, recursive: true); } catch { }
        }
    }
}
