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
                ExtractionCacheLockWaitElapsed = [TimeSpan]::FromMilliseconds(1)
                DependencyElapsed = [TimeSpan]::FromMilliseconds(800)
                PromotionElapsed = [TimeSpan]::FromMilliseconds(2)
                PromotionLockWaitElapsed = [TimeSpan]::FromMilliseconds(0.5)
                PromotionMoveElapsed = [TimeSpan]::FromMilliseconds(1.5)
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
                        CoalescedWaitElapsed = [TimeSpan]::FromMilliseconds(90)
                        InstallLockWaitElapsed = [TimeSpan]::FromMilliseconds(30)
                        VersionResolutionElapsed = [TimeSpan]::Zero
                        DownloadElapsed = [TimeSpan]::Zero
                        ExtractionElapsed = [TimeSpan]::Zero
                        ExtractionCacheLockWaitElapsed = [TimeSpan]::Zero
                        DependencyElapsed = [TimeSpan]::Zero
                        PromotionElapsed = [TimeSpan]::Zero
                        PromotionLockWaitElapsed = [TimeSpan]::Zero
                        PromotionMoveElapsed = [TimeSpan]::Zero
                        RepositoryRequestCount = 0
                        PackageRepositoryRequestCount = 0
                        PackageRepositoryRedirectCount = 0
                        FileCount = 0
                        ExtractedBytes = 0
                        ExtractionFromCache = $false
                        DependencyResults = @()
                    },
                    [pscustomobject]@{
                        Name = 'Company.NoOp'
                        Version = '1.0.0'
                        Status = 'AlreadyInstalled'
                        ModulePath = '{{EscapePowerShellString(Path.Combine(temp.Path, "Company.NoOp", "1.0.0"))}}'
                        Elapsed = [TimeSpan]::FromMilliseconds(75)
                        CoalescedWaitElapsed = [TimeSpan]::Zero
                        InstallLockWaitElapsed = [TimeSpan]::Zero
                        VersionResolutionElapsed = [TimeSpan]::Zero
                        DownloadElapsed = [TimeSpan]::Zero
                        ExtractionElapsed = [TimeSpan]::Zero
                        ExtractionCacheLockWaitElapsed = [TimeSpan]::Zero
                        DependencyElapsed = [TimeSpan]::Zero
                        PromotionElapsed = [TimeSpan]::Zero
                        PromotionLockWaitElapsed = [TimeSpan]::Zero
                        PromotionMoveElapsed = [TimeSpan]::Zero
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
                        InstallLockWaitElapsed = [TimeSpan]::FromMilliseconds(40)
                        VersionResolutionElapsed = [TimeSpan]::Zero
                        DownloadElapsed = [TimeSpan]::FromMilliseconds(20)
                        ExtractionElapsed = [TimeSpan]::FromMilliseconds(320)
                        ExtractionCacheLockWaitElapsed = [TimeSpan]::FromMilliseconds(15)
                        DependencyElapsed = [TimeSpan]::FromMilliseconds(65)
                        PromotionElapsed = [TimeSpan]::FromMilliseconds(70)
                        PromotionLockWaitElapsed = [TimeSpan]::FromMilliseconds(12)
                        PromotionMoveElapsed = [TimeSpan]::FromMilliseconds(58)
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
            $detail = Get-Content -LiteralPath '{{EscapePowerShellString(detailPath)}}' -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Summary = $detail.Summary
                WaitPackage = @($detail.Packages | Where-Object Name -eq 'Company.Wait')[0]
                NoOpPackage = @($detail.Packages | Where-Object Name -eq 'Company.NoOp')[0]
                BigPackage = @($detail.Packages | Where-Object Name -eq 'Company.Big')[0]
            }
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var output = Assert.Single(results);
        var summary = (PSObject)output.Properties["Summary"].Value;
        var waitPackage = (PSObject)output.Properties["WaitPackage"].Value;
        var noOpPackage = (PSObject)output.Properties["NoOpPackage"].Value;
        var bigPackage = (PSObject)output.Properties["BigPackage"].Value;
        Assert.Equal(1.0, NumericProperty(summary, "CoalescedWaitCount"));
        Assert.Equal(90.0, NumericProperty(summary, "TotalCoalescedWaitMilliseconds"));
        Assert.Equal("Company.Wait", Property(summary, "SlowestCoalescedWaitName"));
        Assert.Equal(90.0, NumericProperty(summary, "SlowestCoalescedWaitMilliseconds"));
        Assert.Equal(2.0, NumericProperty(summary, "InstallLockWaitCount"));
        Assert.Equal(70.0, NumericProperty(summary, "TotalInstallLockWaitMilliseconds"));
        Assert.Equal("Company.Big", Property(summary, "SlowestInstallLockWaitName"));
        Assert.Equal(40.0, NumericProperty(summary, "SlowestInstallLockWaitMilliseconds"));
        Assert.Equal(65.0, NumericProperty(summary, "TotalDependencyMilliseconds"));
        Assert.Equal("Company.Big", Property(summary, "SlowestDependencyPackageName"));
        Assert.Equal("Company.Root", Property(summary, "SlowestDependencyPackageParent"));
        Assert.Equal(65.0, NumericProperty(summary, "SlowestDependencyPackageMilliseconds"));
        Assert.Equal(90.0, NumericProperty(waitPackage, "CoalescedWaitMilliseconds"));
        Assert.Equal(30.0, NumericProperty(waitPackage, "InstallLockWaitMilliseconds"));
        Assert.Equal(0.0, NumericProperty(noOpPackage, "CoalescedWaitMilliseconds"));
        Assert.Equal(0.0, NumericProperty(noOpPackage, "InstallLockWaitMilliseconds"));
        Assert.Equal(0.0, NumericProperty(bigPackage, "CoalescedWaitMilliseconds"));
        Assert.Equal(40.0, NumericProperty(bigPackage, "InstallLockWaitMilliseconds"));
        Assert.Equal(15.0, NumericProperty(bigPackage, "ExtractionCacheLockWaitMilliseconds"));
        Assert.Equal("Company.Big", Property(summary, "SlowestMaterializedPackageName"));
        Assert.Equal(450.0, NumericProperty(summary, "SlowestMaterializedPackageMilliseconds"));
        Assert.Equal(320.0, NumericProperty(summary, "SlowestMaterializedPackageExtractionMilliseconds"));
        Assert.Equal(16.0, NumericProperty(summary, "TotalExtractionCacheLockWaitMilliseconds"));
        Assert.Equal(15.0, NumericProperty(summary, "SlowestMaterializedPackageExtractionCacheLockWaitMilliseconds"));
        Assert.Equal(70.0, NumericProperty(summary, "SlowestMaterializedPackagePromotionMilliseconds"));
        Assert.Equal(12.0, NumericProperty(summary, "SlowestMaterializedPackagePromotionLockWaitMilliseconds"));
        Assert.Equal(58.0, NumericProperty(summary, "SlowestMaterializedPackagePromotionMoveMilliseconds"));
        Assert.Equal(12.5, NumericProperty(summary, "TotalPromotionLockWaitMilliseconds"));
        Assert.Equal(59.5, NumericProperty(summary, "TotalPromotionMoveMilliseconds"));
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
