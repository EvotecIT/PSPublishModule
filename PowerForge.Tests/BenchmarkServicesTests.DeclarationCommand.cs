using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void DeclarationCommand_StopsOnDriftAndReturnsFindingsWithPassThru()
    {
        string root = CreateTempRoot();
        try
        {
            BenchmarkDeclarationDocument declarations = TestDeclarations();
            declarations.Comparisons[0].Cases = ["Read"];
            string path = Path.Combine(root, "benchmarks.json");
            BenchmarkJson.Write(path, declarations);
            BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("OfficeIMO", "OnePdfLayout", Spread(12, 10)));
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Test-BenchmarkDeclaration", typeof(PSPublishModule.TestBenchmarkDeclarationCommand), null));
            using var runspace = RunspaceFactory.CreateRunspace(state);
            runspace.Open();

            using (var clean = PowerShell.Create(runspace))
            {
                clean.AddCommand("Test-BenchmarkDeclaration").AddParameter("Path", path);
                Assert.Empty(clean.Invoke());
                Assert.False(clean.HadErrors);
            }
            using (var failing = PowerShell.Create(runspace))
            {
                failing.AddCommand("Test-BenchmarkDeclaration").AddParameter("Path", path)
                    .AddParameter("InputObject", result).AddParameter("ComparisonId", "comparison-a");
                RuntimeException error = Assert.ThrowsAny<RuntimeException>(() => failing.Invoke());
                Assert.StartsWith("BenchmarkDeclarationInvalid", error.ErrorRecord.FullyQualifiedErrorId);
                Assert.Contains("OnePdfLayout", error.Message, StringComparison.Ordinal);
            }
            using (var passThru = PowerShell.Create(runspace))
            {
                passThru.AddCommand("Test-BenchmarkDeclaration").AddParameter("Path", path)
                    .AddParameter("InputObject", result).AddParameter("ComparisonId", "comparison-a").AddParameter("PassThru");
                var issue = Assert.IsType<BenchmarkDeclarationIssue>(Assert.Single(passThru.Invoke()).BaseObject);
                Assert.Equal("unexpected-case", issue.Code);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
