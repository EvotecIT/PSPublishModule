using System.Collections.Concurrent;

namespace PowerForge.Tests;

public sealed class AppleMacOSFileChangeObserverTests
{
    [Fact]
    public void Native_exclusion_filters_output_before_delivery_but_preserves_input_events()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            var root = AppleReleaseArtifactService.ResolvePhysicalPath(fixture.FullName);
            var output = Directory.CreateDirectory(Path.Combine(root, "DerivedData")).FullName;
            var input = Directory.CreateDirectory(Path.Combine(root, "SourcePackages")).FullName;
            var allEvents = new ConcurrentQueue<string>();
            var filteredEvents = new ConcurrentQueue<string>();
            var errors = new ConcurrentQueue<Exception>();
            using var all = new AppleMacOSFileChangeObserver(root, (path, _) => allEvents.Enqueue(path), errors.Enqueue);
            using var filtered = new AppleMacOSFileChangeObserver(root, (path, _) => filteredEvents.Enqueue(path), errors.Enqueue, new[] { output });
            var outputFile = Path.Combine(output, "build.log");
            var inputFile = Path.Combine(input, "package.swift");

            File.WriteAllText(outputFile, "mutable compiler output");
            File.WriteAllText(inputFile, "package input");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                Thread.Sleep(50);
                all.Drain();
                filtered.Drain();
            } while ((!allEvents.Contains(outputFile) || !filteredEvents.Contains(inputFile)) && DateTime.UtcNow < deadline);
            all.Stop();
            filtered.Stop();

            Assert.Empty(errors);
            Assert.Contains(outputFile, allEvents);
            Assert.Contains(inputFile, filteredEvents);
            Assert.DoesNotContain(filteredEvents, path => path == output || path.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
        finally { fixture.Delete(recursive: true); }
    }
}
