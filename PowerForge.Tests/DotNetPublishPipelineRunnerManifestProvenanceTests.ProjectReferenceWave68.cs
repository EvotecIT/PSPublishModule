using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("LD_LIBRARY_PATH")]
    [InlineData("LD_AUDIT")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("DYLD_LIBRARY_PATH")]
    [InlineData("DYLD_FRAMEWORK_PATH")]
    [InlineData("LIBPATH")]
    [InlineData("SHLIB_PATH")]
    public void TrustedGitEnvironment_ClearsNativeLoaderInjection(string variableName)
    {
        string? previous = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, "ambient-loader-payload");
            Dictionary<string, string?> environment =
                DotNetPublishPipelineRunner.CreateTrustedGitEnvironment(
                    new Dictionary<string, string?>
                    {
                        [variableName] = "requested-loader-payload"
                    });

            Assert.True(environment.ContainsKey(variableName));
            Assert.Null(environment[variableName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Theory]
    [InlineData("vbruntime", false)]
    [InlineData("sdkpath", true)]
    public void ControlledBuildInputs_RejectVisualBasicResponsePathReparsePoint(
        string switchName,
        bool directoryInput)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = directoryInput
                ? Directory.CreateDirectory(Path.Combine(externalRoot, "payload")).FullName
                : Path.Combine(externalRoot, "payload.dll");
            if (!directoryInput)
                File.WriteAllText(externalPath, "external runtime");
            string linkPath = Path.Combine(root, directoryInput ? "payload-link" : "payload-link.dll");
            try
            {
                if (directoryInput)
                    Directory.CreateSymbolicLink(linkPath, externalPath);
                else
                    File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllText(responsePath, $"/{switchName}:{Path.GetFileName(linkPath)}");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><Vbc ResponseFiles=\"compiler.rsp\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("vbruntime", "runtime.dll")]
    [InlineData("sdkpath", "sdk")]
    public void ControlledBuildInputs_AcceptContainedVisualBasicResponsePath(
        string switchName,
        string operand)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string operandPath = Path.Combine(root, operand);
            if (Path.HasExtension(operandPath))
                File.WriteAllText(operandPath, "contained runtime");
            else
                Directory.CreateDirectory(operandPath);
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllText(responsePath, $"/{switchName}:{operand}");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><Vbc ResponseFiles=\"compiler.rsp\" /></Target></Project>");
            var controlledInputs = new List<string> { projectPath, responsePath };
            if (File.Exists(operandPath))
                controlledInputs.Add(operandPath);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                controlledInputs,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
