namespace PowerForge.Tests;

public sealed partial class BinaryDependencyPreflightServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_OptionalReferenceExemptsOnlyTheDeclaredEdge(bool rootLayout)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-optional-" + Guid.NewGuid().ToString("N")));
        try
        {
            var fixture = CreateDependencyFixture(root.FullName);
            BuildProject(fixture.ConsumerProjectPath);
            var module = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            var payload = rootLayout ? module.FullName : Directory.CreateDirectory(Path.Combine(module.FullName, "Lib", "Core")).FullName;
            File.Copy(fixture.ConsumerAssemblyPath, Path.Combine(payload, "Consumer.dll"));
            var manifest = Path.Combine(module.FullName, "Module.psd1");
            File.WriteAllText(manifest, rootLayout
                ? "@{ModuleVersion='1.0.0';RequiredAssemblies=@('Consumer.dll')}"
                : "@{ModuleVersion='1.0.0'}");
            var service = new BinaryDependencyPreflightService(new NullLogger());
            var wrongEdge = new Dictionary<string, string[]> { ["Other.dll"] = ["Dependency.dll"] };
            var exactEdge = new Dictionary<string, string[]> { ["consumer.DLL"] = ["dependency.DLL"] };

            Assert.Contains(service.Analyze(module.FullName, "Core", manifest, wrongEdge).Issues,
                issue => issue.MissingDependencyName == "Dependency");
            Assert.False(service.Analyze(module.FullName, "Core", manifest, exactEdge).HasIssues);

            // A different consumer of the same dependency remains blocking.
            File.Copy(fixture.ConsumerAssemblyPath, Path.Combine(payload, "Other.dll"));
            if (rootLayout)
                File.WriteAllText(manifest, "@{ModuleVersion='1.0.0';RequiredAssemblies=@('Consumer.dll','Other.dll')}");
            Assert.Contains(service.Analyze(module.FullName, "Core", manifest, exactEdge).Issues,
                issue => issue.AssemblyFileName == "Other.dll" && issue.MissingDependencyName == "Dependency");

            // A manifest-declared dependency is always required, even if another
            // assembly lists that reference as optional.
            File.WriteAllText(manifest, "@{ModuleVersion='1.0.0';RequiredAssemblies=@('Dependency.dll')}");
            Assert.Contains(service.Analyze(module.FullName, "Core", manifest, exactEdge).Issues,
                issue => issue.AssemblyFileName == "Module.psd1" && issue.MissingDependencyName == "Dependency");
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("*.dll", "Dependency.dll")]
    [InlineData("Consumer.dll", "*.dll")]
    [InlineData("Consumer.dll", "../Dependency.dll")]
    [InlineData("Module.psd1", "Dependency.dll")]
    public void Analyze_RejectsUnboundedOptionalReferenceDeclarations(string consumer, string dependency)
    {
        var declarations = new Dictionary<string, string[]> { [consumer] = [dependency] };
        Assert.Throws<ArgumentException>(() => new BinaryDependencyPreflightService(new NullLogger())
            .Analyze(Path.GetTempPath(), "Core", null, declarations));
    }
}
