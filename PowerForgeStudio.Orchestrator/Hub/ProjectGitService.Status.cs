using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class ProjectGitService
{
    private static (string? Branch, string? Upstream, int Ahead, int Behind,
        List<GitFileChange> Staged, List<GitFileChange> Unstaged, List<GitFileChange> Untracked)
        ParseFileChanges(string output)
    {
        string? branch = null, upstream = null;
        var ahead = 0;
        var behind = 0;
        var staged = new List<GitFileChange>();
        var unstaged = new List<GitFileChange>();
        var untracked = new List<GitFileChange>();
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.StartsWith("# branch.head ", StringComparison.Ordinal)) branch = record[14..];
            else if (record.StartsWith("# branch.upstream ", StringComparison.Ordinal)) upstream = record[18..];
            else if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var values = record[12..].Split(' ');
                if (values.Length == 2) { int.TryParse(values[0], out ahead); int.TryParse(values[1], out behind); behind = Math.Abs(behind); }
            }
            else if (record.StartsWith("? ", StringComparison.Ordinal)) untracked.Add(new GitFileChange(record[2..], GitChangeKind.Untracked));
            else if (record.StartsWith("1 ", StringComparison.Ordinal) || record.StartsWith("2 ", StringComparison.Ordinal) || record.StartsWith("u ", StringComparison.Ordinal))
            {
                var renamed = record[0] == '2';
                var conflict = record[0] == 'u';
                var fields = record.Split(' ', renamed ? 10 : conflict ? 11 : 9, StringSplitOptions.None);
                if (fields.Length != (renamed ? 10 : conflict ? 11 : 9) || fields[1].Length != 2)
                    throw new InvalidOperationException("Git returned an incomplete status record.");
                var path = fields[^1];
                string? original = null;
                if (renamed)
                {
                    if (++i >= records.Length) throw new InvalidOperationException("Git returned an incomplete rename record.");
                    original = records[i];
                }
                if (fields[1][0] != '.') staged.Add(new GitFileChange(path, conflict ? GitChangeKind.Unmerged : ParseChangeChar(fields[1][0]), OriginalPath: original));
                if (fields[1][1] != '.') unstaged.Add(new GitFileChange(path, conflict ? GitChangeKind.Unmerged : ParseChangeChar(fields[1][1]), OriginalPath: original));
            }
        }
        return (branch, upstream, ahead, behind, staged, unstaged, untracked);
    }

    private static GitChangeKind ParseChangeChar(char value) => value switch
    {
        'A' => GitChangeKind.Added, 'D' => GitChangeKind.Deleted, 'R' => GitChangeKind.Renamed,
        'C' => GitChangeKind.Copied, 'U' => GitChangeKind.Unmerged, _ => GitChangeKind.Modified
    };
}
