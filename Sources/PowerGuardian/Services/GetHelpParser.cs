using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PowerGuardian;

internal sealed class GetHelpParser
{
    public CommandHelpModel? Parse(string commandName, int timeoutSeconds = 5)
    {
        using var ps = PowerShell.Create();
        ps.AddScript($"Get-Help -Name '{commandName}' -Full -ErrorAction SilentlyContinue");
        var async = ps.BeginInvoke();
        if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
        {
            try { ps.Stop(); } catch { }
            return null;
        }
        var help = ps.EndInvoke(async).FirstOrDefault() as PSObject;
        if (help == null) return null;

        var model = new CommandHelpModel
        {
            Name = GetString(help, "Name") ?? commandName,
            Synopsis = GetSynopsis(help) ?? string.Empty,
            Description = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(help, "Description"))
        };

        // Syntax sets
        foreach (var s in GetArray(help, "Syntax", "SyntaxItem"))
        {
            var syntax = new SyntaxSet
            {
                Name = GetString(s, "Name") ?? commandName
            };
            foreach (var p in GetArray(s, "Parameter"))
            {
                var ph = new ParameterHelp
                {
                    Name = GetString(p, "Name") ?? string.Empty,
                    Type = Get(p, "Type", t => GetString(t, "Name") ?? string.Empty) ?? string.Empty,
                    Position = GetString(p, "Position"),
                    Required = GetBool(p, "Required"),
                    PipelineInput = GetString(p, "PipelineInput"),
                    Globbing = GetBool(p, "Globbing"),
                    DefaultValue = GetString(p, "DefaultValue")
                };
                var aliases = GetArray(p, "Aliases").Select(a => SafeToString(a)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
                if (aliases.Count > 0) ph.Aliases = aliases;
                syntax.Parameters.Add(ph);
            }
            model.Syntax.Add(syntax);
        }

        // Parameters (detailed docs)
        foreach (var p in GetArray(help, "Parameters", "Parameter"))
        {
            var ph = new ParameterHelp
            {
                Name = GetString(p, "Name") ?? string.Empty,
                Type = Get(p, "Type", t => GetString(t, "Name") ?? string.Empty) ?? string.Empty,
                Description = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(p, "Description")),
                Required = GetBool(p, "Required"),
                Position = GetString(p, "Position"),
                PipelineInput = GetString(p, "PipelineInput"),
                Globbing = GetBool(p, "Globbing"),
                DefaultValue = GetString(p, "DefaultValue")
            };
            var aliases = GetArray(p, "Aliases").Select(a => SafeToString(a)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
            if (aliases.Count > 0) ph.Aliases = aliases;
            model.Parameters.Add(ph);
        }

        // Examples
        foreach (var ex in GetArray(help, "Examples", "Example"))
        {
            model.Examples.Add(new ExampleHelp
            {
                Title = (GetString(ex, "Title") ?? string.Empty).Trim(),
                Code = (GetString(ex, "Code") ?? string.Empty).Trim(),
                Remarks = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(ex, "Remarks")).Trim()
            });
        }

        // Inputs
        foreach (var it in GetArray(help, "InputTypes", "InputType"))
        {
            var type = ResolveTypeName(it) ?? Get(it, "Type", t => GetString(t, "Name"));
            var desc = string.Join(" ", GetParagraphs(it, "Description"));
            if (!string.IsNullOrWhiteSpace(type)) model.Inputs.Add(new TypeHelp { TypeName = type!.Trim(), Description = string.IsNullOrWhiteSpace(desc) ? null : desc });
        }

        // Outputs
        foreach (var ot in GetArray(help, "ReturnValues", "ReturnValue"))
        {
            var type = ResolveTypeName(ot) ?? Get(ot, "Type", t => GetString(t, "Name"));
            var desc = string.Join(" ", GetParagraphs(ot, "Description"));
            if (!string.IsNullOrWhiteSpace(type)) model.Outputs.Add(new TypeHelp { TypeName = type!.Trim(), Description = string.IsNullOrWhiteSpace(desc) ? null : desc });
        }

