using System.Management.Automation;
using System.Text.RegularExpressions;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class AppStoreConnectScreenshotCmdletSourceCommitTests
{
    [Theory]
    [InlineData(typeof(SyncAppStoreConnectScreenshotsCommand))]
    [InlineData(typeof(TestAppStoreConnectScreenshotSyncConfigCommand))]
    public void SourceCommitParameter_AcceptsRepositoryNativeSha1AndSha256ObjectIds(Type commandType)
    {
        var property = commandType.GetProperty("SourceCommit")!;
        var validation = Assert.Single(property.GetCustomAttributes(typeof(ValidatePatternAttribute), inherit: true)
            .Cast<ValidatePatternAttribute>());
        var pattern = validation.RegexPattern;

        Assert.Matches(pattern, new string('a', 40));
        Assert.Matches(pattern, new string('b', 64));
        Assert.DoesNotMatch(new Regex(pattern), new string('c', 39));
        Assert.DoesNotMatch(new Regex(pattern), new string('d', 65));
    }
}
