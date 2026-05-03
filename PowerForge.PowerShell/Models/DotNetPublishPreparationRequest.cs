using System.Management.Automation;

namespace PowerForge;

internal sealed class DotNetPublishPreparationRequest
{
    public string ParameterSetName { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
    public ScriptBlock? Settings { get; set; }
    public string? ConfigPath { get; set; }
    public string? ProjectRoot { get; set; }
    public string? Profile { get; set; }
    public string[]? Target { get; set; }
    public string[]? Runtimes { get; set; }
    public string[]? Frameworks { get; set; }
    public DotNetPublishStyle[]? Styles { get; set; }
    public bool SkipRestore { get; set; }
    public bool SkipBuild { get; set; }
    public bool JsonOnly { get; set; }
    public string? JsonPath { get; set; }
    public bool Plan { get; set; }
    public bool Validate { get; set; }
    public Func<string, string>? ResolvePath { get; set; }
}
