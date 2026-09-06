using System.Xml.Linq;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    /// <summary>
    /// Projects the actual build template's properties into isolated acquisition. SDK-injected
    /// dependencies must see the same single-file, trimming, AOT, and analyzer switches at restore.
    /// </summary>
    internal static string RenderRestoreProjectProperties(PowerShellCompilationTargetContract target)
    {
        var templateName = target.ArtifactKind switch
        {
            PowerShellCompilationArtifactKind.Executable when target.Mode == PowerShellCompilationMode.Strict => TypedExecutableProjectTemplate,
            PowerShellCompilationArtifactKind.Executable => PackagedProjectTemplate,
            PowerShellCompilationArtifactKind.BinaryModule => BinaryModuleProjectTemplate,
            _ => TypedProjectTemplate
        };
        var document = XDocument.Parse(ReadTemplate(templateName));
        var nativeAot = target.Deployment == PowerShellCompilationDeploymentModel.NativeAot;
        var trimmed = target.Deployment is PowerShellCompilationDeploymentModel.Trimmed or PowerShellCompilationDeploymentModel.NativeAot;
        var selfContained = target.Deployment is PowerShellCompilationDeploymentModel.SelfContained or
            PowerShellCompilationDeploymentModel.Trimmed or PowerShellCompilationDeploymentModel.NativeAot or PowerShellCompilationDeploymentModel.ReadyToRun;
        var properties = string.Join(Environment.NewLine, document.Root!.Elements("PropertyGroup")
            .Select(static group => group.ToString(SaveOptions.DisableFormatting)))
            .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(target.TargetFramework))
            .Replace("{{ARTIFACT_NAME}}", "Restore")
            .Replace("{{ASSEMBLY_VERSION}}", "1.0.0.0")
            .Replace("{{SINGLE_FILE}}", target.SingleFile && !nativeAot ? "true" : "false")
            .Replace("{{SELF_CONTAINED}}", selfContained ? "true" : "false")
            .Replace("{{PUBLISH_TRIMMED}}", trimmed ? "true" : "false")
            .Replace("{{PUBLISH_AOT}}", nativeAot ? "true" : "false");
        if (properties.Contains("{{"))
            throw new InvalidOperationException("The generated project's restore properties contain an unresolved template value.");
        return properties;
    }
}
