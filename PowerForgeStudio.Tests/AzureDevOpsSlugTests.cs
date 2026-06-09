using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class AzureDevOpsSlugTests
{
    [Fact]
    public void TryParse_ValidAdoUrl_Succeeds()
    {
        var url = "https://dev.azure.com/myorg/myproject/_git/myrepo";
        var result = ProjectDiscoveryService.TryParseAzureDevOpsSlug(url, out var slug);

        Assert.True(result);
        Assert.Equal("myorg/myproject/myrepo", slug);
    }

    [Fact]
    public void TryParse_NonAdoUrl_ReturnsFalse()
    {
        var url = "https://github.com/owner/repo";
        var result = ProjectDiscoveryService.TryParseAzureDevOpsSlug(url, out _);

        Assert.False(result);
    }
}
