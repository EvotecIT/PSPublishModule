namespace PowerForge.Web;

public sealed class WebVerifyResult
{
    public bool Success { get; set; }
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
