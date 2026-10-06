using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishServiceRebuildLockTests
{
    [Fact]
    public void InlineRebuild_ReachesStopPhaseBeforeRejectingRemainingOutputLocks()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Directory.CreateTempSubdirectory("PowerForge.ServiceRebuild.").FullName;
        try
        {
            string output = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            string executable = Path.Combine(output, "Service.exe");
            File.WriteAllText(executable, "existing service payload");
            using var heldFile = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.None);
            var logger = new BufferedLogger();
            var runner = new DotNetPublishPipelineRunner(logger);
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets = [new DotNetPublishTargetPlan
                {
                    Name = "Service",
                    Publish = new DotNetPublishPublishOptions
                    {
                        OutputPath = output,
                        Service = new DotNetPublishServicePackageOptions
                        {
                            ServiceName = "PowerForge.Test.NoServiceCreated",
                            Lifecycle = new DotNetPublishServiceLifecycleOptions
                            {
                                Enabled = true, Mode = DotNetPublishServiceLifecycleMode.InlineRebuild,
                                StopIfExists = true, DeleteIfExists = false, Install = false, WhatIf = true
                            }
                        }
                    }
                }]
            };
            var publish = typeof(DotNetPublishPipelineRunner).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var error = Assert.Throws<TargetInvocationException>(() => publish.Invoke(runner,
                [plan, "Service", "net10.0", "win-x64", DotNetPublishStyle.PortableCompat,
                    "test", null, null, null, Array.Empty<string>(), null]));

            // WhatIf reaches the real lifecycle boundary without touching SCM. The remaining
            // exclusive lock must still block publishing; moving the guard before stop fails this.
            Assert.Contains(logger.Entries, entry => entry.Message.Contains("inline-pre (WhatIf)", StringComparison.Ordinal));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Contains("locked file", error.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("existing service payload".Length, heldFile.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
