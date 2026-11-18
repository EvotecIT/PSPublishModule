using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge.Tests;

public sealed class PublishNugetPackageCommandTests
{
    [Fact]
    public void FailedPublish_WritesAnErrorRecordAndReturnsTheResult()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.PublishNugetPackage", Guid.NewGuid().ToString("N")));
        try
        {
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Publish-NugetPackage", typeof(PSPublishModule.PublishNugetPackageCommand), null));
            using var runspace = RunspaceFactory.CreateRunspace(state);
            runspace.Open();
            using var ps = PowerShell.Create(runspace);
            // An empty folder fails before any push, so no feed is contacted.
            ps.AddCommand("Publish-NugetPackage")
                .AddParameter("Path", root.FullName)
                .AddParameter("ApiKey", "not-used")
                .AddParameter("Source", root.FullName);

            var output = ps.Invoke();

            var result = Assert.IsType<PSPublishModule.PublishNugetPackageCommand.PublishNugetPackageResult>(Assert.Single(output).BaseObject);
            Assert.False(result.Success);
            var error = Assert.Single(ps.Streams.Error);
            Assert.StartsWith("PublishNugetPackageFailed", error.FullyQualifiedErrorId, StringComparison.Ordinal);
            Assert.Equal(result.ErrorMessage, error.Exception.Message);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