        // Notes
        var notes = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(help, "AlertSet").Concat(GetParagraphs(help, "Notes")));
        model.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        // Related links
        foreach (var link in GetArray(help, "RelatedLinks", "NavigationLink"))
        {
            var text = GetString(link, "LinkText") ?? string.Empty;
            var uri = GetString(link, "Uri") ?? string.Empty;
            if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(uri)) model.RelatedLinks.Add(new RelatedLink { Title = text, Uri = uri });
        }

        return model;
    }

    // Helpers
    private static string? GetString(PSObject obj, string name)
        => obj.Properties[name]?.Value?.ToString();

    private static string? GetSynopsis(PSObject help)
    {
        var v = help.Properties["Synopsis"]?.Value;
        if (v is string s) return s;
        if (v is PSObject p)
        {
            var t = p.Properties["Text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(t)) return t;
            var tt = p.Properties["#text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(tt)) return tt;
            // Do not enumerate arbitrary properties here to avoid picking up syntax
        }
        return v?.ToString();
    }

    private static bool? GetBool(PSObject obj, string name)
    {
        var v = obj.Properties[name]?.Value;
        if (v is bool b) return b;
        if (v == null) return null;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        return null;
    }

    private static T? Get<T>(PSObject obj, string name, Func<PSObject, T?> map)
    {
        var v = obj.Properties[name]?.Value as PSObject;
        if (v == null) return default;
        return map(v);
    }

    private static IEnumerable<string> GetParagraphs(PSObject obj, string name)
    {
        var v = obj.Properties[name]?.Value;
        foreach (var s in ExtractTextList(v))
        {
            var trimmed = s?.Trim();
            if (!string.IsNullOrEmpty(trimmed)) yield return trimmed!;
        }
    }

    private static IEnumerable<PSObject> GetArray(PSObject obj, string containerName, string? itemName = null)
    {
        var v = obj.Properties[containerName]?.Value;
        if (v is PSObject p)
        {
            if (!string.IsNullOrEmpty(itemName))
            {
                var inner = p.Properties[itemName]?.Value;
                foreach (var o in Flatten(inner)) yield return o;
            }
            else
            {
                foreach (var o in Flatten(p.BaseObject)) yield return o;
            }
        }
        else
        {
            foreach (var o in Flatten(v)) yield return o;
        }
    }

    private static IEnumerable<PSObject> Flatten(object? value)
    {
        if (value == null) yield break;
        if (value is PSObject po) { yield return po; yield break; }
        if (value is IEnumerable<object> e && value is not string)
        {
            foreach (var item in e)
            {
                if (item is PSObject px) yield return px; else if (item != null) yield return new PSObject(item);
            }
            yield break;
        }
        yield return new PSObject(value);
    }

    private static string? ResolveTypeName(PSObject obj)
    {
        // Try common shapes under a container (e.g., ReturnValue, InputType)
        var typeProp = obj.Properties["Type"]?.Value;
        if (typeProp is PSObject tp)
        {
            var name = tp.Properties["Name"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            var text = tp.Properties["Text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(text)) return text;
            var t2 = tp.Properties["#text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(t2)) return t2;
            if (tp.BaseObject is string s0) return s0;
        }
        if (typeProp is string s1 && !string.IsNullOrWhiteSpace(s1)) return s1;

        // Or the object itself is a type descriptor
        var directName = obj.Properties["Name"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directName)) return directName;
        var directText = obj.Properties["Text"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directText)) return directText;
        var directT = obj.Properties["#text"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directT)) return directT;
        if (obj.BaseObject is string s) return s;
        return null;
    }

    private static IEnumerable<string> ExtractTextList(object? v)
    {
        if (v == null) yield break;
        if (v is string s) { yield return s; yield break; }
        if (v is PSObject p)
        {
            if (p.BaseObject is string s2) { yield return s2; yield break; }
            var textProp = p.Properties["Text"]?.Value;
            if (textProp != null)
            {
                foreach (var t in ExtractTextList(textProp)) yield return t;
            }
            var paraProp = p.Properties["para"]?.Value;
            if (paraProp != null)
            {
                foreach (var t in ExtractTextList(paraProp)) yield return t;
            }
            var innerText = p.Properties["#text"]?.Value;
            if (innerText != null)
            {
                foreach (var t in ExtractTextList(innerText)) yield return t;
            }
            if (textProp == null && paraProp == null && innerText == null)
            {
                foreach (var prop in p.Properties)
                {
                    if (prop?.Value == null) continue;
                    foreach (var t in ExtractTextList(prop.Value)) yield return t;
                }
            }
            yield break;
        }
        if (v is IEnumerable<object> en && v is not string)
        {
            foreach (var item in en)
            {
                foreach (var t in ExtractTextList(item)) yield return t;
            }
            yield break;
        }
        yield return v.ToString() ?? string.Empty;
    }

    private static string? SafeToString(PSObject? po)
    {
        if (po == null) return null;
        var v = po.BaseObject;
        if (v is string s) return s;
        var name = (po.Properties["Name"]?.Value ?? po.Properties["Text"]?.Value ?? po.Properties["#text"]?.Value)?.ToString();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return po.ToString();
    }
}

internal sealed class CommandHelpModel
{
    public string Name { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SyntaxSet> Syntax { get; } = new();
    public List<ParameterHelp> Parameters { get; } = new();
    public List<ExampleHelp> Examples { get; } = new();
    public List<TypeHelp> Inputs { get; } = new();
    public List<TypeHelp> Outputs { get; } = new();
    public string? Notes { get; set; }
    public List<RelatedLink> RelatedLinks { get; } = new();
}

internal sealed class SyntaxSet
{
    public string Name { get; set; } = string.Empty;
    public List<ParameterHelp> Parameters { get; } = new();
}

internal sealed class ParameterHelp
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Position { get; set; }
    public bool? Required { get; set; }
    public string? PipelineInput { get; set; }
    public bool? Globbing { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? Aliases { get; set; }
}

internal sealed class ExampleHelp
{
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

internal sealed class TypeHelp
{
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

internal sealed class RelatedLink
{
    public string Title { get; set; } = string.Empty;
    public string? Uri { get; set; }
}
