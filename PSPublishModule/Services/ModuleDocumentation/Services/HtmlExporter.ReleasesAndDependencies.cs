using System;
using System.Collections.Generic;
using System.Linq;
using HtmlForgeX;
using HtmlForgeX.Extensions;
using HtmlForgeX.Markdown;
using PowerForge;

namespace PSPublishModule;

internal sealed partial class HtmlExporter
{
    private static void RenderStructuredReleases(TablerTabsPanel panel, IEnumerable<RepoRelease> releases)
    {
        var releaseList = OrderReleases(releases);

        if (releaseList.Count == 0)
        {
            panel.Markdown("No releases available.", new MarkdownOptions { HeadingsBaseLevel = 2 });
            return;
        }

        var latest = releaseList[0];
        var latestStable = releaseList.FirstOrDefault(r => !IsPrerelease(r));
        var latestPreview = releaseList.FirstOrDefault(IsPrerelease);
        var totalAssets = releaseList.Sum(r => r.Assets.Count);

        panel.Card(card =>
        {
            card.Header(h => h.Title("Release Overview"));
            card.DataGrid(grid =>
            {
                grid.AddItem("Total releases", releaseList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                grid.AddItem("Total assets", totalAssets.ToString(System.Globalization.CultureInfo.InvariantCulture));

                var latestLabel = string.IsNullOrWhiteSpace(latest.Name) ? latest.Tag : latest.Name;
                if (!string.IsNullOrWhiteSpace(latestLabel))
                {
                    grid.AddItem("Latest release", latestLabel!);
                }

                if (latest.PublishedAt.HasValue)
                {
                    grid.AddItem("Latest published", latest.PublishedAt.Value.ToString("yyyy-MM-dd"));
                }

                if (latestStable is not null)
                {
                    grid.AddItem("Latest stable", ResolveReleaseLabel(latestStable));
                }

                if (latestPreview is not null)
                {
                    grid.AddItem("Latest preview", ResolveReleaseLabel(latestPreview));
                }

                if (!string.IsNullOrWhiteSpace(latest.Url))
                {
                    grid.AddItem("Latest release page", new HtmlForgeX.Tags.Anchor(latest.Url!, latest.Url!));
                }
            });
        });

        panel.LineBreak();
        panel.Tabs(inner =>
        {
            inner.AddTab("🕒 Timeline", timeline =>
            {
                if (releaseList.Count > 1 || totalAssets > 0)
                {
                    var rows = releaseList.Select(release => new
                    {
                        Release = ResolveReleaseLabel(release),
                        Tag = release.Tag ?? string.Empty,
                        Status = ResolveReleaseStatus(release, latestStable, latestPreview),
                        Published = release.PublishedAt?.ToString("yyyy-MM-dd") ?? string.Empty,
                        Assets = release.Assets.Count,
                        Url = release.Url ?? string.Empty
                    }).ToList();

                    timeline.Card(card =>
                    {
                        card.Header(h => h.Title("Release Index"));
                        card.DataTable(rows, t => t
                            .Settings(s => s.Preset(DataTablesPreset.Minimal))
                            .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                            .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                        );
                    });
                }

                foreach (var release in releaseList)
                {
                    timeline.LineBreak();
                    timeline.Card(card =>
                    {
                        card.Header(h => h.Title(ResolveReleaseLabel(release)));
                        card.Body(body =>
                        {
                            body.DataGrid(grid =>
                            {
                                if (!string.IsNullOrWhiteSpace(release.Tag))
                                {
                                    grid.AddItem("Tag", release.Tag);
                                }

                                grid.AddItem("Status", ResolveReleaseStatus(release, latestStable, latestPreview));

                                if (release.PublishedAt.HasValue)
                                {
                                    grid.AddItem("Published", release.PublishedAt.Value.ToString("yyyy-MM-dd"));
                                }

                                grid.AddItem("Assets", release.Assets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

                                if (!string.IsNullOrWhiteSpace(release.Url))
                                {
                                    grid.AddItem("Release page", new HtmlForgeX.Tags.Anchor(release.Url!, release.Url!));
                                }
                            });

                            if (!string.IsNullOrWhiteSpace(release.Body))
                            {
                                body.Markdown("### Notes", new MarkdownOptions
                                {
                                    HeadingsBaseLevel = 2,
                                    AutolinkBareUrls = true,
                                });
                                body.Markdown(release.Body.Trim(), new MarkdownOptions
                                {
                                    HeadingsBaseLevel = 3,
                                    AutolinkBareUrls = true,
                                    OpenLinksInNewTab = false,
                                    AllowRelativeLinks = true,
                                    TableMode = MarkdownTableMode.DataTables,
                                    DataTables = new MarkdownDataTablesOptions
                                    {
                                        Responsive = true,
                                        Export = true,
                                        ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy },
                                        StateSave = true
                                    }
                                });
                            }

                            if (release.Assets.Count > 0)
                            {
                                body.Markdown("### Assets", new MarkdownOptions
                                {
                                    HeadingsBaseLevel = 2,
                                });

                                var assetRows = release.Assets.Select(asset => new
                                {
                                    Name = asset.Name,
                                    Download = asset.DownloadUrl,
                                    Size = asset.Size.HasValue ? FormatBytes(asset.Size.Value) : string.Empty,
                                    Type = asset.ContentType ?? string.Empty
                                }).ToList();

                                body.DataTable(assetRows, t => t
                                    .Settings(s => s.Preset(DataTablesPreset.Minimal))
                                    .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                                    .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                                );
                            }
                        });
                    });
                }
            });

            if (totalAssets > 0)
            {
                var downloadRows = releaseList
                    .SelectMany(release => release.Assets.Select(asset => new
                    {
                        Release = ResolveReleaseLabel(release),
                        Tag = release.Tag ?? string.Empty,
                        Status = ResolveReleaseStatus(release, latestStable, latestPreview),
                        Published = release.PublishedAt?.ToString("yyyy-MM-dd") ?? string.Empty,
                        Name = asset.Name,
                        Download = asset.DownloadUrl,
                        Size = asset.Size.HasValue ? FormatBytes(asset.Size.Value) : string.Empty,
                        Type = asset.ContentType ?? string.Empty
                    }))
                    .ToList();

                inner.AddTab("⬇️ Downloads", downloads =>
                {
                    downloads.Card(card =>
                    {
                        card.Header(h => h.Title("Release Downloads"));
                        card.DataTable(downloadRows, t => t
                            .Settings(s => s.Preset(DataTablesPreset.Minimal))
                            .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                            .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                        );
                    });
                });
            }
        });
    }

    private static string ResolveReleaseLabel(RepoRelease release)
    {
        if (!string.IsNullOrWhiteSpace(release.Name))
            return release.Name;
        if (!string.IsNullOrWhiteSpace(release.Tag))
            return release.Tag;
        return "Release";
    }

    private static List<RepoRelease> OrderReleases(IEnumerable<RepoRelease> releases)
    {
        var releaseList = releases
            .Where(r => r is not null && !r.IsDraft)
            .ToList();

        releaseList.Sort(CompareReleasesDescending);
        return releaseList;
    }

    private static int CompareReleasesDescending(RepoRelease? left, RepoRelease? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;

        var publishedComparison = Nullable.Compare(right.PublishedAt, left.PublishedAt);
        if (publishedComparison != 0)
            return publishedComparison;

        var versionComparison = CompareReleaseVersions(left, right);
        if (versionComparison != 0)
            return -versionComparison;

        return string.Compare(ResolveReleaseLabel(right), ResolveReleaseLabel(left), StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareReleaseVersions(RepoRelease left, RepoRelease right)
    {
        var leftVersion = TryParseReleaseVersion(left, out var parsedLeft, out var leftSuffix, out var leftIsPrerelease);
        var rightVersion = TryParseReleaseVersion(right, out var parsedRight, out var rightSuffix, out var rightIsPrerelease);

        if (leftVersion && rightVersion)
        {
            var versionComparison = parsedLeft.CompareTo(parsedRight);
            if (versionComparison != 0)
                return versionComparison;

            if (leftIsPrerelease != rightIsPrerelease)
                return leftIsPrerelease ? -1 : 1;

            return string.Compare(leftSuffix, rightSuffix, StringComparison.OrdinalIgnoreCase);
        }

        if (leftVersion)
            return 1;
        if (rightVersion)
            return -1;

        return string.Compare(ResolveReleaseLabel(left), ResolveReleaseLabel(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseReleaseVersion(RepoRelease release, out Version version, out string suffix, out bool isPrerelease)
    {
        var token = string.Join(" ", new[] { release.Tag ?? string.Empty, release.Name ?? string.Empty });
        var match = System.Text.RegularExpressions.Regex.Match(
            token,
            @"\bv?(?<version>\d+(?:\.\d+){1,3})(?<suffix>[-a-z][a-z0-9\.-]*)?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out version!))
        {
            version = new Version(0, 0);
            suffix = string.Empty;
            isPrerelease = false;
            return false;
        }

        suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : string.Empty;
        isPrerelease = !string.IsNullOrWhiteSpace(suffix);
        return true;
    }

    private static string ResolveReleaseStatus(RepoRelease release, RepoRelease? latestStable, RepoRelease? latestPreview)
    {
        if (latestStable is not null && ReferenceEquals(release, latestStable))
            return "Latest Stable";
        if (latestPreview is not null && ReferenceEquals(release, latestPreview))
            return "Latest Preview";
        return IsPrerelease(release) ? "Preview" : "Stable";
    }

    private static bool IsPrerelease(RepoRelease release)
    {
        if (release.IsPrerelease)
            return true;

        var combined = string.Join(" ", new[] { release.Tag ?? string.Empty, release.Name ?? string.Empty });
        return combined.Contains("preview", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("prerelease", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("pre-release", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("alpha", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("beta", StringComparison.OrdinalIgnoreCase)
               || System.Text.RegularExpressions.Regex.IsMatch(combined, @"(?<![a-z])rc[\.-]?\d*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string NormalizeSourceLikeMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var trimmed = markdown!.Trim();
        if ((trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            && TryExtractSingleFencedBlock(trimmed, out var codeContent))
        {
            return RepositoryContentNormalizer.WrapAsSourceCodeBlock(codeContent, "text");
        }

        return RepositoryContentNormalizer.WrapAsSourceCodeBlock(trimmed, "text");
    }

    private static bool TryExtractSingleFencedBlock(string markdown, out string codeContent)
    {
        codeContent = string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
            return false;

        var normalized = markdown.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length < 3)
            return false;

        var firstLine = lines[0].Trim();
        if (!(firstLine.StartsWith("```", StringComparison.Ordinal) || firstLine.StartsWith("~~~", StringComparison.Ordinal)))
            return false;

        var markerChar = firstLine[0];
        var markerLength = 0;
        while (markerLength < firstLine.Length && firstLine[markerLength] == markerChar)
        {
            markerLength++;
        }

        var closingIndex = lines.Length - 1;
        while (closingIndex > 0 && string.IsNullOrWhiteSpace(lines[closingIndex]))
        {
            closingIndex--;
        }

        var closingLine = lines[closingIndex].Trim();
        if (closingLine.Length < markerLength || !closingLine.All(ch => ch == markerChar))
            return false;

        codeContent = string.Join("\n", lines.Skip(1).Take(closingIndex - 1));
        return true;
    }

    private static string FormatBytes(long size)
    {
        if (size < 1024)
            return size.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";

        double value = size;
        var suffixes = new[] { "KB", "MB", "GB", "TB" };
        var suffixIndex = -1;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return value.ToString(value >= 10 ? "0.#" : "0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + suffixes[Math.Max(suffixIndex, 0)];
    }

    private static string NormalizeExampleTitle(string raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"Example {index}";
        var t = raw.Trim();
        t = t.Trim('-').Trim();
        if (t.Length == 0) return $"Example {index}";
        return t;
    }

    private static string RenderHelpMarkdown(CommandHelpModel m)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(m.Synopsis))
        {
            sb.AppendLine("## Synopsis");
            sb.AppendLine(m.Synopsis.Trim());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            sb.AppendLine("## Description");
            sb.AppendLine(m.Description.Trim());
            sb.AppendLine();
        }
        if (m.Syntax.Count > 0)
        {
            sb.AppendLine("## Syntax");
            var total = m.Syntax.Count;
            for (int i = 0; i < total; i++)
            {
                var s = m.Syntax[i];
                // Friendly preface between blocks
                sb.AppendLine($"_Parameter set {i + 1} of {total}_");
                var parts = new List<string>();
                foreach (var p in s.Parameters)
                {
                    var name = p.Name;
                    var type = string.IsNullOrEmpty(p.Type) ? string.Empty : $" <{p.Type}>";
                    var token = $"-{name}{type}";
                    if (!(p.Required ?? false)) token = $"[{token}]";
                    parts.Add(token);
                }
                var line = (s.Name ?? m.Name) + (parts.Count > 0 ? (" " + string.Join(" ", parts)) : string.Empty);
                sb.AppendLine("```powershell");
                sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        if (m.Parameters.Count > 0)
        {
            sb.AppendLine("## Parameters");
            sb.AppendLine("| Name | Type | Required | Position | Pipeline | Wildcards | Default | Aliases | Description |");
            sb.AppendLine("|:-----|:-----|:--------:|:--------:|:--------:|:---------:|:--------|:--------|:-----------|");
            foreach (var p in m.Parameters)
            {
                var name = $"-{p.Name}";
                var type = string.IsNullOrWhiteSpace(p.Type) ? "" : p.Type.Replace("|", "\\|");
                var req = p.Required.HasValue ? (p.Required.Value ? "Yes" : "No") : "";
                var pos = string.IsNullOrWhiteSpace(p.Position) ? "" : p.Position;
                var pipe = ((p.PipelineInput ?? string.Empty)).Replace("|", "\\|");
                var wc = p.Globbing.HasValue ? (p.Globbing.Value ? "Yes" : "No") : "";
                var def = ((p.DefaultValue ?? string.Empty)).Replace("|", "\\|");
                var aliases = (p.Aliases == null || p.Aliases.Count == 0) ? "" : string.Join(", ", p.Aliases).Replace("|", "\\|");
                var desc = string.IsNullOrWhiteSpace(p.Description) ? "" : p.Description!.Replace("\n", " ").Replace("|", "\\|");
                sb.AppendLine($"| {name} | {type} | {req} | {pos} | {pipe} | {wc} | {def} | {aliases} | {desc} |");
            }
            sb.AppendLine();
        }
        if (m.Examples.Count > 0)
        {
            sb.AppendLine("## Examples");
            int i = 1;
            foreach (var ex in m.Examples)
            {
                var title = string.IsNullOrWhiteSpace(ex.Title) ? $"Example {i}" : ex.Title.Trim();
                sb.AppendLine($"### {title}");
                var block = string.IsNullOrEmpty(ex.Remarks) ? (ex.Code ?? string.Empty) : ((ex.Code ?? string.Empty) + "\n\n" + ex.Remarks);
                sb.AppendLine("```powershell");
                sb.AppendLine((block ?? string.Empty).TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
                i++;
            }
        }
        if (m.Inputs.Count > 0)
        {
            sb.AppendLine("## Inputs");
            AppendTypeHelpList(sb, m.Inputs);
            sb.AppendLine();
        }
        if (m.Outputs.Count > 0)
        {
            sb.AppendLine("## Outputs");
            AppendTypeHelpList(sb, m.Outputs);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            sb.AppendLine("## Notes");
            sb.AppendLine(m.Notes!.Trim());
            sb.AppendLine();
        }
        if (m.RelatedLinks.Count > 0)
        {
            sb.AppendLine("## Related Links");
            foreach (var l in m.RelatedLinks)
            {
                if (!string.IsNullOrEmpty(l.Uri)) sb.AppendLine($"- [{l.Title}]({l.Uri})"); else sb.AppendLine($"- {l.Title}");
            }
        }
        return sb.ToString();
    }

    internal static string RenderHelpMarkdownForTesting(CommandHelpModel model) => RenderHelpMarkdown(model);

    private static void AppendTypeHelpList(System.Text.StringBuilder sb, IEnumerable<TypeHelp> types)
    {
        foreach (var t in types)
        {
            var type = FormatCodeSpan(string.IsNullOrWhiteSpace(t.TypeName) ? "None" : t.TypeName.Trim());
            var desc = NormalizeListDescription(t.Description);
            sb.AppendLine(string.IsNullOrWhiteSpace(desc)
                ? $"- {type}"
                : $"- {type} - {desc}");
        }
    }

    private static string FormatCodeSpan(string value)
    {
        var delimiterLength = 1;
        var currentRun = 0;
        foreach (var ch in value)
        {
            if (ch == '`')
            {
                currentRun++;
                delimiterLength = Math.Max(delimiterLength, currentRun + 1);
                continue;
            }

            currentRun = 0;
        }

        var delimiter = new string('`', delimiterLength);
        var needsPadding = value.StartsWith("`", StringComparison.Ordinal) ||
                           value.EndsWith("`", StringComparison.Ordinal);
        var content = needsPadding ? $" {value} " : value;
        return $"{delimiter}{content}{delimiter}";
    }

    private static string NormalizeListDescription(string? value)
    {
        var text = value ?? string.Empty;
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", " ").Trim();
    }

    private static string BuildDependenciesMarkdown(ModuleInfoModel module)
    {
        // Compose a markdown table with Kind, Name, Version, Guid
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Kind | Name | Version | Guid |");
        sb.AppendLine("|:-----|:-----|:--------|:-----|");
        foreach (var d in module.Dependencies)
        {
            var kind = d.Kind == DocumentationDocumentationModuleDependencyKind.External ? "External" : "Required";
            var name = EscapePipe(d.Name);
            var ver  = EscapePipe(d.Version ?? string.Empty);
            var guid = EscapePipe(d.Guid ?? string.Empty);
            sb.AppendLine($"| {kind} | {name} | {ver} | {guid} |");
        }
        return sb.ToString();
    }

    private static string EscapePipe(string s)
    {
        return (s ?? string.Empty).Replace("|", "\\|");
    }

    private static string EmojiForCommand(string name)
    {
        if (string.IsNullOrEmpty(name)) return "⚙️";
        if (name.StartsWith("Get-", StringComparison.OrdinalIgnoreCase)) return "🔎";
        if (name.StartsWith("Set-", StringComparison.OrdinalIgnoreCase)) return "🛠️";
        if (name.StartsWith("New-", StringComparison.OrdinalIgnoreCase)) return "✨";
        if (name.StartsWith("Remove-", StringComparison.OrdinalIgnoreCase)) return "🗑️";
        if (name.StartsWith("Show-", StringComparison.OrdinalIgnoreCase)) return "📄";
        if (name.StartsWith("Install-", StringComparison.OrdinalIgnoreCase)) return "⬇️";
        if (name.StartsWith("Export-", StringComparison.OrdinalIgnoreCase)) return "📤";
        if (name.StartsWith("Import-", StringComparison.OrdinalIgnoreCase)) return "📥";
        if (name.StartsWith("Test-", StringComparison.OrdinalIgnoreCase)) return "🧪";
        if (name.StartsWith("Start-", StringComparison.OrdinalIgnoreCase)) return "▶️";
        if (name.StartsWith("Stop-", StringComparison.OrdinalIgnoreCase)) return "⏹️";
        if (name.StartsWith("Update-", StringComparison.OrdinalIgnoreCase)) return "🔄";
        if (name.StartsWith("Invoke-", StringComparison.OrdinalIgnoreCase)) return "⚡";
        return "⚙️";
    }
}
