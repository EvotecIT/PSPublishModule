using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleTestFailureSerializationServiceTests
{
    [Fact]
    public void ToJson_serializes_failure_analysis_without_powerShell_conversion()
    {
        var service = new ModuleTestFailureSerializationService();
        var analysis = new ModuleTestFailureAnalysis
        {
            Source = "PesterResults",
            TotalCount = 3,
            PassedCount = 2,
            FailedCount = 1,
            FailedTests = new[]
            {
                new ModuleTestFailureInfo
                {
                    Name = "It does the thing",
                    ErrorMessage = "Expected true, got false"
                }
            }
        };

        var json = service.ToJson(analysis);

        Assert.Contains("\"Source\":\"PesterResults\"", json, StringComparison.Ordinal);
        Assert.Contains("\"FailedCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"Name\":\"It does the thing\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ErrorMessage\":\"Expected true, got false\"", json, StringComparison.Ordinal);
    }
}
