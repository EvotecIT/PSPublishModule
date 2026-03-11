namespace PowerForge;

internal sealed class CommentRemovalRequest
{
    public string Content { get; set; } = string.Empty;
    public bool RemoveEmptyLines { get; set; }
    public bool RemoveAllEmptyLines { get; set; }
    public bool RemoveCommentsInParamBlock { get; set; }
    public bool RemoveCommentsBeforeParamBlock { get; set; }
    public bool DoNotRemoveSignatureBlock { get; set; }
}
