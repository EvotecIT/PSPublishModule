using System.Management.Automation;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkArtifactScriptTests
{
    [Fact]
    public void ArtifactWriters_CreateParseableFilesAndRemoveTemporaryFiles()
    {
        using var temp = new TemporaryDirectory();
        using var ps = PowerShell.Create();
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.Artifacts.ps1");
        var csvPath = Path.Combine(temp.Path, "results.csv");
        var jsonPath = Path.Combine(temp.Path, "results.json");
        ps.AddScript(File.ReadAllText(script) + Environment.NewLine + $$"""
            $rows = @(
                [pscustomobject]@{ Engine = 'Managed'; Status = 'Succeeded'; ElapsedMilliseconds = 123.45 }
            )
            $csvPath = '{{EscapePowerShellString(csvPath)}}'
            $jsonPath = '{{EscapePowerShellString(jsonPath)}}'
            Write-ManagedBenchmarkCsv -InputObject $rows -Path $csvPath
            Write-ManagedBenchmarkJson -InputObject $rows -Path $jsonPath -Depth 4
            [pscustomobject]@{
                CsvText = Get-Content -LiteralPath $csvPath -Raw
                JsonText = Get-Content -LiteralPath $jsonPath -Raw
                TemporaryCount = @(Get-ChildItem -LiteralPath (Split-Path -Path $csvPath -Parent) -Filter '.*.tmp' -Force).Count
            }
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Contains("Managed", Property(row, "CsvText"), StringComparison.Ordinal);
        Assert.Contains("ElapsedMilliseconds", Property(row, "JsonText"), StringComparison.Ordinal);
        Assert.Equal(0, Convert.ToInt32(row.Properties["TemporaryCount"].Value));

        Assert.Contains("Managed", File.ReadAllText(csvPath), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("Managed", document.RootElement.GetProperty("Engine").GetString());
    }

    private static string Property(PSObject value, string name)
        => value.Properties[name]?.Value?.ToString() ?? string.Empty;

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString()));
        Assert.Fail(message);
    }
}
