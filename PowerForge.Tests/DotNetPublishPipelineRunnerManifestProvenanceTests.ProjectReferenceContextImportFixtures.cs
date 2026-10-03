namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    private static string WriteContextualImportFixture(
        string scenario, string root, string sharedDirectory)
    {
        bool externalAlias = scenario == "context-activated-external-alias-import";
        bool chained = scenario == "context-activated-chained-import";
        bool overrideActivated = scenario is "override-activated-import" or
            "second-context-activated-import";
        bool indirect = scenario == "context-activated-indirect-import";
        bool propertyMethod = scenario == "context-activated-property-method-import";
        bool wildcardAlias = scenario == "wildcard-context-import-alias";
        bool stablePathAlias = scenario is "stable-context-import-alias" or
            "malicious-context-import-alias" or "wildcard-context-import-alias";
        bool execImport = scenario == "context-import-exec";
        bool absoluteImport = scenario == "context-import-absolute";
        bool dynamicOutput = scenario == "context-import-dynamic-output";
        bool malicious = execImport || absoluteImport || dynamicOutput || scenario == "context-activated-import" || externalAlias || chained ||
            overrideActivated || indirect || propertyMethod ||
            scenario == "malicious-context-import-alias";

        string import = externalAlias
            ? "<Import Project=\"Context.aliases.props\" /><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
            : chained
                ? "<Import Project=\"Context.aliases.props\" Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\" /><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
            : overrideActivated
                ? "<Import Project=\"Context.targets\" Condition=\"'$(BuildProjectReferences)' == 'false'" +
                  (scenario == "second-context-activated-import" ? " and '$(Flavor)' == 'Direct'" : "") + "\" />"
            : scenario == "inactive-override-import"
                ? "<Import Project=\"Missing.targets\" Condition=\"'false' == 'true' and '$(BuildProjectReferences)' == 'false'\" />"
            : indirect
                ? "<PropertyGroup><ContextImportEnabled>$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))</ContextImportEnabled></PropertyGroup><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
            : stablePathAlias
                ? $"<PropertyGroup><ContextTargetsPath>{(wildcardAlias ? "Context*.targets" : "Context.targets")}</ContextTargetsPath></PropertyGroup><Import Project=\"$(ContextTargetsPath)\" Condition=\"$(BaseIntermediateOutputPath.Contains('powerforge-context'))\" />"
            : propertyMethod
                ? "<Import Project=\"Context.targets\" Condition=\"$(BaseIntermediateOutputPath.Contains('powerforge-context'))\" />"
            : absoluteImport
                ? $"<Import Project=\"{System.Security.SecurityElement.Escape(Path.Combine(sharedDirectory, "Context.targets"))}\" Condition=\"$(BaseIntermediateOutputPath.Contains('powerforge-context'))\" />"
            : malicious
                ? "<Import Project=\"Context.targets\" Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\" />"
                : string.Empty;

        if (malicious || stablePathAlias)
        {
            if (externalAlias || chained)
                File.WriteAllText(Path.Combine(sharedDirectory, "Context.aliases.props"),
                    "<Project><PropertyGroup><ContextImportEnabled>$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))</ContextImportEnabled></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(sharedDirectory, "Context.targets"),
                execImport
                    ? "<Project><Target Name=\"UnexpectedExec\" BeforeTargets=\"Build\"><Exec Command=\"echo contextual-exec\" /></Target></Project>"
                : dynamicOutput
                    ? "<Project><PropertyGroup><PropertyToSet>IntermediateOutputPath</PropertyToSet></PropertyGroup><Target Name=\"UnexpectedOutput\" BeforeTargets=\"Build\"><CreateProperty Value=\"$(MSBuildProjectDirectory)/../shared-obj/\"><Output TaskParameter=\"Value\" PropertyName=\"$(PropertyToSet)\" /></CreateProperty></Target></Project>"
                : absoluteImport
                    ? "<Project><PropertyGroup><HarmlessContextMarker>LoadedFromOriginal</HarmlessContextMarker></PropertyGroup></Project>"
                : malicious
                    ? "<Project><Target Name=\"MutateImportedContext\" BeforeTargets=\"CoreCompile\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target></Project>"
                    : "<Project><PropertyGroup><HarmlessContextMarker>Loaded</HarmlessContextMarker></PropertyGroup></Project>");
            if (malicious)
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(
                    Path.Combine(root, "shared-obj")).FullName, ".keep"), string.Empty);
        }
        return import;
    }
}
