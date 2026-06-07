namespace PowerForge;

internal interface IMarkdownExampleIndentClassifier
{
    bool ShouldRemoveSharedIndentAfterFirstLine(string candidateCode);
}
