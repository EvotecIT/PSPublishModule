using System.Reflection;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderPackagingDestinationTests
{
    [Fact]
    public void CreateModulePackageCopyPlan_RejectsCaseCollidingDestinationsOnCaseInsensitiveOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root, "staging")).FullName;
            string destinationRoot = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            if (FrameworkCompatibility.GetPathStringComparisonForPath(destinationRoot) !=
                StringComparison.OrdinalIgnoreCase)
            {
                return;
            }

            string[] sources =
            [
                Path.Combine(stagingRoot, "Resources", "Data.json"),
                Path.Combine(stagingRoot, "Resources", "data.json")
            ];
            MethodInfo? method = typeof(ArtefactBuilder).GetMethod(
                "CreateModulePackageCopyPlan",
                BindingFlags.Static | BindingFlags.NonPublic);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { stagingRoot, destinationRoot, sources }));

            InvalidOperationException collision = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("same destination", collision.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("output filesystem", collision.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
