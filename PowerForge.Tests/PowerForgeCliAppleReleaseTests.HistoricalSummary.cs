using System.Reflection;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliAppleReleaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppleRelease_HistoricalPlanAndValidationSummaryPreserveRequestedIdentity(bool planOnly)
    {
        var program = Assembly.LoadFrom(GetCliPath(FindRepositoryRoot())).GetType("Program", throwOnError: true)!;
        var method = program.GetMethod("CreateAppleSummaryElement", BindingFlags.Static | BindingFlags.NonPublic)!;
        var root = Path.GetTempPath();
        var result = new PowerForgeReleaseResult
        {
            AppleAppPlan = new()
            {
                ProjectRoot = root, ReceiptPath = Path.Combine(root, "receipt.json"), PlanReceiptPath = Path.Combine(root, "plan.json"),
                Action = PowerForgeAppleReleaseAction.Status, ObservedMarketingVersion = "2.2",
                Apps =
                [
                    new() { Name = "iOS", MarketingVersion = "2.3", BuildNumber = "30", DistributionRoute = AppleDistributionRoute.AppStore },
                    new() { Name = "Direct Mac", MarketingVersion = "3.0", BuildNumber = "40", DistributionRoute = AppleDistributionRoute.DirectNotarized }
                ]
            }
        };
        var summary = (JsonElement)method.Invoke(null,
            [result, new PowerForgeReleaseRequest { PlanOnly = planOnly, ValidateOnly = !planOnly }])!;
        var targets = summary.GetProperty("targets").EnumerateArray().ToArray();
        Assert.Equal("2.2", targets[0].GetProperty("marketingVersion").GetString());
        Assert.True(!targets[0].TryGetProperty("buildNumber", out var build) || build.ValueKind == JsonValueKind.Null);
        Assert.Equal("3.0", targets[1].GetProperty("marketingVersion").GetString());
        Assert.Equal("40", targets[1].GetProperty("buildNumber").GetString());
    }
}
