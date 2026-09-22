using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    private static void VerifyProviderFromLocalFeed(string root, PowerShellCompilationBuildResult result)
    {
        var feed = Directory.CreateDirectory(Path.Combine(root, "provider-feed")).FullName;
        var package = new PowerShellCompilationLibraryPackageBuilder().Build(
            new PowerShellCompilationLibraryPackageBuildRequest(
                result,
                Path.Combine(feed, "Generated.ReviewedProvider.1.0.0.nupkg"),
                "Generated.ReviewedProvider",
                "1.0.0"));
        Assert.Contains("lib/net10.0/Generic.Semantic.Provider.dll", package.Files);
        Assert.Contains("powerforge/provider-lock.json", package.Files);

        var consumer = Directory.CreateDirectory(Path.Combine(root, "provider-consumer")).FullName;
        var project = Path.Combine(consumer, "ProviderConsumer.csproj");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("ImplicitUsings", "enable"),
                new System.Xml.Linq.XElement("RestoreSources", feed),
                new System.Xml.Linq.XElement("RestorePackagesPath", Path.Combine(consumer, ".packages"))),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageReference",
                new System.Xml.Linq.XAttribute("Include", "Generated.ReviewedProvider"),
                new System.Xml.Linq.XAttribute("Version", "1.0.0"))))).Save(project);
        var abi = result.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(consumer, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            var information = new List<string>();
            Methods.Write_PackageNotice(
                _ => { }, _ => { }, _ => { }, _ => { }, information.Add, _ => { }, _ => { });
            if (!information.SequenceEqual(new[] { "provider:locked" })) return 1;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 2;
            Console.WriteLine("provider local-feed consumer passed");
            return 0;
            """);
        var build = RunProviderConsumer("dotnet", consumer, "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.Succeeded, build.StdOut + build.StdErr);
        var run = RunProviderConsumer("dotnet", consumer,
            Path.Combine(consumer, "bin", "Release", "net10.0", "ProviderConsumer.dll"));
        Assert.True(run.Succeeded, run.StdOut + run.StdErr);
        Assert.Contains("provider local-feed consumer passed", run.StdOut, StringComparison.Ordinal);
    }

    private static ProcessRunResult RunProviderConsumer(string executable, string workingDirectory, params string[] arguments)
        => new ProcessRunner().RunAsync(new ProcessRunRequest(
            executable, workingDirectory, arguments, TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();

    private static async Task VerifyProviderMatrixFromLocalFeed(
        string root,
        PowerShellCompilationBuildResult result,
        string cleanupPath,
        string cancellationPath)
    {
        var feed = Directory.CreateDirectory(Path.Combine(root, "provider-matrix-feed")).FullName;
        var package = new PowerShellCompilationLibraryPackageBuilder().Build(
            new PowerShellCompilationLibraryPackageBuildRequest(
                result,
                Path.Combine(feed, "Generated.ProviderMatrix.1.0.0.nupkg"),
                "Generated.ProviderMatrix",
                "1.0.0"));
        Assert.Contains("lib/net10.0/Generic.Semantic.Provider.dll", package.Files);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(package.PackagePath))
        using (var reader = new StreamReader(archive.GetEntry("lib/net10.0/ProviderMatrixStrict.xml")!.Open()))
        {
            var documentation = reader.ReadToEnd();
            Assert.Contains("name=\"__writeOutput\"", documentation, StringComparison.Ordinal);
            Assert.Contains("name=\"__providerCancellationToken\"", documentation, StringComparison.Ordinal);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(cancellationPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cleanupPath)!);

        var consumer = Directory.CreateDirectory(Path.Combine(root, "provider-matrix-consumer")).FullName;
        var project = Path.Combine(consumer, "ProviderMatrixConsumer.csproj");
        WriteConsumerProject(project, "Generated.ProviderMatrix", feed);
        var abi = result.Manifest!.PublicAbi!;
        var program = "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            static Action<object?> Output(List<object?> values) => values.Add;
            static Action<string> Ignore() => _ => { };
            static object[] Sinks(List<object?>? output = null, List<string>? information = null) => new object[] {
                output is null ? IgnoreObject() : Output(output), Ignore(), Ignore(), Ignore(),
                information is null ? Ignore() : information.Add, Ignore(), Ignore()
            };
            static Action<object?> IgnoreObject() => _ => { };
            static void Require(bool value, string message) { if (!value) throw new Exception(message); }
            static void InvokeMatrix() {
                var output = new List<object?>(); var information = new List<string>();
                Methods.Invoke_ProviderMatrix(Output(output), Ignore(), Ignore(), Ignore(), information.Add, Ignore(), Ignore());
                Require(output.Count == 9 && Equals(output[0], "provider:value") && Equals(output[8], 3.5d), "provider output");
                Require(information.SequenceEqual(new[] { "provider:information" }), "provider information");
            }
            InvokeMatrix();
            try { Methods.Invoke_ProviderFailure(IgnoreObject(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore()); throw new Exception("failure returned"); }
            catch (InvalidOperationException error) { Require(error.Message.Contains("provider-failure:broken"), "failure contract"); }
            const string cancellationPath = {{CANCELLATION_PATH}};
            using (var cancellation = new CancellationTokenSource()) {
                var canceled = false;
                var invocation = Task.Factory.StartNew(() => { try { Methods.Invoke_ProviderCancellationWrapper(IgnoreObject(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore(), cancellation.Token); }
                    catch (OperationCanceledException) { canceled = true; } }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                var started = SpinWait.SpinUntil(() => File.Exists(cancellationPath) || invocation.IsCompleted, TimeSpan.FromSeconds(5));
                if (!started || !File.Exists(cancellationPath)) { await invocation; throw new Exception("cancellation provider returned without starting"); }
                cancellation.Cancel(); await invocation; Require(canceled, "cancellation contract");
            }
            using (new FileStream(cancellationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cancellationPath);
            const string cleanupPath = {{CLEANUP_PATH}};
            var cleanupInformation = new List<string>();
            Methods.Invoke_ProviderCleanup(IgnoreObject(), Ignore(), Ignore(), Ignore(), cleanupInformation.Add, Ignore(), Ignore());
            Require(cleanupInformation.SequenceEqual(new[] { "released:" + cleanupPath }), "cleanup result");
            using (new FileStream(cleanupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cleanupPath);
            var cleanupFailurePath = cleanupPath + ".failure";
            try { Methods.Invoke_ProviderCleanupFailure(IgnoreObject(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore(), Ignore()); throw new Exception("cleanup failure returned"); }
            catch (InvalidOperationException error) { Require(error.Message.Contains("provider-cleanup-failure:"), "cleanup failure contract"); }
            using (new FileStream(cleanupFailurePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cleanupFailurePath);
            InvokeMatrix();
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation"), "PowerShell loaded");
            Console.WriteLine("provider matrix local-feed consumer passed");
            """
            .Replace("{{CANCELLATION_PATH}}", System.Text.Json.JsonSerializer.Serialize(cancellationPath), StringComparison.Ordinal)
            .Replace("{{CLEANUP_PATH}}", System.Text.Json.JsonSerializer.Serialize(cleanupPath), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(consumer, "Program.cs"), program);
        var build = RunProviderConsumer("dotnet", consumer, "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.Succeeded, build.StdOut + build.StdErr);
        var run = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet", consumer, new[] { Path.Combine(consumer, "bin", "Release", "net10.0", "ProviderMatrixConsumer.dll") }, TimeSpan.FromMinutes(2)));
        Assert.True(run.Succeeded, run.StdOut + run.StdErr);
        Assert.Contains("provider matrix local-feed consumer passed", run.StdOut, StringComparison.Ordinal);
    }

    private static void VerifyDependencyProviderFromLocalFeed(string root, PowerShellCompilationBuildResult result, string? existingPackage = null)
    {
        var feed = Directory.CreateDirectory(Path.Combine(root, "provider-dependency-feed")).FullName;
        var packagePath = Path.Combine(feed, "Generated.ProviderDependency.1.0.0.nupkg");
        if (existingPackage is not null) File.Copy(existingPackage, packagePath);
        else new PowerShellCompilationLibraryPackageBuilder().Build(
            new PowerShellCompilationLibraryPackageBuildRequest(
                result,
                packagePath,
                "Generated.ProviderDependency",
                "1.0.0"));
        using (var package = new NuGet.Packaging.PackageArchiveReader(packagePath))
        {
            Assert.Contains("lib/net10.0/Generic.Semantic.Provider.WithDependency.dll", package.GetFiles());
            Assert.Contains("lib/net10.0/Generic.Semantic.Provider.Dependency.dll", package.GetFiles());
        }
        var consumer = Directory.CreateDirectory(Path.Combine(root, "provider-dependency-consumer")).FullName;
        var project = Path.Combine(consumer, "ProviderDependencyConsumer.csproj");
        WriteConsumerProject(project, "Generated.ProviderDependency", feed);
        var abi = result.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(consumer, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            var information = new List<string>();
            Methods.Write_PackageDependency(_ => { }, _ => { }, _ => { }, _ => { }, information.Add, _ => { }, _ => { });
            if (!information.SequenceEqual(new[] { "dependency:locked" })) return 1;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 2;
            Console.WriteLine("provider dependency local-feed consumer passed");
            return 0;
            """);
        var build = RunProviderConsumer("dotnet", consumer, "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.Succeeded, build.StdOut + build.StdErr);
        var run = RunProviderConsumer("dotnet", consumer,
            Path.Combine(consumer, "bin", "Release", "net10.0", "ProviderDependencyConsumer.dll"));
        Assert.True(run.Succeeded, run.StdOut + run.StdErr);
        Assert.Contains("provider dependency local-feed consumer passed", run.StdOut, StringComparison.Ordinal);
    }

    private static void WriteConsumerProject(string project, string packageId, string feed)
        => new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("ImplicitUsings", "enable"),
                new System.Xml.Linq.XElement("RestoreSources", feed),
                new System.Xml.Linq.XElement("RestorePackagesPath", Path.Combine(Path.GetDirectoryName(project)!, ".packages"))),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageReference",
                new System.Xml.Linq.XAttribute("Include", packageId),
                new System.Xml.Linq.XAttribute("Version", "1.0.0"))))).Save(project);
}
