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
        Func<string, string[]?>? readLines = null,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties = null)
        => HasOnlyControlledConditionFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput,
               immutableGlobalProperties) &&
           HasOnlyControlledCallTargetDestinations(
               document,
               declaringPath,
               taskInputBaseDirectory,
               relatedDocuments,
               evaluatedGlobalProperties,
               immutableGlobalProperties) &&
           HasOnlyControlledSdkTaskPropertyFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput,
               readLines ?? ReadControlledCheckoutTextInput,
               immutableGlobalProperties) &&
           HasOnlyControlledGenerateResourceSourcePaths(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               readLines ?? ReadControlledCheckoutTextInput,
               immutableGlobalProperties) &&
           HasOnlyControlledPublishItemInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput,
               immutableGlobalProperties) &&
           HasOnlyControlledTaskLoadedFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               readLines ?? ReadControlledCheckoutTextInput,
               immutableGlobalProperties) &&
           HasOnlyControlledLiteralTaskFileOutputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               controlledProjectPath,
               immutableGlobalProperties) &&
           HasOnlyControlledLiteralTaskFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               isControlledInput,
               readLines,
               immutableGlobalProperties);
}
