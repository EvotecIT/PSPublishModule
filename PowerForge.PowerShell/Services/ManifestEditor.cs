using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Edits PowerShell data files (PSD1) safely using the PowerShell AST, preserving file layout.
/// Only the targeted value text is replaced; comments and other content remain untouched.
/// </summary>
public static partial class ManifestEditor
{
    private static readonly string NewLine = Environment.NewLine;

}
