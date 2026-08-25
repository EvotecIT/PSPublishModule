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
           HasOnlyControlledTaskLoadedFileInputs(
               document,
               declaringPath,
               taskInputBaseDirectory,
               declaringAllowedRoot,
               taskInputAllowedRoot,
               relatedDocuments,
               evaluatedGlobalProperties,
               readLines ?? ReadControlledCheckoutTextInput) &&
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
