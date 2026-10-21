using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private const string PowerShellCommonParametersText =
        "This command supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable.";

    /// <summary>Section labels used by the generated reference chrome (breadcrumb, sidebar, overview, type meta).</summary>
    private sealed class ApiDocsChromeLabels
    {
        private ApiDocsChromeLabels(string sectionLabel, bool isPowerShell, bool showExampleOrigins)
        {
            SectionLabel = sectionLabel;
            IsPowerShell = isPowerShell;
            ShowExampleOrigins = showExampleOrigins;
        }

        public string SectionLabel { get; }
        public bool IsPowerShell { get; }
        public bool ShowExampleOrigins { get; }
        public string GroupLabel => IsPowerShell ? "Module" : "Namespace";
        public string GroupPluralLabel => IsPowerShell ? "modules" : "namespaces";

        public static ApiDocsChromeLabels From(WebApiDocsOptions? options)
            => new(ResolveApiSectionLabel(options), options?.Type == ApiDocsType.PowerShell, options?.ShowExampleOrigins == true);
    }

    private static string ResolveApiSectionLabel(WebApiDocsOptions? options)
    {
        var configured = options?.SectionLabel?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return options?.Type == ApiDocsType.PowerShell ? "Commands" : "API Reference";
    }

    /// <summary>
    /// Renders a command page in the order PowerShell readers expect from Get-Help and Microsoft Learn:
    /// syntax, description, examples, parameters (each listed once), common parameters, outputs and related links.
    /// </summary>
    private static void AppendPowerShellCommandBody(
        HtmlFragmentBuilder body,
        ApiTypeModel type,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeUsageModel? usage,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        bool showExampleOrigins)
    {
        if (!string.IsNullOrWhiteSpace(type.Summary))
            body.Line($"<p class=\"type-summary\">{RenderLinkedText(type.Summary, baseUrl, slugMap)}</p>");

        if (type.Methods.Count > 0)
        {
            var usedMemberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppendMemberSections(body, "Syntax", "method", type.Methods, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: false, sectionId: "syntax");
        }

        if (HasDistinctPowerShellDescription(type))
        {
            body.Line("<section class=\"command-description\" id=\"remarks\">");
            using (body.Indent())
            {
                body.Line("<h2>Description</h2>");
                foreach (var paragraph in SplitExampleParagraphs(GetPowerShellDescription(type)))
                    body.Line($"<p>{RenderLinkedText(paragraph, baseUrl, slugMap)}</p>");
            }
            body.Line("</section>");
        }

        if (relatedContent?.Entries.Count > 0)
        {
            AppendRelatedContentSection(
                body,
                relatedContent.Entries,
                baseUrl,
                slugMap,
                "guides-and-samples",
                "Guides & Samples",
                "Authored walkthroughs and practical samples linked to this command.",
                "h2");
        }

        if (usage?.HasEntries == true)
            AppendUsageSection(body, usage, baseUrl, slugMap);

        if (type.Examples.Count > 0)
        {
            body.Line("<section class=\"type-examples\" id=\"examples\">");
            using (body.Indent())
            {
                body.Line("<h2>Examples</h2>");
                AppendExamples(body, type.Examples, baseUrl, slugMap, codeLanguage, showExampleOrigins);
            }
            body.Line("</section>");
        }

        AppendPowerShellParametersSection(body, type, baseUrl, slugMap);

        if (HasPowerShellCommonParameters(type))
        {
            var commonParametersLink = ResolvePowerShellCommonParametersUrl(baseUrl, slugMap);
            var commonParametersLinkTarget = IsExternal(commonParametersLink) ? " target=\"_blank\" rel=\"noopener\"" : string.Empty;
            body.Line("<section class=\"type-common-parameters\" id=\"common-parameters\">");
            using (body.Indent())
            {
                body.Line("<h2>Common Parameters</h2>");
                body.Line($"<p>{PowerShellCommonParametersText}</p>");
                body.Line($"<p>For more information, see <a href=\"{System.Web.HttpUtility.HtmlAttributeEncode(commonParametersLink)}\"{commonParametersLinkTarget}>about_CommonParameters</a>.</p>");
            }
            body.Line("</section>");
        }

        var outputNotes = GetPowerShellOutputNotes(type);
        if (outputNotes.Count > 0)
        {
            body.Line("<section class=\"type-outputs\" id=\"outputs\">");
            using (body.Indent())
            {
                body.Line("<h2>Outputs</h2>");
                foreach (var note in outputNotes)
                    body.Line($"<p>{RenderLinkedText(note, baseUrl, slugMap)}</p>");
            }
            body.Line("</section>");
        }

        if (type.SeeAlso.Count > 0)
        {
            body.Line("<section class=\"type-see-also\" id=\"see-also\">");
            using (body.Indent())
            {
                body.Line("<h2>Related Links</h2>");
                body.Line("<ul class=\"see-also-list\">");
                using (body.Indent())
                {
                    foreach (var item in type.SeeAlso)
                        body.Line($"<li>{RenderLinkedText(item, baseUrl, slugMap)}</li>");
                }
                body.Line("</ul>");
            }
            body.Line("</section>");
        }
    }

    private static List<(string id, string label)> BuildPowerShellCommandToc(ApiTypeModel type, bool hasUsage, bool hasRelatedContent)
    {
        var list = new List<(string id, string label)> { ("overview", "Overview") };
        if (type.Methods.Count > 0)
            list.Add(("syntax", "Syntax"));
        if (HasDistinctPowerShellDescription(type))
            list.Add(("remarks", "Description"));
        if (hasRelatedContent)
            list.Add(("guides-and-samples", "Guides & Samples"));
        if (hasUsage)
            list.Add(("usage", "Usage"));
        if (type.Examples.Count > 0)
            list.Add(("examples", "Examples"));
        if (BuildPowerShellParameterEntries(type).Count > 0)
            list.Add(("parameters", "Parameters"));
        if (HasPowerShellCommonParameters(type))
            list.Add(("common-parameters", "Common Parameters"));
        if (GetPowerShellOutputNotes(type).Count > 0)
            list.Add(("outputs", "Outputs"));
        if (type.SeeAlso.Count > 0)
            list.Add(("see-also", "Related Links"));
        return list;
    }

    private sealed class PowerShellParameterEntry
    {
        public PowerShellParameterEntry(ApiParameterModel parameter) => Parameter = parameter;

        public ApiParameterModel Parameter { get; }
        public string? Summary { get; set; }
        public List<string> Sets { get; } = new();
        public List<string> RequiredSets { get; } = new();
    }

    /// <summary>Merges the parameters of every parameter set into one list, keeping first-seen order.</summary>
    private static List<PowerShellParameterEntry> BuildPowerShellParameterEntries(ApiTypeModel type)
    {
        var entries = new List<PowerShellParameterEntry>();
        var byName = new Dictionary<string, PowerShellParameterEntry>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < type.Methods.Count; index++)
        {
            var member = type.Methods[index];
            var setName = string.IsNullOrWhiteSpace(member.ParameterSetName) ? $"Set {index + 1}" : member.ParameterSetName!.Trim();
            foreach (var parameter in member.Parameters)
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
                    continue;
                if (!byName.TryGetValue(parameter.Name, out var entry))
                {
                    entry = new PowerShellParameterEntry(parameter);
                    byName[parameter.Name] = entry;
                    entries.Add(entry);
                }

                if (string.IsNullOrWhiteSpace(entry.Summary) && !string.IsNullOrWhiteSpace(parameter.Summary))
                    entry.Summary = parameter.Summary;
                if (!entry.Sets.Contains(setName, StringComparer.OrdinalIgnoreCase))
                    entry.Sets.Add(setName);
                if (!parameter.IsOptional && !entry.RequiredSets.Contains(setName, StringComparer.OrdinalIgnoreCase))
                    entry.RequiredSets.Add(setName);
            }
        }

        return entries;
    }

    private static void AppendPowerShellParametersSection(
        HtmlFragmentBuilder body,
        ApiTypeModel type,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        var entries = BuildPowerShellParameterEntries(type);
        if (entries.Count == 0)
            return;

        var setCount = type.Methods.Count;
        body.Line("<section class=\"type-parameters-list powershell-parameters\" id=\"parameters\">");
        using (body.Indent())
        {
            body.Line("<h2>Parameters</h2>");
            body.Line("<dl class=\"param-list param-list--command\">");
            using (body.Indent())
            {
                foreach (var entry in entries)
                {
                    var parameter = entry.Parameter;
                    var anchor = "parameter-" + Slugify(parameter.Name);
                    var defaultText = string.IsNullOrWhiteSpace(parameter.DefaultValue) || IsPowerShellNoneValue(parameter.DefaultValue)
                        ? string.Empty
                        : $" = {parameter.DefaultValue}";
                    body.Line(
                        $"<dt id=\"{System.Web.HttpUtility.HtmlAttributeEncode(anchor)}\"><span class=\"param-name\">-{System.Web.HttpUtility.HtmlEncode(parameter.Name)}</span> " +
                        $"<span class=\"param-type{(entry.RequiredSets.Count == 0 ? " optional" : string.Empty)}\">{System.Web.HttpUtility.HtmlEncode(parameter.Type)}</span>" +
                        $"<span class=\"param-default\">{System.Web.HttpUtility.HtmlEncode(defaultText)}</span>{BuildPowerShellParameterChips(entry, setCount)}</dt>");
                    if (!string.IsNullOrWhiteSpace(entry.Summary))
                        body.Line($"<dd>{RenderLinkedText(entry.Summary, baseUrl, slugMap)}</dd>");
                    if (parameter.PossibleValues.Count > 0)
                        body.Line($"<dd class=\"param-possible-values\">Possible values: {RenderPowerShellPossibleValues(parameter.PossibleValues)}</dd>");
                }
            }
            body.Line("</dl>");
        }
        body.Line("</section>");
    }

    /// <summary>
    /// Chips carry what Get-Help shows per parameter, without restating defaults on every row:
    /// position only when positional, pipeline input only when accepted, sets only when not shared by all sets.
    /// </summary>
    private static string BuildPowerShellParameterChips(PowerShellParameterEntry entry, int setCount)
    {
        var parameter = entry.Parameter;
        var chips = new List<string>();
        if (entry.RequiredSets.Count == 0)
            chips.Add("<span class=\"param-meta-chip optional\">optional</span>");
        else if (entry.RequiredSets.Count == entry.Sets.Count)
            chips.Add("<span class=\"param-meta-chip required\">required</span>");
        else
            chips.Add($"<span class=\"param-meta-chip required\">required in {System.Web.HttpUtility.HtmlEncode(string.Join(", ", entry.RequiredSets))}</span>");

        if (IsPowerShellPositionalParameter(parameter.Position))
            chips.Add($"<span class=\"param-meta-chip param-meta-chip--position\">position: {System.Web.HttpUtility.HtmlEncode(parameter.Position!.Trim())}</span>");
        if (!string.IsNullOrWhiteSpace(parameter.PipelineInput) &&
            !string.Equals(parameter.PipelineInput.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            chips.Add($"<span class=\"param-meta-chip pipeline param-meta-chip--pipeline\">pipeline: {System.Web.HttpUtility.HtmlEncode(parameter.PipelineInput.Trim())}</span>");

        var aliases = parameter.Aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias) && !IsPowerShellNoneValue(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (aliases.Length > 0)
            chips.Add($"<span class=\"param-meta-chip\">aliases: {System.Web.HttpUtility.HtmlEncode(string.Join(", ", aliases))}</span>");

        if (setCount > 1 && entry.Sets.Count < setCount)
            chips.Add($"<span class=\"param-meta-chip sets\">sets: {System.Web.HttpUtility.HtmlEncode(string.Join(", ", entry.Sets))}</span>");

        return $" <span class=\"param-meta\">{string.Join(string.Empty, chips)}</span>";
    }

    /// <summary>Help generators often copy the synopsis into the description; repeating it adds nothing.</summary>
    private static bool HasDistinctPowerShellDescription(ApiTypeModel type)
        => !string.IsNullOrWhiteSpace(GetPowerShellDescription(type));

    /// <summary>Removes a normalized synopsis prefix while retaining the original text of the remainder.</summary>
    private static string GetPowerShellDescription(ApiTypeModel type)
    {
        var description = type.Remarks?.Trim() ?? string.Empty;
        var synopsis = type.Summary?.Trim();
        if (description.Length == 0 || string.IsNullOrWhiteSpace(synopsis))
            return description;

        // Match words, allowing help exporters to fold whitespace or change casing.
        // A word boundary prevents "Reads a file" from consuming "Reads a filename".
        var words = Regex.Split(synopsis, @"\s+", RegexOptions.CultureInvariant, RegexTimeout);
        var pattern = @"\A" + string.Join(@"\s+", words.Select(Regex.Escape));
        var match = Regex.Match(description, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success ||
            (match.Length < description.Length && char.IsLetterOrDigit(description[match.Length]) && char.IsLetterOrDigit(synopsis[^1])))
            return description;
        return description[match.Length..].TrimStart();
    }

    private static bool IsPowerShellNoneValue(string? value)
        => string.Equals(value?.Trim(), "None", StringComparison.OrdinalIgnoreCase);

    /// <summary>Output descriptions worth a section: the type names alone are already listed in the page meta.</summary>
    private static List<string> GetPowerShellOutputNotes(ApiTypeModel type)
    {
        var outputTypes = new HashSet<string>(type.OutputTypes.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        bool IsTypeListOnly(string note)
            => note
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => part.Trim())
                .Where(static part => part.Length > 0)
                .All(outputTypes.Contains);
        return type.Methods
            .Select(static member => member.Returns?.Trim())
            .Where(note => !string.IsNullOrWhiteSpace(note) && !IsTypeListOnly(note!))
            .Select(static note => note!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
