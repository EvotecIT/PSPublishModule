using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectTrustedXslTransformation()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string inputPath = Path.Combine(root, "payload.xml");
            string transformPath = Path.Combine(root, "transform.xsl");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><XslTransformation XmlInputPaths=\"payload.xml\" XslInputPath=\"transform.xsl\" OutputPaths=\"output.xml\" UseTrustedSettings=\"true\" /></Target></Project>");
            File.WriteAllText(inputPath, "<payload />");
            File.WriteAllText(transformPath, "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" />");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, inputPath, transformPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptExplicitlyUntrustedXslTransformation()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string inputPath = Path.Combine(root, "payload.xml");
            string transformPath = Path.Combine(root, "transform.xsl");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><XslTransformation XmlInputPaths=\"payload.xml\" XslInputPath=\"transform.xsl\" OutputPaths=\"output.xml\" UseTrustedSettings=\"false\" /></Target></Project>");
            File.WriteAllText(inputPath, "<payload />");
            File.WriteAllText(transformPath, "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" />");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, inputPath, transformPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("ExecuteAsTool", "true")]
    [InlineData("SdkToolsPath", "tools")]
    public void ControlledBuildInputs_RejectGenerateResourceToolOverride(
        string attributeName,
        string attributeValue)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string sourcePath = Path.Combine(root, "Resources.txt");
            File.WriteAllText(
                projectPath,
                $"<Project><Target Name=\"Build\"><GenerateResource Sources=\"Resources.txt\" OutputResources=\"obj/Resources.resources\" {attributeName}=\"{attributeValue}\" /></Target></Project>");
            File.WriteAllText(sourcePath, "Name=Value");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, sourcePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("ComReferenceExecuteAsTool", "true")]
    [InlineData("ExecuteAsTool", "true")]
    [InlineData("ResGenExecuteAsTool", "true")]
    [InlineData("ResgenToolPath", "tools")]
    [InlineData("ResGenEnvironment", "BUILD_STAMP=ambient")]
    [InlineData("WinMDExpToolPath", "tools")]
    [InlineData("WinMDExpEnvironment", "BUILD_STAMP=ambient")]
    [InlineData("ResolveComReferenceToolPath", "tools")]
    [InlineData("ResolveComReferenceEnvironment", "BUILD_STAMP=ambient")]
    [InlineData("LCToolPath", "tools")]
    [InlineData("LCEnvironment", "BUILD_STAMP=ambient")]
    [InlineData("SGenToolPath", "tools")]
    [InlineData("SGenEnvironment", "BUILD_STAMP=ambient")]
    public void ControlledBuildInputs_RejectSdkTaskExecutionPropertyOverride(
        string propertyName,
        string propertyValue)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                $"<Project><PropertyGroup><{propertyName}>{propertyValue}</{propertyName}></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("ComReferenceExecuteAsTool")]
    [InlineData("ExecuteAsTool")]
    [InlineData("ResGenExecuteAsTool")]
    public void ControlledBuildInputs_AcceptExplicitlyDisabledSdkTaskExecutionProperty(
        string propertyName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                $"<Project><PropertyGroup><{propertyName}>false</{propertyName}></PropertyGroup></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildEnvironment_RejectsOutputObservableRequestedValue()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.False(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?> { ["BUILD_STAMP"] = "ambient-value" },
                root,
                controlledRoot,
                out _));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
