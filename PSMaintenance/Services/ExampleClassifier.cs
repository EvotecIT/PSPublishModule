using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PSMaintenance;

/// <summary>
/// Classifies example text into Code and Remarks segments while preserving the author's formatting.
/// Used by structured and raw help paths to decide what goes inside fenced code blocks versus prose.
/// </summary>
internal sealed class ExampleClassifier
{
    private readonly HashSet<string> _approvedVerbs;
    private readonly HashSet<string> _approvedDslCommands;

    public ExampleClassifier(IEnumerable<string>? approvedVerbs = null, IEnumerable<string>? approvedDslCommands = null)
    {
        _approvedVerbs = new HashSet<string>((approvedVerbs ?? DefaultVerbs), StringComparer.OrdinalIgnoreCase);
        _approvedDslCommands = new HashSet<string>((approvedDslCommands ?? DefaultDslCommands), StringComparer.OrdinalIgnoreCase);
    }

    public bool Classify(string input, out string code, out string remarks, out string mode)
    {
        code = string.Empty; remarks = string.Empty; mode = "classifier";
        if (string.IsNullOrWhiteSpace(input)) return true;

        var text = input.Replace("\r\n", "\n");
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);

        // 1) Prompt-first minimal split: if any line begins with a PowerShell prompt, treat prompt lines as code; all others as remarks.
        bool HasPrompt(string t)
        {
            var s = (t ?? string.Empty).TrimStart();
            if (s.StartsWith("PS>") || s.StartsWith("PS ")) return true; // PS>, PS C:\> etc.
            return false;
        }
        if (lines.Any(l => HasPrompt(l)))
        {
            var codeLines = new List<string>();
            var remarkLines = new List<string>();
            foreach (var l in lines)
            {
                if (HasPrompt(l)) codeLines.Add(l);
                else remarkLines.Add(l);
            }
            code = string.Join("\n", codeLines).TrimEnd();
            remarks = string.Join("\n", remarkLines).Trim();
            mode = "classifier:prompt-split";
            return true;
        }

        // 2) No prompt detected: treat entire chunk as code (do not second-guess formatting)
        code = text.TrimEnd();
        remarks = string.Empty;
        mode = "classifier:all-code";
        return true;
    }

    private static readonly string[] DefaultVerbs = new[]
    {
        "Get","Set","New","Remove","Start","Stop","Enable","Disable","Add","Clear","Read","Write","Invoke","Repair","Update","Test","Convert","Export","Import","Install","Uninstall","Find","Move","Copy","Rename","Resume","Suspend","Join","Split","Compare","Measure","Select","Where","ForEach","Group","Sort","Show","Hide","Lock","Unlock","Optimize","Connect","Disconnect","Grant","Revoke"
    };

    private static readonly string[] DefaultDslCommands = new[]
    {
        // Common DSL-like commands in examples
        "EmailText","EmailImage","EmailLineBreak","EmailList","EmailListItem"
    };
}
