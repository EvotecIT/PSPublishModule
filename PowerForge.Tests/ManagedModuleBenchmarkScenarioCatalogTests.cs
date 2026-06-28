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
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Graph.Authentication.Save" &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "PSResourceGet" }) &&
            Int32Property(row, "ManagedMaxRank") == 1 &&
            DoubleProperty(row, "ManagedMaxVsFastest") == 0);
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Graph.Authentication.SaveExact.NoOpForce" &&
            Property(row, "Version") == "2.38.0" &&
            StringArrayProperty(row, "Operations").SequenceEqual(new[] { "SaveNoOp", "SaveForce" }) &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }) &&
            Int32Property(row, "ManagedMaxRank") == 1);
        Assert.Contains(rows, row =>
            Property(row, "Suite") == "HeavyLifecycleGate" &&
            Property(row, "Name") == "Graph.Full.InstallExact.NoOpForce" &&
            Property(row, "ModuleName") == "Microsoft.Graph" &&
            Property(row, "Version") == "2.38.0" &&
            Property(row, "Repository") == "https://pwsh.gallery/index.json" &&
            Property(row, "RepositoryName") == "PWSHGallery" &&
            StringArrayProperty(row, "Operations").SequenceEqual(new[] { "InstallNoOp", "InstallForce" }) &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet" }) &&
            Int32Property(row, "ManagedMaxRank") == 1);
        Assert.Contains(rows, row =>
            Property(row, "Suite") == "HeavyLifecycleGate" &&
            Property(row, "Name") == "Az.Full.InstallExact.NoOpForce" &&
            Property(row, "ModuleName") == "Az" &&
            Property(row, "Version") == "16.0.0" &&
            Property(row, "Repository") == "https://pwsh.gallery/index.json" &&
            Property(row, "RepositoryName") == "PWSHGallery" &&
            StringArrayProperty(row, "Operations").SequenceEqual(new[] { "InstallNoOp", "InstallForce" }) &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet" }) &&
            Int32Property(row, "ManagedMaxRank") == 1);
        Assert.Contains(rows, row =>
            Property(row, "Name") == "Az.Accounts.SaveExact.NoOpForce" &&
            Property(row, "Version") == "5.5.0" &&
            StringArrayProperty(row, "Operations").SequenceEqual(new[] { "SaveNoOp", "SaveForce" }) &&
            StringArrayProperty(row, "Engines").SequenceEqual(new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }) &&
            Int32Property(row, "ManagedMaxRank") == 1);
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
            "& '" + script.Replace("'", "''", StringComparison.Ordinal) + "' -Suite SpeedGate,SaveGate,LifecycleGate,HeavyLifecycleGate -ListScenarios | ConvertTo-Json -Depth 5 -Compress");
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

    private static int Int32Property(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? result : 0;

    private static double DoubleProperty(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.TryGetDouble(out var result) ? result : 0;
}
