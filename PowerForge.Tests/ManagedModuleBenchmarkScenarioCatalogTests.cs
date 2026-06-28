using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkScenarioCatalogTests
{
    [Fact]
    public void BenchmarkSuite_SpeedGate_ExposesModuleFastAndProviderMatrixScenarios()
    {
        var script = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "Invoke-ManagedModuleBenchmarkSuite.ps1");

        var results = InvokeScenarioList(script);

        var rows = results.RootElement.EnumerateArray().ToArray();
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Graph.Full.SameSource" &&
            Property(row, "Repository") == "https://pwsh.gallery/index.json" &&
            Property(row, "RepositoryName") == "PWSHGallery" &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast" }));
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Graph.Full.ProviderMatrix" &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }));
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Az.Accounts.ProviderMatrix" &&
            Property(row, "Version") == "5.5.0" &&
            BooleanProperty(row, "AcceptLicense") &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }));
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Az.Full.ProviderMatrix" &&
            Property(row, "Version") == "16.0.0" &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }));
    }

    private static JsonDocument InvokeScenarioList(string script)
    {
        using var process = new Process();
        process.StartInfo.FileName = "pwsh";
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(
            "& '" + script.Replace("'", "''", StringComparison.Ordinal) + "' -Suite SpeedGate -ListScenarios | ConvertTo-Json -Depth 5 -Compress");
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            Assert.Fail("Scenario list failed: " + error);

        return JsonDocument.Parse(output);
    }

    private static string Property(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) ? property.GetString() ?? string.Empty : string.Empty;

    private static string[] StringArrayProperty(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Select(static item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static bool BooleanProperty(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
}
