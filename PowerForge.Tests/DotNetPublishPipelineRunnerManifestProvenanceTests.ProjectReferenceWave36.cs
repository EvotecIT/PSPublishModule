using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RejectsCaseDistinctConfiguredSmudgeFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string librarySource = Path.Combine(libraryDirectory, "Library.cs");
            string placeholderPath = Path.Combine(cacheDirectory, "placeholder.cs");
            string payloadPath = Path.Combine(cacheDirectory, "payload.cs");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            const string placeholder = "public static class Library { public const int Value = 1; }";
            const string payload = "public static class Library { public const int Value = 2; }";
            File.WriteAllText(librarySource, placeholder);
            File.WriteAllText(placeholderPath, placeholder);
            File.WriteAllText(payloadPath, payload);
            File.WriteAllText(Path.Combine(root, ".gitattributes"), "src/Library/Library.cs filter=Payload\n");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string gitPlaceholderPath = placeholderPath.Replace('\\', '/');
            string gitPayloadPath = payloadPath.Replace('\\', '/');
            RunGit(root, $"config filter.payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(root, $"config filter.payload.smudge \"cat '{gitPlaceholderPath}'\"");
            RunGit(root, $"config filter.Payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(root, $"config filter.Payload.smudge \"cat '{gitPayloadPath}'\"");
            RunGit(root, "config filter.Payload.required true");
            File.Delete(librarySource);
            RunGit(root, "checkout -- src/Library/Library.cs");
            Assert.Equal(payload, File.ReadAllText(librarySource));
            Assert.Equal(string.Empty, RunGit(root, "status --porcelain").Trim());

            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsCaseDistinctWorktreeScopedSmudgeFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string linkedParent = Directory.CreateTempSubdirectory().FullName;
        string linkedRoot = Path.Combine(linkedParent, "linked");
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            const string placeholder = "public static class Library { public const int Value = 1; }";
            const string payload = "public static class Library { public const int Value = 2; }";
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), placeholder);
            File.WriteAllText(Path.Combine(root, ".gitattributes"), "src/Library/Library.cs filter=Payload\n");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunGit(root, "config extensions.worktreeConfig true");
            RunGit(root, $"worktree add -b provenance-linked \"{linkedRoot.Replace('\\', '/')}\"");

            string linkedAppProject = Path.Combine(linkedRoot, "src", "App", "App.csproj");
            string linkedLibraryProject = Path.Combine(linkedRoot, "src", "Library", "Library.csproj");
            string linkedLibrarySource = Path.Combine(linkedRoot, "src", "Library", "Library.cs");
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(linkedRoot, ".cache")).FullName;
            string placeholderPath = Path.Combine(cacheDirectory, "placeholder.cs");
            string payloadPath = Path.Combine(cacheDirectory, "payload.cs");
            File.WriteAllText(placeholderPath, placeholder);
            File.WriteAllText(payloadPath, payload);
            string gitPlaceholderPath = placeholderPath.Replace('\\', '/');
            string gitPayloadPath = payloadPath.Replace('\\', '/');
            RunGit(linkedRoot, $"config --worktree filter.payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(linkedRoot, $"config --worktree filter.payload.smudge \"cat '{gitPlaceholderPath}'\"");
            RunGit(linkedRoot, $"config --worktree filter.Payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(linkedRoot, $"config --worktree filter.Payload.smudge \"cat '{gitPayloadPath}'\"");
            RunGit(linkedRoot, "config --worktree filter.Payload.required true");
            File.Delete(linkedLibrarySource);
            RunGit(linkedRoot, "checkout -- src/Library/Library.cs");
            Assert.Equal(payload, File.ReadAllText(linkedLibrarySource));
            Assert.Equal(string.Empty, RunGit(linkedRoot, "status --porcelain").Trim());
            RunDotNet(linkedRoot, $"restore \"{linkedAppProject}\" --use-lock-file --nologo");
            RunDotNet(linkedRoot, $"build \"{linkedLibraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    linkedRoot,
                    buildProjectPaths: [linkedAppProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                try { RunGit(root, $"worktree remove --force \"{linkedRoot.Replace('\\', '/')}\""); }
                catch { }
            }
            DeleteTestRepository(linkedParent);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_AllowsUninitializedUnrelatedRecordedSubmodule()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(leafRoot, "README.md"), "recorded submodule");
            RunGit(leafRoot, "add .");
            RunGit(leafRoot, "commit -m \"approved leaf\"");

            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config protocol.file.allow always");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(
                root,
                $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" vendor/Unused");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            RunGit(root, "submodule deinit -f -- vendor/Unused");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsCustomSubmoduleUpdateCommandPayload()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(
                Path.Combine(leafRoot, "Leaf.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(leafRoot, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(leafRoot, ".gitignore"), "bin/\nobj/\npackages.lock.json\n");
            RunGit(leafRoot, "add .");
            RunGit(leafRoot, "commit -m \"approved leaf\"");

            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config protocol.file.allow always");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="../Leaf/Leaf.cs" Link="Leaf.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunGit(
                root,
                $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            string leafSource = Path.Combine(root, "src", "Leaf", "Leaf.cs");
            string payloadSource = Path.Combine(cacheDirectory, "Leaf.cs");
            const string payload = "public static class Leaf { public const int Value = 2; }";
            File.WriteAllText(payloadSource, payload);
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(leafSource, payload);
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            RunGit(Path.Combine(root, "src", "Leaf"), "checkout -- Leaf.cs");
            Assert.Equal(string.Empty, RunGit(Path.Combine(root, "src", "Leaf"), "status --porcelain").Trim());
            Assert.Equal(string.Empty, RunGit(root, "status --porcelain").Trim());

            string updateScript = Path.Combine(cacheDirectory, "update.sh");
            string payloadSourceForShell = payloadSource.Replace('\\', '/').Replace("'", "'\\''");
            File.WriteAllText(
                updateScript,
                $"#!/bin/sh\ngit checkout -f \"$1\"\ncp '{payloadSourceForShell}' Leaf.cs\ngit update-index --assume-unchanged Leaf.cs\n");
            RunGit(root, $"config submodule.src/Leaf.update \"!sh '{updateScript.Replace('\\', '/')}'\"");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsCaseDistinctSubmoduleSmudgeFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(leafRoot, ".gitattributes"), "Leaf.cs filter=Payload\n");
            File.WriteAllText(Path.Combine(leafRoot, ".gitignore"), "bin/\nobj/\npackages.lock.json\n");
            const string placeholder = "public static class Leaf { public const int Value = 1; }";
            const string payload = "public static class Leaf { public const int Value = 2; }";
            File.WriteAllText(Path.Combine(leafRoot, "Leaf.cs"), placeholder);
            RunGit(leafRoot, "add .");
            RunGit(leafRoot, "commit -m \"approved leaf\"");

            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config protocol.file.allow always");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../Leaf/Leaf.cs" Link="Leaf.cs" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunGit(
                root,
                $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            string placeholderPath = Path.Combine(cacheDirectory, "placeholder.cs");
            string payloadPath = Path.Combine(cacheDirectory, "payload.cs");
            File.WriteAllText(placeholderPath, placeholder);
            File.WriteAllText(payloadPath, payload);
            string submoduleRoot = Path.Combine(root, "src", "Leaf");
            string submoduleSource = Path.Combine(submoduleRoot, "Leaf.cs");
            string gitPlaceholderPath = placeholderPath.Replace('\\', '/');
            string gitPayloadPath = payloadPath.Replace('\\', '/');
            RunGit(submoduleRoot, $"config filter.payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(submoduleRoot, $"config filter.payload.smudge \"cat '{gitPlaceholderPath}'\"");
            RunGit(submoduleRoot, $"config filter.Payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(submoduleRoot, $"config filter.Payload.smudge \"cat '{gitPayloadPath}'\"");
            RunGit(submoduleRoot, "config filter.Payload.required true");
            File.Delete(submoduleSource);
            RunGit(submoduleRoot, "checkout -- Leaf.cs");
            Assert.Equal(payload, File.ReadAllText(submoduleSource));
            Assert.Equal(string.Empty, RunGit(submoduleRoot, "status --porcelain").Trim());
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_PreservesDuplicateAssignmentPropertyContext()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <PropertyGroup><Ctx>A=9;B=9</Ctx></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(Ctx)" />
                  </ItemGroup>
                  <PropertyGroup><Ctx>A=1;B=2</Ctx></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(Ctx)" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }
}
