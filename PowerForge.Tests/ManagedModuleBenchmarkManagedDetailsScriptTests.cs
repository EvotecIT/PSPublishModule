using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkManagedDetailsScriptTests
{
    [Fact]
    public void ManagedDetails_ReportsCoalescedWaitAndSlowestMaterializedPackage()
    {
        using var temp = new TemporaryDirectory();
        var detailPath = Path.Combine(temp.Path, "managed-details.json");

        using var ps = CreateBenchmarkPowerShell($$"""
            $root = [pscustomobject]@{
                Name = 'Company.Root'
                Version = '1.0.0'
                Status = 'Installed'
                ModulePath = '{{EscapePowerShellString(Path.Combine(temp.Path, "Company.Root", "1.0.0"))}}'
                Elapsed = [TimeSpan]::FromMilliseconds(1000)
                VersionResolutionElapsed = [TimeSpan]::Zero
                DownloadElapsed = [TimeSpan]::FromMilliseconds(5)
                ExtractionElapsed = [TimeSpan]::FromMilliseconds(10)
                DependencyElapsed = [TimeSpan]::FromMilliseconds(800)
                PromotionElapsed = [TimeSpan]::FromMilliseconds(2)
                RepositoryRequestCount = 0
                PackageRepositoryRequestCount = 0
                PackageRepositoryRedirectCount = 0
                FileCount = 1
                ExtractedBytes = 100
                ExtractionFromCache = $true
                DependencyResults = @(
                    [pscustomobject]@{
                        Name = 'Company.Wait'
                        Version = '1.0.0'
                        Status = 'AlreadyInstalled'
                        ModulePath = '{{EscapePowerShellString(Path.Combine(temp.Path, "Company.Wait", "1.0.0"))}}'
                        Elapsed = [TimeSpan]::FromMilliseconds(125)
                        VersionResolutionElapsed = [TimeSpan]::Zero
                        DownloadElapsed = [TimeSpan]::Zero
                        ExtractionElapsed = [TimeSpan]::Zero
                        DependencyElapsed = [TimeSpan]::Zero
                        PromotionElapsed = [TimeSpan]::Zero
                        RepositoryRequestCount = 0
                        PackageRepositoryRequestCount = 0
                        PackageRepositoryRedirectCount = 0
                        FileCount = 0
                        ExtractedBytes = 0
                        ExtractionFromCache = $false
                        DependencyResults = @()
                    },
                    [pscustomobject]@{
                        Name = 'Company.Big'
                        Version = '1.0.0'
                        Status = 'Installed'
                        ModulePath = '{{EscapePowerShellString(Path.Combine(temp.Path, "Company.Big", "1.0.0"))}}'
                        Elapsed = [TimeSpan]::FromMilliseconds(450)
                        VersionResolutionElapsed = [TimeSpan]::Zero
                        DownloadElapsed = [TimeSpan]::FromMilliseconds(20)
                        ExtractionElapsed = [TimeSpan]::FromMilliseconds(320)
                        DependencyElapsed = [TimeSpan]::Zero
                        PromotionElapsed = [TimeSpan]::FromMilliseconds(70)
                        RepositoryRequestCount = 0
                        PackageRepositoryRequestCount = 0
                        PackageRepositoryRedirectCount = 0
                        FileCount = 10
                        ExtractedBytes = 1000
                        ExtractionFromCache = $true
                        DependencyResults = @()
                    }
                )
            }

            Write-ManagedInstallDetail -Result $root -Path '{{EscapePowerShellString(detailPath)}}'
            (Get-Content -LiteralPath '{{EscapePowerShellString(detailPath)}}' -Raw | ConvertFrom-Json).Summary
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var summary = Assert.Single(results);
        Assert.Equal(1.0, NumericProperty(summary, "CoalescedWaitCount"));
        Assert.Equal(125.0, NumericProperty(summary, "TotalCoalescedWaitMilliseconds"));
        Assert.Equal("Company.Wait", Property(summary, "SlowestCoalescedWaitName"));
        Assert.Equal(125.0, NumericProperty(summary, "SlowestCoalescedWaitMilliseconds"));
        Assert.Equal("Company.Big", Property(summary, "SlowestMaterializedPackageName"));
        Assert.Equal(450.0, NumericProperty(summary, "SlowestMaterializedPackageMilliseconds"));
        Assert.Equal(320.0, NumericProperty(summary, "SlowestMaterializedPackageExtractionMilliseconds"));
        Assert.Equal(70.0, NumericProperty(summary, "SlowestMaterializedPackagePromotionMilliseconds"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var detailScript = Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "ManagedModuleBenchmark.ManagedDetails.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(detailScript) + Environment.NewLine + script);
        return ps;
    }

    private static string Property(PSObject value, string name)
        => (string)value.Properties[name].Value;

    private static double NumericProperty(PSObject value, string name)
        => Convert.ToDouble(value.Properties[name].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
