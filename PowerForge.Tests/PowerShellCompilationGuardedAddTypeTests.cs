using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void GuardedAddType_AdmitsOnlyLaterMatchingHybridTypeUse(string framework)
    {
        const string guard = "if (-not ('Offline.Qualified' -as [type])) { Add-Type -TypeDefinition 'namespace Offline { public static class Qualified { public static int Read() { return 7; } } }' }";
        var cases = new[]
        {
            (Body: guard + "\n[Offline.Qualified]::Read()", Expected: true),
            (Body: "[Offline.Qualified]::Read()\n" + guard, Expected: false),
            (Body: guard.Replace("Offline.Qualified' -as", "Offline.Unrelated' -as", StringComparison.Ordinal) +
                   "\n[Offline.Qualified]::Read()", Expected: false),
            (Body: guard.Replace("Add-Type -TypeDefinition", "Add-Type -MemberDefinition", StringComparison.Ordinal) +
                   "\n[Offline.Qualified]::Read()", Expected: false),
            (Body: guard.Replace("Offline.Qualified' -as", "Offline.Qualified,OtherAssembly' -as", StringComparison.Ordinal) +
                   "\n[Offline.Qualified,OtherAssembly]::Read()", Expected: false),
            (Body: guard + "\n[Offline.Qualified]", Expected: false)
        };

        foreach (var (body, expected) in cases)
        {
            var source = PowerShellSourceParser.Parse("function Read-Guarded {\n" + body + "\n}",
                Path.Combine(Path.GetTempPath(), "guarded-addtype.psm1"));
            var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
                PowerShellCompilationCapabilities.HybridModule);
            Assert.Equal(expected, hybrid.Emitted.Methods.Any(method => method.GeneratedName == "Read_Guarded"));
        }

        var admitted = PowerShellSourceParser.Parse("function Read-Guarded {\n" + cases[0].Body + "\n}",
            Path.Combine(Path.GetTempPath(), "guarded-addtype-strict.psm1"));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { admitted }, framework,
            PowerShellCompilationCapabilities.TypedLibrary).Emitted.Methods);
    }
}
