using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledDocumentTaskFileInputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties = null,
        string? controlledProjectPath = null,
        Func<string, bool>? isControlledInput = null,
        Func<string, string[]?>? readLines = null)
        => HasOnlyControlledConditionFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput) &&
           HasOnlyControlledCallTargetDestinations(
               document,
               declaringPath,
               taskInputBaseDirectory,
               relatedDocuments,
               evaluatedGlobalProperties) &&
           HasOnlyControlledTaskLoadedFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               readLines ?? ReadControlledCheckoutTextInput) &&
           HasOnlyControlledLiteralTaskFileOutputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               controlledProjectPath) &&
           HasOnlyControlledLiteralTaskFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput,
               readLines);
}
