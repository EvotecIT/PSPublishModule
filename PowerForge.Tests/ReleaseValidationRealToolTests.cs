namespace PowerForge.Tests;

public sealed class ReleaseValidationRealToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.RealTool.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationRealToolTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Real_local_package_installs_both_modes_and_rejects_an_unexported_command_without_probing_it()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var feed = Path.Combine(_root, "feed");
        File.WriteAllText(Path.Combine(source, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        File.WriteAllText(Path.Combine(source, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
                <PackAsTool>true</PackAsTool><ToolCommandName>validation-fixture</ToolCommandName>
                <PackageId>PowerForge.Validation.Fixture</PackageId><Version>2.3.4</Version>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(source, "Program.cs"), "System.Console.WriteLine(\"2.3.4\");");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var service = new ReleaseValidationService();
        var packed = await service.RunCommandAsync(new() {
            FileName = "dotnet", WorkingDirectory = source,
            Arguments = ["pack", Path.Combine(source, "Fixture.csproj"), "-c", "Release", "-o", feed, "--nologo"], TimeoutSeconds = 90
        }, cancellationToken: deadline.Token);
        Assert.True(packed.Succeeded, packed.StdErr);
        var package = Assert.Single(Directory.GetFiles(feed, "*.nupkg"));
        File.Move(package, Path.Combine(feed, "renamed-tool.nupkg"));

        var tool = new DotNetToolValidation { PackageRoot = feed, PackageId = "PowerForge.Validation.Fixture",
            CommandName = "validation-fixture", IncludeManifestInstall = true,
            Commands = [new() { FileName = "{ToolPath}", ExpectedOutput = "2.3.4" }] };
        var valid = await service.RunAsync(new() { Tools = [tool] }, cancellationToken: deadline.Token);
        Assert.True(valid.Success, string.Join("; ", valid.Errors));
        Assert.Contains("Command (tool path)", valid.Checks);
        Assert.Contains("Command (manifest)", valid.Checks);

        tool.CommandName = "not-exported";
        tool.Commands = [];
        var invalid = await service.RunAsync(new() { Tools = [tool] }, cancellationToken: deadline.Token);
        Assert.False(invalid.Success);
        Assert.Contains("did not provide command 'not-exported'", Assert.Single(invalid.Errors));
        Assert.DoesNotContain("Install PowerForge.Validation.Fixture (manifest)", invalid.Checks);
        Assert.True(File.Exists(Path.Combine(feed, "renamed-tool.nupkg")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
